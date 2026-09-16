import {epgFor, warm, sweepOldGuides} from '../../epgCache.js';

/// What has been picked, until there is somewhere to send it. Kept in the
/// browser on purpose: the page is the whole feature for now, and a list that
/// survives a reload is the only way to tell whether picking one works.
const STORE = 'planned-recordings';

export const recordingView = () => ({
    channel: null,
    epg: [],
    day: 'today',
    loading: false,
    planned: [],

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
        this.day = 'today';
        await this.load();
    },

    async showDay(day) {
        if (this.day === day) {
            return;
        }

        this.day = day;
        await this.load();
    },

    async load() {
        if (!this.channel) {
            return;
        }

        const canonicalName = this.channel.canonicalName;
        const tomorrow = this.day === 'tomorrow';

        this.loading = true;
        try {
            this.epg = await epgFor(canonicalName, tomorrow);
        } finally {
            this.loading = false;
        }

        // the other day is usually the next thing asked for
        warm(canonicalName, !tomorrow);
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
