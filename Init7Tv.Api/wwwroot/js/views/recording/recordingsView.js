import {get, post, deleteItem} from '../../requestHandler.js';
import {notify} from '../../notification.js';

/// States the scheduler is still working on, which is what decides how often
/// this asks again.
const BUSY = ['Pending', 'Recording', 'Finalizing'];

const BUSY_INTERVAL = 15_000;
const IDLE_INTERVAL = 60_000;

export const recordingsView = () => ({
    recordings: [],
    /// The one open in the player, or null when it is closed.
    playing: null,
    timer: null,
    hls: null,

    async init() {
        await this.load();
        this.schedule();

        this.$watch('recordings', () => this.publishCounts());
        this.publishCounts();
    },

    /// The tabs say how many without having to be opened, which is the point of
    /// having one for what is under way.
    publishCounts() {
        const underway = this.inProgress;

        this.$store.tabs.counts.recording = underway.length;
        this.$store.tabs.counts.recorded = this.finished.length;
        this.$store.recordings.underway = underway.map(x => x.programmeId);
    },

    destroy() {
        clearTimeout(this.timer);
        this.stopHls();
    },

    async load() {
        const before = this.recordings.map(x => `${x.recordingId}:${x.state}`).join();

        this.recordings = await get('/api/recording/recordings') ?? [];
        this.$store.filter.offer(this.recordings.map(x => x.userName));

        // The picks behind a recording are dropped once it finishes, so the planned list is stale
        // the moment any of this changes. It owns its own data and is a component away, so it is
        // told rather than reached into.
        if (before !== this.recordings.map(x => `${x.recordingId}:${x.state}`).join()) {
            window.dispatchEvent(new CustomEvent('recordings-changed'));
        }
    },

    /// A pick turns into a recording with nobody touching this page, so the list
    /// has to notice on its own; it just looks more often while something is
    /// actually happening.
    schedule() {
        clearTimeout(this.timer);
        this.timer = setTimeout(async () => {
            await this.load();
            this.schedule();
        }, this.recordings.some(x => this.isBusy(x)) ? BUSY_INTERVAL : IDLE_INTERVAL);
    },

    isBusy(recording) {
        return BUSY.includes(recording.state);
    },

    /// What the badge says. Failed and skipped carry their reason instead.
    label(recording) {
        switch (recording.state) {
            case 'Pending':
                return 'starting';
            case 'Recording':
                return 'recording';
            case 'Finalizing':
                return 'finishing';
            case 'Completed':
                return 'recorded';
            case 'Interrupted':
                return 'partial';
            case 'Skipped':
                return 'skipped';
            case 'Missing':
                return 'file gone';
            default:
                return 'failed';
        }
    },

    fileUrl(recording, download = false) {
        return `/api/recording/recordings/${recording.recordingId}/file${download ? '?download=true' : ''}`;
    },

    playlistUrl(recording) {
        return `/api/recording/recordings/${recording.recordingId}/playlist.m3u8`;
    },

    /// A finished recording is an mp4 the browser plays on its own. One still being written is the
    /// transport stream on disk, described as byte ranges, which needs hls.js to demux it.
    needsHls(recording) {
        return this.isBusy(recording);
    },

    /// Anything with a whole segment on disk can be watched, which for one under way is everything
    /// recorded up to a few seconds ago.
    canPlay(recording) {
        return recording.isPlayable || this.isBusy(recording);
    },

    play(recording) {
        this.playing = recording;

        if (!this.needsHls(recording)) {
            return;
        }

        // the element only exists once the overlay has been drawn
        this.$nextTick(() => this.startHls(recording));
    },

    startHls(recording) {
        const video = this.$root.querySelector('.player-box video');

        this.stopHls();

        if (!Hls.isSupported()) {
            notify('Hls is not supported', 'This browser cannot play a recording that is still running', 'error');
            return;
        }

        // A growing capture is a live playlist, which is what has hls.js come back for new segments
        // and append them without a gap. Left alone it would also drag the playhead to within a few
        // seconds of the end and refuse to be moved off it, so it is told to target an hour behind:
        // far enough back that it never pulls, which is the point of watching what has already been
        // recorded.
        const behind = 3600;

        this.hls = new Hls({
            lowLatencyMode: false,
            liveSyncDuration: behind,
            liveMaxLatencyDuration: behind * 24,
            backBufferLength: Infinity,
            maxBufferLength: 30
        });

        this.hls.on(Hls.Events.ERROR, (_, data) => {
            if (data.fatal) {
                this.gone(recording);
            }
        });

        this.hls.loadSource(this.playlistUrl(recording));
        this.hls.attachMedia(video);
    },

    stopHls() {
        if (this.hls) {
            this.hls.destroy();
            this.hls = null;
        }
    },

    /// A file can go between the list being drawn and the button being pressed, and a video element
    /// pointed at nothing just sits there looking broken.
    async gone(recording) {
        notify('Not there any more', `${recording.title} is no longer on disk.`, 'error');

        this.stopPlaying();
        await this.load();
    },

    /// The download is a plain link so the browser streams it rather than the page holding gigabytes
    /// in memory, which also means a failure would navigate to the error instead of reporting it.
    async download(recording) {
        const url = this.fileUrl(recording, true);
        const head = await fetch(url, {method: 'HEAD'});

        if (!head.ok) {
            await this.gone(recording);
            return;
        }

        window.location = url;
    },

    stopPlaying() {
        this.stopHls();
        this.playing = null;
    },

    /// Letting go of one still running. What it has caught so far is kept either way: if nobody else
    /// is waiting for it the capture ends, and if somebody is, their part carries on without you.
    async stop(recording) {
        const shared = recording.sharedWith.length
            ? ` ${recording.sharedWith.join(' and ')} also asked for it, so it keeps recording for them.`
            : '';

        const confirmed = await Alpine.store('modal').show(
            'Stop this recording?',
            `${recording.title} will stop and what has been recorded so far is kept.${shared}`);

        if (!confirmed) {
            return;
        }

        if (await post(`/api/recording/recordings/${recording.recordingId}/stop`) === null) {
            return;
        }

        await this.load();
    },

    async remove(recording) {
        const confirmed = await Alpine.store('modal').show(
            'Delete this recording?',
            `${recording.title} will be removed and the file deleted.`);

        if (!confirmed) {
            return;
        }

        // null is only returned when the request failed, and it has said so
        if (await deleteItem(`/api/recording/recordings/${recording.recordingId}`) === null) {
            return;
        }

        if (this.playing?.recordingId === recording.recordingId) {
            this.stopPlaying();
        }

        this.recordings = this.recordings.filter(x => x.recordingId !== recording.recordingId);
    },

    when(recording) {
        const date = new Date(recording.scheduledStart);
        const day = date.toLocaleDateString([], {weekday: 'short', day: 'numeric', month: 'short'});
        const time = date.toLocaleTimeString([], {hour: '2-digit', minute: '2-digit', hour12: false});
        return `${day} ${time}`;
    },

    size(recording) {
        if (!recording.fileSizeBytes) {
            return '';
        }

        const gigabytes = recording.fileSizeBytes / 1_000_000_000;
        return gigabytes >= 1
            ? `${gigabytes.toFixed(1)} GB`
            : `${Math.round(recording.fileSizeBytes / 1_000_000)} MB`;
    },

    /// Under way now, soonest started first, because the one ending next matters most.
    get inProgress() {
        return this.recordings
            .filter(x => this.isBusy(x))
            .sort((a, b) => Date.parse(a.scheduledStart) - Date.parse(b.scheduledStart));
    },

    /// Everything else, newest first: what was recorded last is what somebody wants to watch.
    get finished() {
        return this.recordings
            .filter(x => !this.isBusy(x))
            .sort((a, b) => Date.parse(b.scheduledStart) - Date.parse(a.scheduledStart));
    },

    /// One component sits behind both tabs, so which list it shows is the tab.
    get visible() {
        const shown = this.$store.tabs.is('recording') ? this.inProgress : this.finished;

        return shown.filter(x => this.$store.filter.matches(x));
    }
});
