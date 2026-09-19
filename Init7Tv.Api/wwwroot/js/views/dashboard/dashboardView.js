export const dashboardView = () => {
    return {
        streams: [],
        recordings: [],

        async init() {
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
        progress(recording) {
            const from = Date.parse(recording.startedAt);
            const to = Date.parse(recording.scheduledEnd);
            const done = Math.round((Date.now() - from) / 60000);
            const total = Math.max(Math.round((to - from) / 60000), 1);

            return `${Math.min(Math.max(done, 0), total)} of ${total} min`;
        },

        until(recording) {
            return new Date(recording.scheduledEnd)
                .toLocaleTimeString([], {hour: "2-digit", minute: "2-digit", hour12: false});
        }
    }
}
