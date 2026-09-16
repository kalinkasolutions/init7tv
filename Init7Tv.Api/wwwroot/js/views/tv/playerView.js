import {get} from "../../requestHandler.js";
import {notify} from "../../notification.js";

export const playerView = () => ({
    currentChannel: null,
    languages: [],
    selectedLanguage: null,
    hls: null,
    streamId: null,
    events: null,
    loading: false,
    pendingStart: null,

    init() {
        // the server tears a stream down when ffmpeg exits; without this the
        // player just stalls with no explanation
        this.events = new EventSource("/api/streaming/events");
        this.events.addEventListener("streams", event => {
            const {streamIds} = JSON.parse(event.data);
            this.onViewerStreams(streamIds ?? []);
        });
    },

    destroy() {
        this.events?.close();
    },

    onViewerStreams(streamIds) {
        // streamId is null while a channel switch is in flight, when the server
        // legitimately reports us as watching nothing
        if (!this.streamId || streamIds.includes(this.streamId)) {
            return;
        }

        this.stopPlayback();
        notify("Stream ended", "The channel stopped streaming.", "error");
    },

    stopPlayback() {
        this.streamId = null;

        if (this.hls) {
            this.hls.destroy();
            this.hls = null;
        }

        // destroying hls leaves the last decoded frame on screen
        const player = document.getElementById("player");
        if (player) {
            player.removeAttribute("src");
            player.load();
        }
    },

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

        // the server stops the previous stream as soon as this one is asked for,
        // so keep the old player from polling a playlist that is already gone
        this.stopPlayback();

        // A start still waiting for its first segments is about to be stopped by
        // this one, and would report that as a failure over the channel now
        // playing. The viewer has moved on, so drop it.
        this.pendingStart?.abort();
        const start = this.pendingStart = new AbortController();
        this.loading = true;

        try {
            const streamId = await this.startStream(channel.channelId, audioStreamIndex, start.signal);
            if (streamId) {
                this.selectedLanguage = audioStreamIndex;
                this.startHls(streamId);
                this.streamId = streamId;
                this.saveLastChannelInfo(audioStreamIndex);
            }
        } finally {
            // a superseded start must not clear the spinner the newer one put up
            if (this.pendingStart === start) {
                this.pendingStart = null;
                this.loading = false;
            }
        }
    },

    saveLastChannelInfo(audioStreamIndex) {
        localStorage.setItem("channel-id", this.currentChannel.channelId);
        localStorage.setItem("audio-stream-index", JSON.stringify(audioStreamIndex));
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
            this.hls = null;
        }

        if (!Hls.isSupported()) {
            notify("Hls is not supported.", "Playing hls streams is not supported in this browser", "error");
            return;
        }

        // Defaults are tuned for adaptive VOD. This is a single rendition live
        // stream with short segments, and low latency mode is on by default
        // while the playlist carries no LL-HLS parts for it to use.
        this.hls = new Hls({
            lowLatencyMode: false,
            // as far back as the server has ready when a channel opens, and no
            // further: these are counts of segments, so they track their length
            liveSyncDurationCount: 2,
            liveMaxLatencyDurationCount: 6,
            maxBufferLength: 30
        });

        this.hls.on(Hls.Events.MANIFEST_PARSED, () => {
            player.play().catch(err => {
                if (err.name === "NotAllowedError") {
                    this.waitForUserToPlay(player);
                } else if (err.name !== "AbortError") {
                    notify("Playback error", "Something went wrong", "error");
                }
            });
        });

        this.hls.loadSource(`/api/streaming/playlist?streamId=${streamId}`);
        this.hls.attachMedia(player);
    },

    /// Autoplay was refused. Keep the player from pulling segments nobody is
    /// watching, which otherwise continues for as long as the page is open.
    waitForUserToPlay(player) {
        this.hls?.stopLoad();

        notify(
            "Press play to start",
            "Your browser blocked autoplay for this page.",
            "error"
        );

        player.addEventListener("play", () => this.hls?.startLoad(), {once: true});
    },

    async startStream(channelId, audioStreamIndex = null, signal = null) {
        const params = new URLSearchParams({channelId});

        if (audioStreamIndex !== null) {
            params.set("audioStreamIndex", audioStreamIndex);
        }

        const stream = await get(`/api/streaming/start-stream?${params}`, {signal});
        if (!stream) {
            return null;
        }

        this.languages = stream.languages ?? [];
        return stream.streamId;
    }
})
