import {notify} from "../../notification.js";

export const playerView = () => ({
    currentChannel: null,
    languages: [],
    selectedLanguage: null,
    hls: null,

    get currentTitle() {
        return this.currentChannel?.displayName ?? "";
    },

    get currentLogo() {
        return this.currentChannel
            ? `data:image/png;base64,${this.currentChannel.logo}`
            : "";
    },

    async playChannel(channel, audioStreamIndex = null) {
        this.currentChannel = channel;
        const streamId = await this.startStream(channel.channelId, audioStreamIndex);
        if (streamId) {
            this.selectedLanguage = audioStreamIndex;
            this.startHls(streamId);
            this.safeLastChannelInfo(streamId, audioStreamIndex);
        }
    },

    safeLastChannelInfo(streamId, audioStreamIndex) {
        localStorage.setItem("channel-id", this.currentChannel.channelId);
        localStorage.setItem("audio-stream-index", audioStreamIndex);
    },

    async onLanguageChange(audioStreamIndex) {
        if (!this.currentChannel) {
            return;
        }

        await this.playChannel(this.currentChannel, audioStreamIndex);
    },

    startHls(streamId) {
        const player = document.getElementById("player");

        if (this.hls) {
            this.hls.destroy();
        }

        if (!Hls.isSupported()) {
            notify("Hls is not supported.", "Playing hls streams is not supported in this browser", "error");
            return;
        }

        this.hls = new Hls();

        this.hls.on(Hls.Events.MANIFEST_PARSED, () => {
            player.play().catch(err => {
                if (err.name === "NotAllowedError") {
                    notify("Autoplay Error", "Autoplay is currently not allowed, you can allow it in your browser.", "error");
                } else if (err.name !== "AbortError") {
                    notify("Playback error", "Something went wrong", "error");
                }
            });
        });

        this.hls.loadSource(`/api/streaming/playlist?streamId=${streamId}`);
        this.hls.attachMedia(player);
    },

    async startStream(channelId, audioStreamIndex = null) {
        try {
            const params = new URLSearchParams({channelId});

            if (audioStreamIndex !== null) {
                params.set("audioStreamIndex", audioStreamIndex);
            }

            const res = await fetch(`/api/streaming/start-stream?${params}`);
            const {streamId, languages} = await res.json();
            this.languages = languages ?? [];
            return streamId;
        } catch (err) {
            notify("Failed to start stream.", "Something went wrong", "error");
            return null;
        }
    }
})
