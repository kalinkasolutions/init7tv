import {deleteItem, get, postJson, putJson} from "../../requestHandler.js";

export const usersView = () => {
    return {
        users: [],
        roles: [],
        addUserForm: {userName: '', email: '', password: '', roles: []},
        editUserForm: {userName: '', email: '', password: '', roles: []},

        async init() {
            this.users = (await get("/api/admin/users")).map(user => ({
                ...user,
                edit: false
            }));
            this.roles = await get("/api/admin/roles");
        },

        isDeleteAble(userId) {
            const user = this.users.find(u => u.id === userId);
            if (!user.roles.some(r => r === "Admin")) {
                return true;
            }
            return this.users.filter(u => u.roles.some(r => r === "Admin")).length > 1
        },

        async addUser() {
            const newUser = await postJson("/api/admin/add-user", this.addUserForm);
            if (newUser === null) {
                return;
            }
            this.users.push(newUser);
            this.addUserForm = {userName: '', email: '', password: '', roles: []};

        },

        async deleteUser(user) {

            const modal = Alpine.store('modal');
            const confirmed = await modal.show(`Delete user`, `Are you sure you want to delete user ${user.userName}?`, "Yes", "No");

            if (!confirmed) {
                return;
            }

            const result = await deleteItem(`/api/admin/delete-user/${user.id}`);
            if (result === null) {
                return;
            }
            this.users.splice(this.users.indexOf(user), 1);
        },

        toggleEdit(user) {
            this.editUserForm = {...user, roles: [...user.roles]};
            user.edit = !user.edit;
        },

        async updateUser(userId) {
            const updatedUser = await putJson(`/api/admin/update-user/${userId}`, this.editUserForm);
            if (updatedUser === null) {
                return;
            }

            this.users = this.users.map((u) => {
                if (u.id !== userId) {
                    return u;
                }
                return {
                    ...updatedUser,
                    edit: false
                }
            });

            this.addUserForm = {userName: '', email: '', password: '', roles: []};
        }
    }
}