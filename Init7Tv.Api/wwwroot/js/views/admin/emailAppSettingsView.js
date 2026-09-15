import {get, putJson} from "../../requestHandler.js";
import {notify} from "../../notification.js";

export const emailAppSettingsView = () => {
    return {
        appSettings: {},
        saving: false,
        sendingTestMail: false,

        async init() {
            this.appSettings = await get("api/admin/get-email-app-settings") ?? {};
        },

        async update() {
            try {
                this.saving = true;
                const result = await putJson("api/admin/update-email-app-settings", this.appSettings);
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

        async sendTestMail() {
            try {
                this.sendingTestMail = true
                const message = await get("api/admin/send-test-mail");
                if (!message) {
                    return;
                }
                notify(
                    "Test Mail sent",
                    message.message,
                    "success"
                );
            } finally {
                this.sendingTestMail = false;
            }
        }
    }
}