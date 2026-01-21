export const player = () => ({
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
        this.selectedLanguage = audioStreamIndex;

        const streamId = await this.startStream(channel.hlsUrl, audioStreamIndex);
        if (streamId) {
            this.startHls(streamId);
        }
    },

    async onLanguageChange(audioStreamIndex) {
        if (!this.currentChannel) return;
        await this.playChannel(this.currentChannel, audioStreamIndex);
    },

    startHls(streamId) {
        const player = document.getElementById("player");

        if (this.hls) {
            this.hls.destroy();
        }

        if (!Hls.isSupported()) {
            console.error("HLS not supported");
            return;
        }

        this.hls = new Hls();
        this.hls.loadSource(`/api/streaming/playlist?streamId=${streamId}`);
        this.hls.attachMedia(player);

        this.hls.on(Hls.Events.MANIFEST_PARSED, () => {
            player.play().catch(err => {
                if (err.name !== "AbortError") {
                    console.error("Playback error:", err);
                }
            });
        });
    },

    async startStream(streamUrl, audioStreamIndex = null) {
        try {
            const params = new URLSearchParams({streamUrl});
            if (audioStreamIndex !== null) {
                params.set("audioStreamIndex", audioStreamIndex);
            }

            const res = await fetch(`/api/streaming/start-stream?${params}`);
            if (!res.ok) throw new Error("Failed to start stream");

            const {streamId, languages} = await res.json();
            this.languages = languages ?? [];
            return streamId;
        } catch (err) {
            console.error("Failed to start stream:", err);
            return null;
        }
    }
})
