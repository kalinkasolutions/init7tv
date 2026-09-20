import {get, postJson, deleteItem} from '../../requestHandler.js';
import {epgFor, warm, sweepOldGuides} from '../../epgCache.js';
import {whileShowing} from '../../whileShowing.js';

/// Where picks lived before there was somewhere to send them. Read once so that
/// anything chosen while it was a browser-only page is not silently lost.
const OLD_STORE = 'planned-recordings';

export const recordingView = () => ({
    channel: null,
    programmes: [],
    day: 0,
    loading: false,
    planned: [],
    /// Set while jumping to a pick, so the guide knows which day to open on and
    /// what to scroll to once it has loaded.
    goingTo: null,
    /// The row just jumped to. Held as state rather than written onto the element,
    /// because the guide owns that element's classes and a redraw wiped it.
    foundId: null,
    highlight: null,

    /// The source carries full days out to six and part of a seventh.
    days: Array.from({length: 7}, (_, offset) => {
        const date = new Date();
        date.setDate(date.getDate() + offset);
        return {
            offset,
            label: offset === 0 ? 'today'
                : offset === 1 ? 'tomorrow'
                : date.toLocaleDateString([], {weekday: 'short', day: 'numeric', month: 'short'})
        };
    }),

    async init() {
        sweepOldGuides();

        whileShowing(this, 'recording', {enter: () => this.catchUp()});
        this.$watch('$store.channels.current', () => this.catchUp());

        // a recording finishing takes its pick with it, so this list has gone stale
        this.$watch('$store.recordings.changedAt', () => this.loadPlanned());

        // the tab shows how many are waiting without having to be opened. Watching
        // the count rather than the picks, because one starting to record changes it
        // without the picks themselves changing at all.
        this.$watch('waiting.length', count => (this.$store.tabs.counts.planned = count));

        await this.loadPlanned();
        await this.adoptAnythingPickedBefore();
        this.$store.tabs.counts.planned = this.waiting.length;
    },

    /// The guide for whatever the channel list points at, fetched only while this is the view being
    /// looked at. A channel chosen while watching was showing meant "watch this", not "move my tab
    /// and load a guide nobody is looking at", so it waits until this view is come back to.
    async catchUp() {
        const channel = this.$store.channels.current;

        if (!this.$store.view.is('recording') || !channel || channel.channelId === this.channel?.channelId) {
            return;
        }

        this.$store.tabs.show('guide');
        await this.show(channel);
    },

    async show(channel) {
        this.channel = channel;
        this.day = this.goingTo ? this.goingTo.day : 0;

        await this.load();

        if (this.goingTo) {
            const id = this.goingTo.id;
            this.goingTo = null;
            this.scrollTo(id, {mark: true});
            return;
        }

        this.scrollTo(this.onNow?.id);
    },

    async load() {
        const {canonicalName} = this.channel;
        const day = this.day;

        this.loading = true;
        try {
            this.programmes = await epgFor(canonicalName, day);
        } finally {
            this.loading = false;
        }

        // whichever way the viewer steps next is already there
        warm(canonicalName, day + 1);
        if (day > 0) {
            warm(canonicalName, day - 1);
        }
    },

    async showDay(offset) {
        if (this.day === offset) {
            return;
        }

        this.day = offset;
        await this.load();
        this.scrollTo(this.onNow?.id);
    },

    /// Opens the guide where a pick sits: its channel, its day, scrolled to it.
    async openPlanned(entry) {
        this.goingTo = {id: entry.programmeId, day: this.dayOffsetOf(entry.startsAt)};
        this.$store.tabs.show('guide');

        if (this.channel?.channelId === entry.channelId) {
            // the list is already pointing at it, so nothing is about to change
            await this.show(this.channel);
            return;
        }

        this.$store.channels.selectById(entry.channelId);
    },

    /// A day of the guide starts at midnight, so opening one lands on hours that are already over.
    /// What is on now is where anybody wants to be, and on a later day that is its first programme.
    ///
    /// The row only exists once the day has been drawn, which is a frame or two after the guide
    /// arrives rather than on the next tick. $root, not $el: this also runs from a pick's own click
    /// handler, where $el is that button.
    scrollTo(id, {mark = false} = {}, attemptsLeft = 60) {
        if (!id) {
            return;
        }

        const row = this.$root.querySelector(`[data-programme="${id}"]`);

        if (!row) {
            if (attemptsLeft > 0) {
                requestAnimationFrame(() => this.scrollTo(id, {mark}, attemptsLeft - 1));
            }

            return;
        }

        row.scrollIntoView({block: mark ? 'center' : 'start', behavior: mark ? 'smooth' : 'auto'});

        if (!mark) {
            return;
        }

        // it is one row among forty, so say which one was meant
        clearTimeout(this.highlight);
        this.foundId = id;
        this.highlight = setTimeout(() => (this.foundId = null), 2500);
    },

    /// Which day of the guide a programme is on. The source cuts its days at UTC
    /// midnight, so counting in local days sends anything airing after midnight
    /// here to a day the programme is not on.
    dayOffsetOf(when) {
        const midnight = date => Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
        const days = Math.round((midnight(new Date(when)) - midnight(new Date())) / 86_400_000);

        return Math.min(Math.max(days, 0), this.days.length - 1);
    },

    // --- what is on when -------------------------------------------------

    time(value) {
        return new Date(value).toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false});
    },

    span(programme) {
        return `${this.time(programme.lower)} - ${this.time(programme.upper)}`;
    },

    minutes(programme) {
        return Math.round((Date.parse(programme.upper) - Date.parse(programme.lower)) / 60000);
    },

    hasEnded(programme) {
        return Date.parse(programme.upper) <= Date.now();
    },

    isOnNow(programme) {
        const now = Date.now();

        return Date.parse(programme.lower) <= now && now < Date.parse(programme.upper);
    },

    get onNow() {
        return this.programmes.find(p => this.isOnNow(p)) ?? this.programmes.find(p => !this.hasEnded(p));
    },

    // --- picking ---------------------------------------------------------

    async loadPlanned() {
        this.planned = await get('/api/recording/planned') ?? [];
        this.$store.filter.offer(this.planned.map(x => x.userName));
    },

    /// The viewer's own pick of a programme, if they made one. An admin is shown everybody's, and
    /// somebody else having picked it says nothing about whether this viewer has: two people who
    /// pick the same programme share one capture, so both may.
    myPick(programmeId) {
        return this.planned.find(x => x.programmeId === programmeId && x.isMine);
    },

    isPlanned(programme) {
        return this.myPick(programme.id) !== undefined;
    },

    async toggle(programme) {
        if (this.hasEnded(programme)) {
            return;
        }

        const mine = this.myPick(programme.id);
        if (mine) {
            await this.forget(mine);
            return;
        }

        const saved = await postJson('/api/recording/planned', this.entryFor(programme));
        if (saved) {
            this.planned = [...this.planned, saved];
        }
    },

    /// Whose pick it is has to be said: an admin is shown everybody's, and the programme id alone
    /// does not say which of them is meant.
    async forget(entry) {
        const url = `/api/recording/planned/${entry.programmeId}?owner=${encodeURIComponent(entry.userName)}`;

        // null is only returned when the request failed, and it has said so
        if (await deleteItem(url) === null) {
            return;
        }

        this.planned = this.planned.filter(
            x => x.programmeId !== entry.programmeId || x.userName !== entry.userName);
    },

    /// An admin is shown everybody's picks, so clearing the list can drop somebody else's work.
    /// Asked first for that reason, the way stopping and deleting a recording are.
    async forgetEverything() {
        const entries = [...this.planned];
        if (entries.length === 0) {
            return;
        }

        const theirs = entries.filter(x => !x.isMine).length;
        const what = entries.length === 1 ? '1 picked programme' : `${entries.length} picked programmes`;

        const confirmed = await this.$store.modal.show(
            'Clear every pick?',
            `${what} will be dropped.${theirs ? ` ${theirs} of them belong to somebody else.` : ''}`);

        if (!confirmed) {
            return;
        }

        for (const entry of entries) {
            await this.forget(entry);
        }
    },

    entryFor(programme) {
        return {
            programmeId: programme.id,
            title: programme.title,
            subTitle: programme.subTitle ?? '',
            startsAt: programme.lower,
            endsAt: programme.upper,
            channelId: this.channel.channelId,
            channelName: this.channel.displayName,
            canonicalName: this.channel.canonicalName
        };
    },

    /// Picks made while this page kept them in the browser are sent on once, so
    /// that nobody loses what they chose yesterday.
    async adoptAnythingPickedBefore() {
        let older;
        try {
            older = JSON.parse(localStorage.getItem(OLD_STORE));
        } catch {
            older = null;
        }

        if (!Array.isArray(older) || older.length === 0) {
            return;
        }

        for (const old of older) {
            if (this.myPick(old.id)) {
                continue;
            }

            await postJson('/api/recording/planned', {
                programmeId: old.id,
                title: old.title ?? '',
                subTitle: old.subTitle ?? '',
                startsAt: old.lower,
                endsAt: old.upper,
                channelId: old.channelId,
                channelName: old.channelName ?? '',
                canonicalName: old.canonicalName ?? ''
            });
        }

        localStorage.removeItem(OLD_STORE);
        await this.loadPlanned();
    },

    /// Still waiting their turn: one already being captured belongs under Recording.
    get waiting() {
        return this.planned.filter(x => !this.$store.recordings.underway.includes(x.programmeId));
    },

    /// Soonest first, which is the order they will happen in.
    get plannedInOrder() {
        return this.waiting
            .filter(x => this.$store.filter.matches(x))
            .sort((a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt));
    },

    plannedMinutes(entry) {
        return Math.round((Date.parse(entry.endsAt) - Date.parse(entry.startsAt)) / 60000);
    },

    plannedOn(entry) {
        const date = new Date(entry.startsAt);
        const day = date.toLocaleDateString([], {weekday: 'short', day: 'numeric', month: 'short'});

        return `${day} ${this.time(entry.startsAt)}`;
    },

    /// The guide is about a channel, the other tabs are not.
    get heading() {
        return this.$store.tabs.is('guide')
            ? (this.channel ? this.channel.displayName : 'Pick a channel')
            : this.$store.tabs.label;
    }
});
