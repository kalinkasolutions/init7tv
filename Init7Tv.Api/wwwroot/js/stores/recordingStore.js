/// What the recording lists have to know about each other.
export function recordingStore() {
    return {
        /// Programmes being captured right now, so a pick that is under way can be left out of
        /// Planned rather than shown in both places.
        underway: [],
        /// Bumped whenever a recording starts, stops or finishes. A recording finishing takes its
        /// pick with it, so the planned list watches this to know it has gone stale.
        changedAt: 0,

        changed() {
            this.changedAt = Date.now();
        }
    };
}
