import {get, deleteItem} from '../../requestHandler.js';

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

    async init() {
        await this.load();
        this.schedule();

        this.$watch('recordings', () => this.publishCounts());
        this.publishCounts();
    },

    /// The tabs say how many without having to be opened, which is the point of
    /// having one for what is under way.
    publishCounts() {
        this.$store.tabs.counts.recording = this.inProgress.length;
        this.$store.tabs.counts.recorded = this.finished.length;
    },

    destroy() {
        clearTimeout(this.timer);
    },

    async load() {
        this.recordings = await get('/api/recording/recordings') ?? [];
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
            default:
                return 'failed';
        }
    },

    fileUrl(recording, download = false) {
        return `/api/recording/recordings/${recording.recordingId}/file${download ? '?download=true' : ''}`;
    },

    play(recording) {
        this.playing = recording;
    },

    stopPlaying() {
        this.playing = null;
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
        return this.$store.tabs.is('recording') ? this.inProgress : this.finished;
    }
});
