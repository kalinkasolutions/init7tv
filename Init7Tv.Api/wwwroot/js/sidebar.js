import {getJson} from './requestHandler.js';

export const sidebar = () => ({
    channels: [],

    async init() {
        await this.loadChannels();
    },

    async loadChannels() {
        this.channels = await getJson("/api/streaming/channels") ?? [];
    }
})
