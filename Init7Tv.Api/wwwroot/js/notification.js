export const notification = () => ({
    maxNotifications: 4,
    timeOutSeconds: 5,
    notifications: [],
    isSuccess: false,

    show(payload) {
        if (this.notifications.length === this.maxNotifications) {
            this.removeFirst();
        }

        const id = crypto.randomUUID();
        this.notifications.push({
            id: id,
            isSuccess: payload.type !== "error",
            ...payload,
        });

        setTimeout(() => {
            this.closeNotification(id)
        }, this.timeOutSeconds * 1000);
    },
    closeNotification(notificationId) {
        this.notifications = this.notifications.filter(n => n.id !== notificationId);
    },

    removeFirst() {
        this.notifications = this.notifications.slice(1);
    }
})