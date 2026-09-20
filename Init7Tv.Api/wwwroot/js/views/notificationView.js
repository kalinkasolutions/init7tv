// crypto.randomUUID only exists in a secure context, and the app is reachable
// over plain http behind a proxy; ids only need to be unique within this list
let nextId = 0;

const MOST_AT_ONCE = 4;
const SECONDS_SHOWN = 5;

export const notificationView = () => ({
    notifications: [],

    show({title, message, type}) {
        if (this.notifications.length === MOST_AT_ONCE) {
            this.notifications = this.notifications.slice(1);
        }

        const id = ++nextId;
        this.notifications.push({id, title, message, isSuccess: type !== 'error'});

        setTimeout(() => this.close(id), SECONDS_SHOWN * 1000);
    },

    close(id) {
        this.notifications = this.notifications.filter(n => n.id !== id);
    }
});
