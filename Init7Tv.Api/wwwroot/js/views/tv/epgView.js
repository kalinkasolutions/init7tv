import {get} from '../../requestHandler.js';

export const epgView = () => ({
    epg: [],
    current: null,
    future: [],
    channel: null,

    async getEpg(channel) {
        this.channel = channel;
        this.current = null;
        this.future = [];
        this.tomorrowFetched = false;
        this.epg = await get(`/api/epg/${channel.canonicalName}`) ?? [];
        this.startProgressTimer();
    },

    endsAt(e) {
        const date = new Date(e.upper);
        return `ends at: ${date.toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false})}`;
    },

    beginsAt(e) {
        const date = new Date(e.lower);
        return `starts at: ${date.toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false})}`;
    },

    startProgressTimer() {
        if (this.intervalId) clearInterval(this.intervalId);
        this.intervalId = setInterval(async () => {
            await this.adjustCurrentProgress();
        }, 2_000);
    },

    async adjustCurrentProgress() {
        if (!this.epg.length) {
            return;
        }

        const now = new Date();
        const currentIndex = this.epg.findIndex(
            c => now >= new Date(c.lower) && now <= new Date(c.upper)
        );

        if (currentIndex === -1) {
            return;
        }

        const newCurrent = this.epg[currentIndex];

        if (!this.current || this.current.id !== newCurrent.id) {
            this.current = newCurrent;
            this.future = this.epg.slice(currentIndex + 1, currentIndex + 4);

            if (this.future.length < 3 && !this.tomorrowFetched) {
                this.tomorrowFetched = true;
                const tomorrow = await get(`/api/epg/${this.channel.canonicalName}?tomorrow=true`);
                if (tomorrow?.length) {
                    this.epg = this.epg.concat(tomorrow);
                    this.future = this.epg.slice(currentIndex + 1, currentIndex + 4);
                }
            }
        }

        if (this.current) {
            const lower = new Date(this.current.lower);
            const upper = new Date(this.current.upper);
            const progress = Math.max(0, Math.min(100, ((now - lower) / (upper - lower)) * 100));
            document.querySelector('#epg .epg-data')?.style.setProperty('--progress', `${progress}%`);
        }
    }
})
