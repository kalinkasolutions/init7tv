import {notify} from "./notification.js";

export async function getJson(url) {
    try {
        const res = await fetch(url);
        if (res.redirected) {
            window.location = res.url;
            return null;
        }
        if (res.ok) {
            return res.json();
        }
        if (res.status === 404) {
            notify("Not Found", `${url} was not found.`, "error");
        }
    } catch (e) {w
        notify("Failed to load channels", e.message, "error");
    }
    return null;
}
