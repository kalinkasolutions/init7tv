/// The channel list. It owns the search box and nothing else: the channels themselves, and which
/// one the page is pointing at, belong to everybody.
export const sidebarView = () => ({
    search: '',

    init() {
        this.$store.channels.load();
    },

    /// Favourites first, then the order the channels came in, which is the order they are numbered
    /// in. Sorting is stable, so within each group that order is kept, and it is always built from
    /// the whole list rather than from what is on screen.
    get channels() {
        const needle = this.search.trim().toLowerCase();
        const matching = needle
            ? this.$store.channels.all.filter(c => c.displayName.toLowerCase().includes(needle))
            : this.$store.channels.all;

        return [...matching].sort((a, b) => Boolean(b.isFavourite) - Boolean(a.isFavourite));
    },

    choose(channel) {
        this.$store.channels.select(channel);
        this.$store.menu.close();
    }
});
