import {get} from "../requestHandler.js";

export const headerView = () => {
    return {
        userInfo: null,

        async init() {
            this.userInfo = await get("api/user/user-info");
        }
    }
}