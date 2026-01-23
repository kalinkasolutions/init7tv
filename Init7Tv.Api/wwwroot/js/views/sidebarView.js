import {getJson} from '../requestHandler.js';

export const sidebarView = () => ({
    channels: [],

    async init() {
        this.channels = await getJson("/api/streaming/channels") ?? [];
    },
})
