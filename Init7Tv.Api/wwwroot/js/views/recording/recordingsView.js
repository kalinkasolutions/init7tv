import {get, post, deleteItem} from '../../requestHandler.js';
import {notify} from '../../notification.js';
import {whileShowing} from '../../whileShowing.js';
import {playHls} from '../../hlsPlayer.js';
import {subscribe} from '../../eventSource.js';

/// States the scheduler is still working on.
const BUSY = ['Pending', 'Recording', 'Finalizing'];

export const recordingsView = () => ({
    recordings: [],
    /// The one open in the player, or null when it is closed.
    playing: null,
    events: null,
    hls: null,
    /// Where the advertising falls in whatever is open, in seconds from its start.
    breaks: [],
    /// Which break the viewer waved away. One at a time: waving one off says nothing about the next.
    dismissed: null,
    /// Re-read while playing so the skip button knows when a break has been reached.
    at: 0,

    init() {
        // A pick turns into a recording with nobody touching this page, so the server says when
        // that happens — but only while somebody is looking, and the first thing it says on
        // connecting is "look again", which is what fills the list.
        whileShowing(this, 'recording', {
            enter: () => this.listen(),
            leave: () => this.rest()
        });

        this.$watch('recordings', () => this.publishCounts());
    },

    destroy() {
        this.rest();
    },

    listen() {
        if (this.events) {
            return;
        }

        this.events = subscribe('/api/recording/events', {changed: () => this.load()});
    },

    /// Nothing is happening on screen, so nothing needs to be listened for or played.
    rest() {
        this.events?.close();
        this.events = null;
        this.stopPlaying();
    },

    async load() {
        const before = this.states();

        this.recordings = await get('/api/recording/recordings') ?? [];
        this.$store.filter.offer(this.recordings.map(x => x.userName));

        if (before !== this.states()) {
            this.$store.recordings.changed();
        }
    },

    states() {
        return this.recordings.map(x => `${x.recordingId}:${x.state}`).join();
    },

    /// The tabs say how many without having to be opened, which is the point of having one for what
    /// is under way.
    publishCounts() {
        this.$store.tabs.counts.recording = this.inProgress.length;
        this.$store.tabs.counts.recorded = this.finished.length;
        this.$store.recordings.underway = this.inProgress.map(x => x.programmeId);
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

    fileUrl(recording, withoutAds = false) {
        return `/api/recording/recordings/${recording.recordingId}/file?withoutAds=${withoutAds}`;
    },

    /// A finished recording is an mp4 the browser plays on its own. One still being written is the
    /// transport stream on disk, described as byte ranges, which needs hls.js to demux it.
    needsHls(recording) {
        return this.isBusy(recording);
    },

    /// What the video element is pointed at, which for one still recording is nothing: hls.js feeds
    /// it instead.
    sourceFor(recording) {
        return this.needsHls(recording) ? null : this.fileUrl(recording);
    },

    /// Anything with a whole segment on disk can be watched, which for one under way is everything
    /// recorded up to a few seconds ago.
    canPlay(recording) {
        return recording.isPlayable || this.isBusy(recording);
    },

    async play(recording) {
        this.playing = recording;
        this.at = 0;
        this.dismissed = null;
        this.breaks = await get(`/api/recording/recordings/${recording.recordingId}/ad-breaks`) ?? [];

        if (this.needsHls(recording)) {
            // the element only exists once the overlay has been drawn
            this.$nextTick(() => this.startHls(recording));
        }
    },

    startHls(recording) {
        this.stopHls();

        // A growing capture is a live playlist, which is what has hls.js come back for new segments
        // and append them without a gap. Left alone it would also drag the playhead to within a few
        // seconds of the end and refuse to be moved off it, so it is told to target an hour behind:
        // far enough back that it never pulls, which is the point of watching what has already been
        // recorded.
        const behind = 3600;

        const playlist = `/api/recording/recordings/${recording.recordingId}/playlist.m3u8`;

        this.hls = playHls(this.$refs.video, playlist, {
            tuning: {
                liveSyncDuration: behind,
                liveMaxLatencyDuration: behind * 24,
                backBufferLength: Infinity
            },
            onFatal: () => this.gone(recording)
        });
    },

    stopHls() {
        this.hls?.destroy();
        this.hls = null;
    },

    stopPlaying() {
        this.stopHls();
        this.playing = null;
        this.breaks = [];
        this.dismissed = null;
    },

    /// Called as the picture moves, which is what the skip button watches.
    moved(video) {
        this.at = video.currentTime;
    },

    /// The break being watched right now, unless it was waved away.
    get inBreak() {
        return this.breaks.find(gap =>
            this.at >= gap.startsAt && this.at < gap.endsAt && this.dismissed !== gap.startsAt) ?? null;
    },

    skipBreak() {
        const gap = this.inBreak;

        if (gap) {
            this.$refs.video.currentTime = gap.endsAt;
        }
    },

    /// Waving it away leaves the advertising playing and says nothing about the next break.
    dismissBreak() {
        this.dismissed = this.inBreak?.startsAt ?? null;
    },

    /// The player gave up. One still being written has usually only just started and has no whole
    /// segment to hand out yet; anything else has lost the file it was pointed at, which can happen
    /// between the list being drawn and the button being pressed.
    async gone(recording) {
        if (this.isBusy(recording)) {
            notify('Nothing to play yet', `${recording.title} has only just started recording.`, 'error');
        } else {
            notify('Not there any more', `${recording.title} is no longer on disk.`, 'error');
        }

        this.stopPlaying();
        await this.load();
    },

    /// The mp4 is made as it is sent rather than kept, so this is a plain navigation: the browser
    /// streams it to disk instead of the page holding gigabytes in memory.
    download(recording, withoutAds) {
        window.location = this.fileUrl(recording, withoutAds);
    },

    /// Letting go of one still running. What it has caught so far is kept either way: if nobody else
    /// is waiting for it the capture ends, and if somebody is, their part carries on without you.
    async stop(recording) {
        const shared = recording.sharedWith.length
            ? ` ${recording.sharedWith.join(' and ')} also asked for it, so it keeps recording for them.`
            : '';

        const confirmed = await this.$store.modal.show(
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
        const confirmed = await this.$store.modal.show(
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
