import {notify} from "./notification.js";

async function request(url, {quietStatuses = [], ...options} = {}) {
    try {
        const res = await fetch(url, options);

        if (res.redirected) {
            window.location = res.url;
            return null;
        }

        if (res.ok) {
            return res.status !== 204 ? res.json() : null;
        }

        if (!quietStatuses.includes(res.status)) {
            notify("Error", await errorMessage(res, url), "error");
        }
    } catch (e) {
        // the caller gave up on this request on purpose
        if (e.name === "AbortError") {
            return null;
        }

        notify("Error", e.message, "error");
    }

    return null;
}

async function errorMessage(res, url) {
    let body;
    try {
        body = await res.json();
    } catch {
        // not every failure comes from an endpoint, 404s on a bad path return html
        return res.status === 404 ? `${url} was not found.` : `Request failed with status ${res.status}.`;
    }

    if (Array.isArray(body)) {
        return body.map(x => x.description).join(", ");
    }

    return body.title ?? body.error ?? `Request failed with status ${res.status}.`;
}

export function get(url, options) {
    return request(url, options);
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

export function put(url) {
    return request(url, {method: "PUT"});
}

export function deleteItem(url) {
    return request(url, {method: "DELETE"});
}
