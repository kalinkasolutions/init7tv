import {get} from '../../requestHandler.js';

export const sidebarView = () => ({
    channels: [],
    selectedChannel: null,

    async init() {
        this.channels = await get("/api/streaming/channels") ?? [];
        this.dispatchLastWatchedChannel();
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
            tvName: localStorage.getItem("tv-name"),
            audioStreamIndex: Number(JSON.parse(localStorage.getItem("audio-stream-index")))
        }
    },
})
