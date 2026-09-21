import {get} from '../requestHandler.js';

/// What the recording lists have to know about each other, and what the player has to know to
/// offer a record button.
export function recordingStore() {
    return {
        /// Programmes being captured right now, so a pick that is under way can be left out of
        /// Planned rather than shown in both places.
        underway: [],
        /// Channels being captured right now, so the player can say so on the one it is showing.
        /// Ids rather than recordings: the player only ever asks whether this channel is one of them.
        channels: [],
        /// Bumped whenever a recording starts, stops or finishes. A recording finishing takes its
        /// pick with it, so the planned list watches this to know it has gone stale.
        changedAt: 0,

        changed() {
            this.changedAt = Date.now();
        },

        /// Reads the list afresh. The event says only that something moved, so this is what turns
        /// that into what moved.
        async load() {
            const recordings = await get('/api/recording/recordings');
            if (recordings === null) {
                return;
            }

            const running = recordings.filter(x => x.state === 'Pending' || x.state === 'Recording');

            this.underway = running.map(x => x.programmeId);
            this.channels = [...new Set(running.map(x => x.channelId))];
        },

        isRecording(channelId) {
            return this.channels.includes(channelId);
        }
    };
}
