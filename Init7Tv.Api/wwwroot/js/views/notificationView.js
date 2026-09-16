// crypto.randomUUID only exists in a secure context, and the app is reachable
// over plain http behind a proxy; ids only need to be unique within this list
let nextNotificationId = 0;

export const notificationView = () => ({
    maxNotifications: 4,
    timeOutSeconds: 5,
    notifications: [],
    isSuccess: false,

    show(payload) {
        if (this.notifications.length === this.maxNotifications) {
            this.removeFirst();
        }

        const id = ++nextNotificationId;
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