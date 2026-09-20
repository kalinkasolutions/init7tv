import {loadPartial} from '../loadPartial.js';
import {whileShowing} from '../whileShowing.js';

/// Brings a view into being the first time it is looked at, and not before.
///
/// Everything lives in one document, so without this every viewer's browser would build the admin
/// page and fetch the dashboard's script whether or not they ever go there. Once built it stays:
/// hidden, keeping its place, ready to be come back to.
export function lazyView(view, partial, script = null) {
    return {
        built: false,

        init() {
            whileShowing(this, view, {enter: () => this.build()});
        },

        async build() {
            if (this.built) {
                return;
            }

            this.built = true;

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
