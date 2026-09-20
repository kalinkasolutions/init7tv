/// Which view is showing.
///
/// They used to be four separate documents, so moving between them threw away the header and the
/// channel list and built them again. They are one page now and only the middle of it changes.
///
/// The address is the only thing that says which: the header's links are ordinary links to a
/// fragment, so the back button, a bookmark and a reload all still mean something. A fragment
/// rather than a path, because these are static files and nothing on the server needs to know.
const VIEWS = ['tv', 'recording', 'admin', 'dashboard'];

export function viewStore() {
    return {
        current: fromAddress(),

        is(name) {
            return this.current === name;
        },

        listen() {
            window.addEventListener('hashchange', () => (this.current = fromAddress()));
        }
    };
}

function fromAddress() {
    const name = location.hash.replace('#', '');

    return VIEWS.includes(name) ? name : 'tv';
}
