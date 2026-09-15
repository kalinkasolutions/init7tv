import {get, putJson} from "../../requestHandler.js";
import {notify} from "../../notification.js";

export const generalSettingsView = () => {
    return {
        appSettings: {},
        saving: false,

        async init() {
            this.appSettings = await get("api/admin/get-general-app-settings") ?? {};
        },

        async update() {
            try {
                this.saving = true;
                const result = await putJson("api/admin/update-general-app-settings", this.appSettings);
                if (result === null) {
                    return;
                }
                this.appSettings = result;
                notify(
                    "Success",
                    "Email settings updated.",
                    "success"
                );

            } finally {
                this.saving = false;
            }
        },
    }
}