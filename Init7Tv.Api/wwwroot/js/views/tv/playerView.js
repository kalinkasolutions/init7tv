import {get} from '../../requestHandler.js';
import {notify} from '../../notification.js';
import {whileShowing} from '../../whileShowing.js';
import {playHls} from '../../hlsPlayer.js';

export const playerView = () => ({
    /// What is playing, or being started. Not the same as the channel the list points at, which is
    /// what it should be playing.
    channel: null,
    languages: [],
    language: null,
    loading: false,
    hls: null,
    streamId: null,
    starting: null,
    events: null,

    init() {
        // Watching is whatever the channel list points at, playing, for as long as this is the view
        // being looked at. Leaving stops it: a stream nobody can see still costs a transcode.
        whileShowing(this, 'tv', {enter: () => this.sync(), leave: () => this.stop()});

        // the list arriving, the list moving, or another language being chosen: each of them can
        // leave what is playing behind what should be
        this.$watch('$store.channels.all', () => this.sync());
        this.$watch('$store.channels.current', () => this.sync());
        this.$watch('$store.channels.audio', () => this.sync());

        // the server tears a stream down when ffmpeg exits; without this the
        // player just stalls with no explanation
        this.events = new EventSource('/api/streaming/events');
        this.events.addEventListener('streams', event => {
            const {streamIds} = JSON.parse(event.data);
            this.checkStillRunning(streamIds ?? []);
        });
    },

    destroy() {
        this.events?.close();
    },

    /// Makes what is playing equal to what the list points at. Everything that could make those two
    /// differ ends up here, so they can land together and asking for what is already on does
    /// nothing.
    sync() {
        if (!this.$store.view.is('tv')) {
            return;
        }

        this.$store.channels.resumeLastWatched();

        const wanted = this.$store.channels.current;
        const audio = this.$store.channels.audio;

        if (!wanted || (this.channel?.channelId === wanted.channelId && this.language === audio)) {
            return;
        }

        this.play(wanted, audio);
    },

    async play(channel, audio) {
        // the server stops the previous stream as soon as this one is asked for,
        // so keep the old player from polling a playlist that is already gone
        this.stop();

        this.channel = channel;
        this.language = audio;

        const start = this.starting = new AbortController();
        this.loading = true;

        try {
            const streamId = await this.startStream(channel.channelId, audio, start.signal);

            if (streamId) {
                this.streamId = streamId;
                this.startHls(streamId);
                this.$store.channels.rememberAsWatched(channel, audio);
            }
        } finally {
            // a superseded start must not clear the spinner the newer one put up
            if (this.starting === start) {
                this.starting = null;
                this.loading = false;
            }
        }
    },

    async startStream(channelId, audio, signal) {
        const params = new URLSearchParams({channelId});

        if (audio !== null) {
            params.set('audioStreamIndex', audio);
        }

        // 409 is this start being superseded, by this browser or by another one
        // signed in as the same viewer. Nothing for them to act on.
        const stream = await get(`/api/streaming/start-stream?${params}`, {signal, quietStatuses: [409]});

        if (!stream) {
            return null;
        }

        this.languages = stream.languages ?? [];
        return stream.streamId;
    },

    stop() {
        // a start still waiting for its first segments would otherwise arrive after this and play
        // on over a channel that has been left, or a view that has been
        this.starting?.abort();
        this.starting = null;
        this.loading = false;
        this.streamId = null;
        this.channel = null;

        // they describe the channel being left, and showing them against the one
        // arriving is worse than showing nothing for the moment it takes
        this.languages = [];

        this.stopHls();

        // destroying hls leaves the last decoded frame on screen
        this.$refs.video.removeAttribute('src');
        this.$refs.video.load();
    },

    stopHls() {
        this.hls?.destroy();
        this.hls = null;
    },

    checkStillRunning(streamIds) {
        // streamId is null while a channel switch is in flight, when the server
        // legitimately reports us as watching nothing
        if (!this.streamId || streamIds.includes(this.streamId)) {
            return;
        }

        this.ended();
    },

    /// Nothing more is coming, whether hls gave up or the server says the stream has stopped.
    ended() {
        this.stop();
        notify('Stream ended', 'The channel stopped streaming.', 'error');
    },

    startHls(streamId) {
        this.stopHls();

        this.hls = playHls(this.$refs.video, `/api/streaming/playlist?streamId=${streamId}`, {
            tuning: {
                // as far back as the server has ready when a channel opens, and no
                // further: these are counts of segments, so they track their length
                liveSyncDurationCount: 2,
                liveMaxLatencyDurationCount: 6
            },
            // hls giving up says the same thing to a viewer as the server saying the stream is gone
            onFatal: () => this.ended()
        });
    },

    get title() {
        return this.channel?.displayName ?? '';
    },

    get logo() {
        return this.channel ? `/api/streaming/channels/${this.channel.channelId}/logo` : '';
    }
});
