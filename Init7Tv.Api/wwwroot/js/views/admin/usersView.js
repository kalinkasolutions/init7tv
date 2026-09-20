import {deleteItem, get, postJson, putJson} from '../../requestHandler.js';
import {notify} from '../../notification.js';

const EMPTY_FORM = {userName: '', email: '', password: '', roles: []};

export const usersView = () => ({
    users: [],
    roles: [],
    addForm: {...EMPTY_FORM},
    editForm: {...EMPTY_FORM},
    inviting: false,

    async init() {
        this.users = (await get('/api/admin/users') ?? []).map(user => ({...user, edit: false}));
        this.roles = await get('/api/admin/roles') ?? [];
    },

    /// The last admin cannot be deleted, or nobody could administer anything.
    canDelete(user) {
        return !this.isAdmin(user) || this.users.filter(u => this.isAdmin(u)).length > 1;
    },

    isAdmin(user) {
        return user.roles.includes('Admin');
    },

    async addUser() {
        const added = await postJson('/api/admin/add-user', this.addForm);
        if (added === null) {
            return;
        }

        this.users.push(added);
        this.addForm = {...EMPTY_FORM};
    },

    async deleteUser(user) {
        const confirmed = await this.$store.modal.show(
            'Delete user', `Are you sure you want to delete user ${user.userName}?`);

        if (!confirmed || await deleteItem(`/api/admin/delete-user/${user.id}`) === null) {
            return;
        }

        this.users = this.users.filter(u => u.id !== user.id);
    },

    toggleEdit(user) {
        this.editForm = {...user, roles: [...user.roles]};
        user.edit = !user.edit;
    },

    async updateUser(userId) {
        const updated = await putJson(`/api/admin/update-user/${userId}`, this.editForm);
        if (updated === null) {
            return;
        }

        this.users = this.users.map(u => u.id === userId ? {...updated, edit: false} : u);
    },

    async inviteUser(user) {
        try {
            this.inviting = true;
            const message = await postJson(`/api/admin/invite-user/${encodeURIComponent(user.email)}`);

            if (message) {
                notify('User invited successfully', message.message, 'success');
            }
        } finally {
            this.inviting = false;
        }
    }
});
