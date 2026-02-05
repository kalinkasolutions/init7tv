export const dashboardView = () => {
    return {
        streams: [],

        async init() {
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl("/hub/admin/dashboard")
                .withAutomaticReconnect()
                .build();

            this.connection.on("DashboardUpdate", (streams) => {
                this.streams = streams;
            });

            try {
                await this.connection.start();
            } catch (err) {
                console.error("SignalR connection failed:", err);
            }
        }
    }
}
