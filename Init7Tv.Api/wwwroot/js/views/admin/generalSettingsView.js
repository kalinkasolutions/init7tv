import {get, putJson} from '../../requestHandler.js';
import {notify} from '../../notification.js';

export const generalSettingsView = () => ({
    appSettings: {},
    saving: false,

    async init() {
        this.appSettings = await get('/api/admin/get-general-app-settings') ?? {};
    },

    async update() {
        try {
            this.saving = true;
            const saved = await putJson('/api/admin/update-general-app-settings', this.appSettings);

            if (saved === null) {
                return;
            }

            this.appSettings = saved;
            notify('Success', 'General settings updated.', 'success');
        } finally {
            this.saving = false;
        }
    }
});
