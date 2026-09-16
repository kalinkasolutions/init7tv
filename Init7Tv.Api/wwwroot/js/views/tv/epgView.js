import {get} from '../../requestHandler.js';

// The schedule is known up front, so nothing here polls: the view rolls over
// when the current programme actually ends and the progress bar is handed to
// the browser's animation engine.
export const epgView = () => ({
    epg: [],
    current: null,
    future: [],
    channel: null,
    tomorrowFetched: false,
    rolloverId: null,
    progress: null,

    init() {
        // timers are throttled in background tabs and do not run while the
        // machine is asleep, so recheck whenever the page comes back
        this.onVisibilityChange = () => {
            if (!document.hidden) {
                this.selectCurrent();
            }
        };
        document.addEventListener("visibilitychange", this.onVisibilityChange);
    },

    destroy() {
        document.removeEventListener("visibilitychange", this.onVisibilityChange);
        this.stopRollover();
        this.progress?.cancel();
    },

    async getEpg(channel) {
        this.channel = channel;
        this.current = null;
        this.future = [];
        this.tomorrowFetched = false;
        this.epg = await get(`/api/epg/${channel.canonicalName}`) ?? [];
        await this.selectCurrent();
    },

    endsAt(e) {
        const date = new Date(e.upper);
        return `ends at: ${date.toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false})}`;
    },

    beginsAt(e) {
        const date = new Date(e.lower);
        return `starts at: ${date.toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false})}`;
    },

    async selectCurrent() {
        this.stopRollover();

        if (!this.epg.length) {
            return;
        }

        const now = Date.now();
        const index = this.epg.findIndex(e => now >= Date.parse(e.lower) && now <= Date.parse(e.upper));

        if (index === -1) {
            // a gap in the guide, or we ran past the end of what was fetched
            this.current = null;
            this.future = this.epg.filter(e => Date.parse(e.lower) > now).slice(0, 3);
            this.scheduleRecheck(now);
            return;
        }

        this.current = this.epg[index];
        this.future = this.epg.slice(index + 1, index + 4);

        if (this.future.length < 3 && !this.tomorrowFetched) {
            this.tomorrowFetched = true;
            const tomorrow = await get(`/api/epg/${this.channel.canonicalName}?day=1`);
            if (tomorrow?.length) {
                this.epg = this.epg.concat(tomorrow);
                this.future = this.epg.slice(index + 1, index + 4);
            }
        }

        this.$nextTick(() => this.startProgress());
        this.scheduleRollover();
    },

    scheduleRollover() {
        const msLeft = Date.parse(this.current.upper) - Date.now();
        // a small margin so the next programme has definitely started
        this.rolloverId = setTimeout(() => this.selectCurrent(), Math.max(msLeft, 0) + 250);
    },

    scheduleRecheck(now) {
        const next = this.epg.find(e => Date.parse(e.lower) > now);
        const msUntilNext = next ? Date.parse(next.lower) - now : 60_000;
        this.rolloverId = setTimeout(() => this.selectCurrent(), Math.max(msUntilNext, 1_000) + 250);
    },

    stopRollover() {
        if (this.rolloverId) {
            clearTimeout(this.rolloverId);
            this.rolloverId = null;
        }
    },

    startProgress() {
        this.progress?.cancel();
        this.progress = null;

        const fill = this.$refs.progressFill;
        if (!fill || !this.current) {
            return;
        }

        const lower = Date.parse(this.current.lower);
        const duration = Date.parse(this.current.upper) - lower;
        if (!(duration > 0)) {
            return;
        }

        this.progress = fill.animate(
            [{transform: "scaleX(0)"}, {transform: "scaleX(1)"}],
            {duration, fill: "forwards"}
        );

        // seek to how far into the programme we already are
        this.progress.currentTime = Math.min(Math.max(Date.now() - lower, 0), duration);
    }
})
