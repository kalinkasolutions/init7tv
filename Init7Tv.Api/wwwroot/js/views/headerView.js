import {get} from '../requestHandler.js';

export const headerView = () => ({
    userInfo: null,

    async init() {
        this.userInfo = await get('/api/user/user-info');
    },

    is(view) {
        return this.$store.view.is(view);
    },

    /// Admins are not given the role, they simply outrank it.
    get canRecord() {
        return this.userInfo?.isAdmin === true
            || this.userInfo?.userRoles?.includes('Recording') === true;
    }
});
