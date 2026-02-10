import {get} from '../../requestHandler.js';

export const epgView = () => ({
    epg: [],
    current: null,
    future: [],

    async getEpg(channel) {
        this.epg = await get(`/api/epg/${channel.channelId}`)
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
        this.intervalId = setInterval(() => {
            this.adjustCurrentProgress();
        }, 2_000);
    },

    adjustCurrentProgress() {
        const now = new Date();
        const newCurrent = this.epg.find(c => now >= new Date(c.lower) && now <= new Date(c.upper));
        if (!this.current || this.current.id !== newCurrent.id) {
            this.current = newCurrent;
            const currentIndex = this.epg.indexOf(this.current);
            this.future = this.epg.slice(currentIndex + 1, currentIndex + 4);
        }

        if (this.current) {
            const lower = new Date(this.current.lower);
            const upper = new Date(this.current.upper);
            const progress = Math.max(0, Math.min(100, ((now - lower) / (upper - lower)) * 100));

            const el = document.querySelector('#epg .epg-data');
            if (el) {
                el.style.setProperty('--progress', `${progress}%`);
            }
        }
    }
})
