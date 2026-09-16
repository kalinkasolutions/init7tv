import {get, put} from '../../requestHandler.js';

export const sidebarView = () => ({
    channels: [],
    allChannels: [],
    searchTerm: "",
    selectedChannel: null,

    async init() {
        this.allChannels = await get("/api/streaming/channels") ?? [];
        this.showChannels();

        // the tv page picks up where it left off; choosing what to record is a
        // decision, and starting on whatever was last watched makes it look like
        // one has already been made
        if (this.$store.ui.restoreLastChannel) {
            this.dispatchLastWatchedChannel();
        }
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
