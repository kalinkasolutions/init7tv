import {get} from '../../requestHandler.js';

export const sidebarView = () => ({
    channels: [],
    allChannels: [],
    selectedChannel: null,

    async init() {
        this.channels = await get("/api/streaming/channels") ?? [];
        await get("/api/epg/b87abb69-d5ed-44c5-8cab-0f7be4ef51b1")
        this.allChannels = this.channels;
        this.dispatchLastWatchedChannel();
    },

    searchChannel(event) {
        this.channels = this.allChannels.filter(c => c.displayName.toLowerCase().includes(event.target.value.toLowerCase()));
    },

    channelSelected(channel) {
        if (this.selectedChannel) {
            this.selectedChannel.selected = false;
        }
        this.selectedChannel = channel;
        this.selectedChannel.selected = true;
        this.dispatch();
    },

    dispatchLastWatchedChannel() {
        const {tvName, audioStreamIndex} = this.getLastChannelInfo();
        const channel = this.channels.find(ch => ch.tvName === tvName);

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
        return {
            channelId: localStorage.getItem("channel-id"),
            audioStreamIndex: Number(JSON.parse(localStorage.getItem("audio-stream-index")))
        }
    },
})
