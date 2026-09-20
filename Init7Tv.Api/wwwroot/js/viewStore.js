/// Which of the two halves of the page is showing.
///
/// They used to be separate documents, so moving between them threw the channel list away and built
/// it again. They are one page now and only the middle of it changes, which is what keeps the list,
/// the player and whatever was being watched where they were.
///
/// The name lives in the address so the back button, a bookmark and a reload all still mean
/// something. A fragment rather than a path, because these are static files and nothing on the
/// server needs to know about it.
const VIEWS = ['tv', 'recording'];

export function viewStore() {
    return {
        current: fromHash(),

        is(name) {
            return this.current === name;
        },

        show(name) {
            if (!VIEWS.includes(name) || this.current === name) {
                return;
            }

            this.current = name;
            history.pushState(null, '', name === 'tv' ? '#tv' : `#${name}`);
            window.dispatchEvent(new CustomEvent('view-changed', {detail: {view: name}}));
        },

        /// The address is the truth, so the back button moves between the two as it should.
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
