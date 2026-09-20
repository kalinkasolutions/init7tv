/// Whether the channel drawer is open.
///
/// On a screen wide enough the channel list simply sits beside the page and this is never true.
export function menuStore() {
    return {
        open: false,

        toggle() {
            this.open = !this.open;
        },

        close() {
            this.open = false;
        }
    };
}
