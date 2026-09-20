/// Says something to the viewer. Fire and forget: the one place that shows them is listening, and
/// nothing waits for it.
export function notify(title, message, type) {
    window.dispatchEvent(new CustomEvent('notify', {detail: {title, message, type}}));
}
