/// Narrowing the three recording lists, kept in one place so changing tab keeps what was being
/// looked for.
export function filterStore() {
    return {
        text: '',
        user: '',
        /// Everybody who owns something in any of the lists. Offered rather than fetched: the lists
        /// already know, and only an admin ever sees more than their own name.
        users: [],

        get active() {
            return this.text.trim() !== '' || this.user !== '';
        },

        clear() {
            this.text = '';
            this.user = '';
        },

        matches({title, subTitle, channelName, userName}) {
            if (this.user && userName !== this.user) {
                return false;
            }

            const needle = this.text.trim().toLowerCase();
            if (!needle) {
                return true;
            }

            return [title, subTitle, channelName]
                .some(field => (field ?? '').toLowerCase().includes(needle));
        },

        offer(names) {
            this.users = [...new Set([...this.users, ...names.filter(Boolean)])].sort();
        }
    };
}
