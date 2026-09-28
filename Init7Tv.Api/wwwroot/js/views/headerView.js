export const headerView = () => ({
    is(view) {
        return this.$store.view.is(view);
    },

    get canRecord() {
        return this.$store.user.canRecord;
    }
});
