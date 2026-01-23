import {get} from "../requestHandler.js";

export const headerView = () => {
    return {
        userInfo: null,
        menuItems: ["admin"],

        async init() {
            this.userInfo = await get("api/user/user-info");
        }
    }
}