import {get, postJson, deleteItem} from '../../requestHandler.js';
import {epgFor, warm, sweepOldGuides} from '../../epgCache.js';

/// Where picks lived before there was somewhere to send them. Read once so that
/// anything chosen while it was a browser-only page is not silently lost.
const OLD_STORE = 'planned-recordings';

export const recordingView = () => ({
    channel: null,
    epg: [],
    day: 0,
    loading: false,
    planned: [],
    /// Set while jumping to a pick, so the guide knows which day to open on and
    /// what to scroll to once it has loaded.
    goingTo: null,
    highlightTimer: null,

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
        await this.loadPlanned();
        await this.adoptAnythingPickedBefore();

        // the channel list is the one from the tv page and announces itself the
        // same way; here it waits to be asked
        this.onChannelSelected = event => this.selectChannel(event.detail.channel);
        window.addEventListener('channel-selected', this.onChannelSelected);
    },

    destroy() {
        window.removeEventListener('channel-selected', this.onChannelSelected);
    },

    async selectChannel(channel) {
        this.channel = channel;
        this.day = this.goingTo ? this.goingTo.day : 0;
        await this.load();

        if (this.goingTo) {
            const id = this.goingTo.id;
            this.goingTo = null;
            this.scrollTo(id);
        }
    },

    /// Opens the guide where a pick sits: its channel, its day, scrolled to it.
    openPlanned(entry) {
        this.goingTo = {id: entry.programmeId, day: this.dayOffsetOf(entry.startsAt)};

        if (this.channel?.channelId === entry.channelId) {
            // already here, so nothing will announce a change
            this.selectChannel(this.channel);
            return;
        }

        window.dispatchEvent(new CustomEvent('select-channel', {
            detail: {channelId: entry.channelId}
        }));
    },

    /// Whole days between today and the one a programme starts on, counted
    /// locally so a programme late tonight does not read as tomorrow.
    dayOffsetOf(when) {
        const midnight = date => new Date(date.getFullYear(), date.getMonth(), date.getDate());
        const days = Math.round((midnight(new Date(when)) - midnight(new Date())) / 86_400_000);
        return Math.min(Math.max(days, 0), this.days.length - 1);
    },

    /// The row only exists once the day has been drawn, which is a frame or two
    /// after the guide arrives rather than on the next tick.
    scrollTo(id, attemptsLeft = 20) {
        const row = this.$el.querySelector(`[data-programme="${id}"]`);

        if (!row) {
            if (attemptsLeft > 0) {
                requestAnimationFrame(() => this.scrollTo(id, attemptsLeft - 1));
            }

            return;
        }

        row.scrollIntoView({block: 'center', behavior: 'smooth'});

        // it is one row among forty, so say which one was meant. The timer from a
        // previous jump would otherwise clear this one part way through.
        clearTimeout(this.highlightTimer);
        this.$el.querySelectorAll('.programme.found').forEach(x => x.classList.remove('found'));

        row.classList.add('found');
        this.highlightTimer = setTimeout(
            () => this.$el.querySelectorAll('.programme.found').forEach(x => x.classList.remove('found')),
            2500);
    },

    async showDay(offset) {
        if (this.day === offset) {
            return;
        }

        this.day = offset;
        await this.load();
    },

    async load() {
        if (!this.channel) {
            return;
        }

        const canonicalName = this.channel.canonicalName;
        const day = this.day;

        this.loading = true;
        try {
            this.epg = await epgFor(canonicalName, day);
        } finally {
            this.loading = false;
        }

        // whichever way the viewer steps next is already there
        warm(canonicalName, day + 1);
        if (day > 0) {
            warm(canonicalName, day - 1);
        }
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

    // --- picking ---------------------------------------------------------

    async loadPlanned() {
        this.planned = await get('/api/recording/planned') ?? [];
    },

    isPlanned(programme) {
        return this.planned.some(x => x.programmeId === programme.id);
    },

    async toggle(programme) {
        if (this.hasEnded(programme)) {
            return;
        }

        if (this.isPlanned(programme)) {
            await this.forget(programme.id);
            return;
        }

        const saved = await postJson('/api/recording/planned', this.entryFor(programme));
        if (saved) {
            this.planned = [...this.planned, saved];
        }
    },

    async forget(programmeId) {
        // null is only returned when the request failed, and it has said so
        if (await deleteItem(`/api/recording/planned/${programmeId}`) === null) {
            return;
        }

        this.planned = this.planned.filter(x => x.programmeId !== programmeId);
    },

    async forgetEverything() {
        for (const entry of [...this.planned]) {
            await this.forget(entry.programmeId);
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
            if (this.planned.some(x => x.programmeId === old.id)) {
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

    /// Soonest first, which is the order they will happen in.
    get plannedInOrder() {
        return [...this.planned].sort((a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt));
    },

    plannedOn(entry) {
        const date = new Date(entry.startsAt);
        const day = date.toLocaleDateString([], {weekday: 'short', day: 'numeric', month: 'short'});
        return `${day} ${this.time(entry.startsAt)}`;
    }
});
