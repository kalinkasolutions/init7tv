export function notify(title, message, type) {
    window.dispatchEvent(new CustomEvent('notify-error', {
        detail: {
            title,
            message,
            type
        }
    }));
}