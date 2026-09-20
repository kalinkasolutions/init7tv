import {whileShowing} from '../../whileShowing.js';

export const dashboardView = () => ({
    streams: [],
    recordings: [],
    /// The bars have to creep along between passes, and a pass only happens when something changes:
    /// without a clock of its own this would sit still for hours.
    now: Date.now(),
    connection: null,
    ticker: null,

    init() {
        // a socket held open for a page nobody is looking at is a socket for nothing
        whileShowing(this, 'dashboard', {enter: () => this.connect(), leave: () => this.disconnect()});
    },

    destroy() {
        this.disconnect();
    },

    async connect() {
        if (this.connection) {
            return;
        }

        this.ticker = setInterval(() => (this.now = Date.now()), 30_000);

        this.connection = new window.signalR.HubConnectionBuilder()
            .withUrl('/hub/admin/dashboard')
            .withAutomaticReconnect()
            .build();

        this.connection.on('DashboardUpdate', streams => (this.streams = streams));
        this.connection.on('RecordingUpdate', recordings => (this.recordings = recordings));

        try {
            await this.connection.start();
        } catch (error) {
            console.error('SignalR connection failed:', error);
        }
    },

    async disconnect() {
        clearInterval(this.ticker);
        this.ticker = null;

        const connection = this.connection;
        this.connection = null;
        await connection?.stop();
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

    until(recording) {
        return new Date(recording.scheduledEnd)
            .toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false});
    }
});
