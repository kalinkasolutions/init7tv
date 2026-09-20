/// Which view is showing.
///
/// They used to be four separate documents, so moving between them threw away the header and the
/// channel list and built them again. They are one page now and only the middle of it changes,
/// which is what keeps the header, the list, the player and whatever was being watched where they
/// were.
///
/// The address is the only truth here, so the back button, a bookmark and a reload all still mean
/// something. A fragment rather than a path, because these are static files and nothing on the
/// server needs to know about it.
const VIEWS = ['tv', 'recording', 'admin', 'dashboard'];

export function viewStore() {
    return {
        current: fromHash(),

        is(name) {
            return this.current === name;
        },

        /// Announces the change as well, because a view that was hidden while something happened
        /// has to be able to catch up when it is brought to the front.
        listen() {
            window.addEventListener('hashchange', () => {
                const next = fromHash();

                if (next !== this.current) {
                    this.current = next;
                    window.dispatchEvent(new CustomEvent('view-changed', {detail: {view: next}}));
                }
            });
        }
    };
}

function fromHash() {
    const name = location.hash.replace('#', '');

    return VIEWS.includes(name) ? name : 'tv';
}
