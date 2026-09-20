import {loadPartial} from './loadPartial.js';

/// Brings a view into being the first time it is looked at, and not before.
///
/// Everything lives in one document now, so without this every viewer's browser would build the
/// admin page and open the dashboard's connection whether or not they ever go there, and somebody
/// with no business on either would be asking for both. Once loaded it stays: hidden, keeping its
/// place, ready to be come back to.
export function lazyView(name, partial, script = null) {
    return {
        loaded: false,

        init() {
            this.show();

            this.onViewChanged = () => this.show();
            window.addEventListener('view-changed', this.onViewChanged);
        },

        destroy() {
            window.removeEventListener('view-changed', this.onViewChanged);
        },

        async show() {
            if (this.loaded || !this.$store.view.is(name)) {
                return;
            }

            this.loaded = true;

            if (script) {
                await load(script);
            }

            this.$el.innerHTML = await loadPartial(partial);
        }
    };
}

/// A plain script the page did not ask for up front, for the one view that needs one.
function load(src) {
    return new Promise((resolve, reject) => {
        const tag = document.createElement('script');
        tag.src = src;
        tag.onload = resolve;
        tag.onerror = reject;
        document.head.append(tag);
    });
}
