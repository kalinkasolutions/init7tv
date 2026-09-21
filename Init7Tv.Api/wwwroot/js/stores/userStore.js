import {get} from '../requestHandler.js';

/// Who is looking, which more than one view has to know.
export function userStore() {
    return {
        info: null,

        async load() {
            this.info = await get('/api/user/user-info');
        },

        /// Admins are not given the role, they simply outrank it.
        get canRecord() {
            return this.info?.isAdmin === true
                || this.info?.userRoles?.includes('Recording') === true;
        }
    };
}
