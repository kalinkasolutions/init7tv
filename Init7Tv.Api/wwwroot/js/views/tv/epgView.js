import {epgFor} from '../../epgCache.js';
import {whileShowing} from '../../whileShowing.js';

/// What is on now beside the player, and the three after it.
///
/// Nothing here polls: the schedule is known up front, so the view rolls over when the current
/// programme actually ends and the progress bar is handed to the browser's animation engine.
export const epgView = () => ({
    channel: null,
    programmes: [],
    current: null,
    next: [],
    rollover: null,
    progress: null,

    init() {
        whileShowing(this, 'tv', {enter: () => this.catchUp()});
        this.$watch('$store.channels.current', () => this.catchUp());

        // timers are throttled in background tabs and do not run while the
        // machine is asleep, so recheck whenever the page comes back
        this.onVisible = () => document.hidden || this.selectCurrent();
        document.addEventListener('visibilitychange', this.onVisible);
    },

    destroy() {
        document.removeEventListener('visibilitychange', this.onVisible);
        this.stopRollover();
        this.progress?.cancel();
    },

    /// The guide for whatever the channel list points at, fetched only while watching is the view
    /// being looked at, and caught up with when it is come back to.
    async catchUp() {
        const channel = this.$store.channels.current;

        if (!this.$store.view.is('tv') || !channel || channel.channelId === this.channel?.channelId) {
            return;
        }

        this.channel = channel;
        this.current = null;
        this.next = [];
        this.programmes = await epgFor(channel.canonicalName);

        await this.selectCurrent();
    },

    async selectCurrent() {
        this.stopRollover();

        if (!this.programmes.length) {
            return;
        }

        const now = Date.now();
        const index = this.programmes.findIndex(p => now >= Date.parse(p.lower) && now <= Date.parse(p.upper));

        if (index === -1) {
            // a gap in the guide, or we ran past the end of what was fetched
            this.current = null;
            this.next = this.programmes.filter(p => Date.parse(p.lower) > now).slice(0, 3);
            this.scheduleRecheck(now);
            return;
        }

        this.current = this.programmes[index];
        this.next = this.programmes.slice(index + 1, index + 4);

        if (this.next.length < 3) {
            await this.addTomorrow(index);
        }

        this.$nextTick(() => this.startProgress());
        this.scheduleRollover();
    },

    /// Late in the evening there is not enough of today left to fill the three that come next.
    async addTomorrow(index) {
        const tomorrow = await epgFor(this.channel.canonicalName, 1);

        if (tomorrow.length) {
            this.programmes = this.programmes.concat(tomorrow);
            this.next = this.programmes.slice(index + 1, index + 4);
        }
    },

    scheduleRollover() {
        const left = Date.parse(this.current.upper) - Date.now();
        // a small margin so the next programme has definitely started
        this.rollover = setTimeout(() => this.selectCurrent(), Math.max(left, 0) + 250);
    },

    scheduleRecheck(now) {
        const next = this.programmes.find(p => Date.parse(p.lower) > now);
        const until = next ? Date.parse(next.lower) - now : 60_000;
        this.rollover = setTimeout(() => this.selectCurrent(), Math.max(until, 1_000) + 250);
    },

    stopRollover() {
        clearTimeout(this.rollover);
        this.rollover = null;
    },

    startProgress() {
        this.progress?.cancel();
        this.progress = null;

        const fill = this.$refs.progressFill;
        if (!fill || !this.current) {
            return;
        }

        const from = Date.parse(this.current.lower);
        const duration = Date.parse(this.current.upper) - from;
        if (!(duration > 0)) {
            return;
        }

        this.progress = fill.animate(
            [{transform: 'scaleX(0)'}, {transform: 'scaleX(1)'}],
            {duration, fill: 'forwards'}
        );

        // seek to how far into the programme we already are
        this.progress.currentTime = Math.min(Math.max(Date.now() - from, 0), duration);
    },

    endsAt(programme) {
        return `ends at: ${this.time(programme.upper)}`;
    },

    beginsAt(programme) {
        return `starts at: ${this.time(programme.lower)}`;
    },

    time(value) {
        return new Date(value).toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false});
    }
});
