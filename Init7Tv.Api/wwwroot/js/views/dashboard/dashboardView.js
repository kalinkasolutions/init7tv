export const dashboardView = () => {
    return {
        streams: [],
        recordings: [],
        /// The bar has to creep along between passes, and a pass only happens when
        /// something changes: without a clock of its own it would sit still for hours.
        now: Date.now(),

        async init() {
            this.ticker = setInterval(() => (this.now = Date.now()), 30_000);

            this.connection = new signalR.HubConnectionBuilder()
                .withUrl("/hub/admin/dashboard")
                .withAutomaticReconnect()
                .build();

            this.connection.on("DashboardUpdate", (streams) => {
                this.streams = streams;
            });

            this.connection.on("RecordingUpdate", (recordings) => {
                this.recordings = recordings;
            });

            try {
                await this.connection.start();
            } catch (err) {
                console.error("SignalR connection failed:", err);
            }
        },

        /// How far through it is, which is the one thing a still picture cannot say.
        elapsed(recording) {
            const {done, total} = this.span(recording);
            return `${done} of ${total} min`;
        },

        percent(recording) {
            const {done, total} = this.span(recording);
            return Math.round((done / total) * 100);
        },

        span(recording) {
            const from = Date.parse(recording.startedAt);
            const to = Date.parse(recording.scheduledEnd);
            const total = Math.max(Math.round((to - from) / 60000), 1);
            const done = Math.min(Math.max(Math.round((this.now - from) / 60000), 0), total);

            return {done, total};
        },

        destroy() {
            clearInterval(this.ticker);
        },

        until(recording) {
            return new Date(recording.scheduledEnd)
                .toLocaleTimeString([], {hour: "2-digit", minute: "2-digit", hour12: false});
        }
    }
}
