import {notify} from "./notification.js";

async function request(url, options = {}) {
    try {
        const res = await fetch(url, options);

        if (res.redirected) {
            window.location = res.url;
            return null;
        }

        if (res.ok) {
            return res.status !== 204 ? res.json() : null;
        }

        if (res.status === 404) {
            notify("Not Found", `${url} was not found.`, "error");
            return null;
        }

        const error = await res.json();
        notify(
            "Error",
            Array.isArray(error)
                ? error.map(x => x.description).join(", ")
                : error.message ?? "Request failed",
            "error"
        );
    } catch (e) {
        notify("Error", e.message, "error");
    }

    return null;
}

export function get(url) {
    return request(url);
}

export function postJson(url, body) {
    return request(url, {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify(body)
    });
}

export function putJson(url, body) {
    return request(url, {
        method: "PUT",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify(body)
    });
}

export function deleteItem(url) {
    return request(url, {method: "DELETE"});
}
