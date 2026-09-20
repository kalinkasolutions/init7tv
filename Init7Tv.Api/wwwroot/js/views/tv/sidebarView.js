import {get, put} from '../../requestHandler.js';

/// The channel list changes about as often as the channels do, and every page draws it. Kept for
/// the tab rather than for ever, so switching between the guide and the recordings does not fetch
/// it again while a new tab still picks up anything that has changed.
const STORE = 'channels';

function remembered() {
    try {
        return JSON.parse(sessionStorage.getItem(STORE)) ?? null;
    } catch {
        return null;
    }
}

function remember(channels) {
    try {
        sessionStorage.setItem(STORE, JSON.stringify(channels));
    } catch {
        // a full store is not worth failing over, it only means fetching again
    }
}

export const sidebarView = () => ({
    channels: [],
    allChannels: [],
    searchTerm: "",
    selectedChannel: null,

    async init() {
        // drawn from what is already known first, so moving between pages does not sit empty
        const known = remembered();
        if (known) {
            this.allChannels = known;
            this.showChannels();
        }

        const fetched = await get("/api/streaming/channels");
        if (fetched) {
            this.allChannels = fetched;
            remember(fetched);
            this.showChannels();
        }


        // Watching picks up where it left off. Choosing what to record is a decision, and starting
        // on whatever was last watched makes it look like one has already been made, so it waits
        // until the watching half is the one being looked at.
        this.onViewChanged = event => {
            if (event.detail.view === 'tv' && !this.selectedChannel) {
                this.dispatchLastWatchedChannel();
            }
        };

        window.addEventListener('view-changed', this.onViewChanged);

        if (this.$store.view.is('tv')) {
            this.dispatchLastWatchedChannel();
        }

        // anything that knows a channel only by its id can ask for it, and the
        // list stays the one place that decides what is selected
        this.onSelectChannel = event => {
            const channel = this.allChannels.find(c => c.channelId === event.detail.channelId);
            if (channel) {
                this.channelSelected(channel);
            }
        };
        window.addEventListener('select-channel', this.onSelectChannel);
    },

    destroy() {
        window.removeEventListener('select-channel', this.onSelectChannel);
        window.removeEventListener('view-changed', this.onViewChanged);
    },

    searchChannel(event) {
        this.searchTerm = event.target.value.toLowerCase();
        this.showChannels();
    },

    async toggleFavourite(channel) {
        const isFavourite = !channel.isFavourite;

        if (await put(`/api/streaming/channels/${channel.channelId}/favourite?isFavourite=${isFavourite}`) === null) {
            return;
        }

        channel.isFavourite = isFavourite;
        this.showChannels();
    },

    /// Always built from the list as the server sends it, never from what is on
    /// screen: sorting an already sorted list loses where a channel belongs once
    /// it stops being a favourite. Sorting is stable, so within each group the
    /// channels keep that order.
    showChannels() {
        const matching = this.searchTerm
            ? this.allChannels.filter(c => c.displayName.toLowerCase().includes(this.searchTerm))
            : this.allChannels;

        this.channels = [...matching].sort((a, b) => Boolean(b.isFavourite) - Boolean(a.isFavourite));
    },

    channelSelected(channel) {
        if (this.selectedChannel) {
            this.selectedChannel.selected = false;
        }
        this.selectedChannel = channel;
        this.selectedChannel.selected = true;
        this.dispatch();
        this.$store.ui.closeMenu();
    },

    dispatchLastWatchedChannel() {
        const {channelId, audioStreamIndex} = this.getLastChannelInfo();
        const channel = this.channels.find(ch => ch.channelId === channelId);

        if (channel) {
            this.selectedChannel = channel;
            this.selectedChannel.selected = true;
            this.dispatch(audioStreamIndex);
        }
    },

    dispatch(audioStreamIndex = null) {
        // what the list is pointing at, for the halves of the page that were not looking when it
        // was chosen: the player picks it up when watching is next brought to the front
        this.$store.ui.channel = this.selectedChannel;
        this.$store.ui.audioStreamIndex = audioStreamIndex;

        window.dispatchEvent(new CustomEvent('channel-selected', {
            detail: {channel: this.selectedChannel, audioStreamIndex}
        }));
    },

    getLastChannelInfo() {
        const storedIndex = localStorage.getItem("audio-stream-index");
        const audioStreamIndex = Number(storedIndex);

        return {
            channelId: localStorage.getItem("channel-id"),
            audioStreamIndex: storedIndex === null || Number.isNaN(audioStreamIndex) ? null : audioStreamIndex
        }
    },
})
