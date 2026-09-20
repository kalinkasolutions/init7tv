/// Which of the four lists on the recording view is showing.
export function tabStore() {
    return {
        all: [
            {key: 'guide', label: 'Guide'},
            {key: 'planned', label: 'Planned'},
            {key: 'recording', label: 'Recording'},
            {key: 'recorded', label: 'Recorded'}
        ],
        current: 'guide',
        /// Shown on the tabs, so what is waiting or under way is visible without opening them.
        counts: {planned: 0, recording: 0, recorded: 0},

        show(key) {
            this.current = key;
        },

        is(key) {
            return this.current === key;
        },

        get label() {
            return this.all.find(tab => tab.key === this.current).label;
        }
    };
}
