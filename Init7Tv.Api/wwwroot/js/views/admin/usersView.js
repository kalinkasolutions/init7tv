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

        isDeleteAble(user) {
            if (!user.isAdmin) {
                return true;
            }
            return user.isAdmin && this.users.filter(u => u.isAdmin).length > 1
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
            const answer = await modal.show("Confirm", "Are you sure?", "Yes", "No");
            console.log(answer);
            /// show modal <==


            // const result = await deleteItem(`/api/admin/delete-user/${user.id}`);
            // if (result === null) {
            //     return;
            // }
            // this.users.splice(this.users.indexOf(user), 1);
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

            const index = this.users.findIndex(u => u.id === updatedUser.id);
            if (index !== -1) {
                this.users.splice(index, 1, updatedUser);
            }

            this.addUserForm = {userName: '', email: '', password: '', roles: []};
        }
    }
}