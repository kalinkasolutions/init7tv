export async function getJson(url) {
    try {
        const res = await fetch(url);
        if (res.redirected) {
            window.location = res.url;
            return null;
        }
        return res.json();
    } catch (e) {
        window.dispatchEvent(new CustomEvent('notify-error', {
            detail: {
                title: "Failed to load channels",
                message: e.message,
                type: "error"
            }
        }));
    }
    return null;
}
