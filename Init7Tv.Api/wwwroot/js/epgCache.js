import {get} from './requestHandler.js';

/// A day's guide for one channel, kept so that flicking between channels and
/// days costs nothing. Entries are keyed by the date they describe, so they fall
/// out of use on their own at midnight rather than needing to be swept.
const PREFIX = 'epg:';
const KEEP_FOR_HOURS = 12;

function dayKey(offset) {
    const date = new Date();
    date.setDate(date.getDate() + offset);
    return date.toISOString().slice(0, 10);
}

function keyFor(canonicalName, offset) {
    return `${PREFIX}${canonicalName}:${dayKey(offset)}`;
}

function read(key) {
    try {
        const entry = JSON.parse(localStorage.getItem(key));
        if (!entry || !Array.isArray(entry.programmes)) {
            return null;
        }

        // a guide can still be revised during the day it describes
        const age = Date.now() - entry.fetchedAt;
        return age < KEEP_FOR_HOURS * 3600_000 ? entry.programmes : null;
    } catch {
        return null;
    }
}

function write(key, programmes) {
    try {
        localStorage.setItem(key, JSON.stringify({fetchedAt: Date.now(), programmes}));
    } catch {
        // a full store is not worth failing a page load over
        sweep();
    }
}

/// Anything for a day that has passed, which is every key whose date is behind
/// today's.
function sweep() {
    const today = dayKey(0);

    for (const key of Object.keys(localStorage)) {
        if (key.startsWith(PREFIX) && key.slice(key.lastIndexOf(':') + 1) < today) {
            localStorage.removeItem(key);
        }
    }
}

export async function epgFor(canonicalName, tomorrow = false) {
    const offset = tomorrow ? 1 : 0;
    const key = keyFor(canonicalName, offset);

    const cached = read(key);
    if (cached) {
        return cached;
    }

    const query = tomorrow ? '?tomorrow=true' : '';
    const programmes = await get(`/api/epg/${canonicalName}${query}`) ?? [];
    write(key, programmes);
    return programmes;
}

/// Fetched quietly so the other day is already there when it is asked for.
export function warm(canonicalName, tomorrow = false) {
    const key = keyFor(canonicalName, tomorrow ? 1 : 0);

    if (!read(key)) {
        epgFor(canonicalName, tomorrow).catch(() => {});
    }
}

export {sweep as sweepOldGuides};
