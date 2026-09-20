import {get, put} from '../requestHandler.js';

/// The channel list, and which channel the page is pointing at.
///
/// One list for the whole page: watching and choosing what to record both show it and both follow
/// it. What they do about it is their own business, which is why nothing here knows about players
/// or guides.
const KEPT_AS = 'channels';
const LAST_WATCHED = 'channel-id';
const LAST_LANGUAGE = 'audio-stream-index';

export function channelStore() {
    return {
        all: [],
        current: null,
        /// Which audio track was asked for, so a channel comes back in the language it was left in.
        audio: null,

        async load() {
            // drawn from what is already known first, so the list does not sit empty on a reload
            const known = readKept();
            if (known) {
                this.all = known;
            }

            const fetched = await get('/api/streaming/channels');
            if (fetched) {
                this.all = fetched;
                keep(fetched);
            }
        },

        select(channel, audio = null) {
            this.current = channel;
            this.audio = audio;
        },

        /// For anything that knows a channel only by its id, so this stays the one place that
        /// decides what the page is pointing at.
        selectById(channelId) {
            const channel = this.all.find(c => c.channelId === channelId);

            if (channel) {
                this.select(channel);
            }
        },

        isCurrent(channel) {
            return this.current?.channelId === channel.channelId;
        },

        /// Watching picks up where it left off. Choosing what to record is a decision, and opening
        /// on whatever was last watched makes it look like one has already been made, so this is
        /// asked for by the player rather than done here for everybody.
        resumeLastWatched() {
            if (this.current) {
                return;
            }

            const channel = this.all.find(c => c.channelId === localStorage.getItem(LAST_WATCHED));

            if (channel) {
                this.select(channel, lastLanguage());
            }
        },

        /// Said once a stream has actually started, so a channel that could not be played is not
        /// the one the next visit opens on.
        rememberAsWatched(channel, audio) {
            localStorage.setItem(LAST_WATCHED, channel.channelId);
            localStorage.setItem(LAST_LANGUAGE, JSON.stringify(audio));
        },

        async toggleFavourite(channel) {
            const isFavourite = !channel.isFavourite;

            if (await put(`/api/streaming/channels/${channel.channelId}/favourite?isFavourite=${isFavourite}`) === null) {
                return;
            }

            channel.isFavourite = isFavourite;
            keep(this.all);
        }
    };
}

/// The channels change about as often as the channels do. Kept for the tab rather than for ever, so
/// a reload draws immediately while a new tab still picks up anything that has changed.
function readKept() {
    try {
        return JSON.parse(sessionStorage.getItem(KEPT_AS)) ?? null;
    } catch {
        return null;
    }
}

function keep(channels) {
    try {
        sessionStorage.setItem(KEPT_AS, JSON.stringify(channels));
    } catch {
        // a full store is not worth failing over, it only means fetching again
    }
}

function lastLanguage() {
    const stored = localStorage.getItem(LAST_LANGUAGE);
    const index = Number(stored);

    return stored === null || Number.isNaN(index) ? null : index;
}
