import {epgFor, warm, sweepOldGuides} from '../../epgCache.js';

/// What has been picked, until there is somewhere to send it. Kept in the
/// browser on purpose: the page is the whole feature for now, and a list that
/// survives a reload is the only way to tell whether picking one works.
const STORE = 'planned-recordings';

export const recordingView = () => ({
    channel: null,
    epg: [],
    day: 0,
    loading: false,
    planned: [],
    /// Set while jumping to a pick, so the guide knows which day to open on and
    /// what to scroll to once it has loaded.
    goingTo: null,

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

    init() {
        this.planned = this.read();
        sweepOldGuides();

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
        this.goingTo = {id: entry.id, day: this.dayOffsetOf(entry.lower)};

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

        // it is one row among forty, so say which one was meant
        row.classList.add('found');
        setTimeout(() => row.classList.remove('found'), 2500);
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

    isPlanned(programme) {
        return this.planned.some(x => x.id === programme.id);
    },

    toggle(programme) {
        if (this.hasEnded(programme)) {
            return;
        }

        this.planned = this.isPlanned(programme)
            ? this.planned.filter(x => x.id !== programme.id)
            : [...this.planned, this.entryFor(programme)];

        this.write();
    },

    forget(id) {
        this.planned = this.planned.filter(x => x.id !== id);
        this.write();
    },

    forgetEverything() {
        this.planned = [];
        this.write();
    },

    entryFor(programme) {
        return {
            id: programme.id,
            title: programme.title,
            subTitle: programme.subTitle,
            lower: programme.lower,
            upper: programme.upper,
            channelId: this.channel.channelId,
            channelName: this.channel.displayName,
            canonicalName: this.channel.canonicalName
        };
    },

    /// Soonest first, which is the order they will happen in.
    get plannedInOrder() {
        return [...this.planned].sort((a, b) => Date.parse(a.lower) - Date.parse(b.lower));
    },

    plannedOn(entry) {
        const date = new Date(entry.lower);
        const day = date.toLocaleDateString([], {weekday: 'short', day: 'numeric', month: 'short'});
        return `${day} ${this.time(entry.lower)}`;
    },

    read() {
        try {
            const stored = JSON.parse(localStorage.getItem(STORE));
            return Array.isArray(stored) ? stored : [];
        } catch {
            // written by an older version of this page, or by hand
            return [];
        }
    },

    write() {
        localStorage.setItem(STORE, JSON.stringify(this.planned));
    }
});
