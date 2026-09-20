import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';
import focus from 'https://cdn.jsdelivr.net/npm/@alpinejs/focus@3.17.3/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js@1.7.3/dist/hls.mjs';

import {sidebarView} from './views/tv/sidebarView.js';
import {playerView} from './views/tv/playerView.js';
import {epgView} from './views/tv/epgView.js';
import {recordingView} from './views/recording/recordingView.js';
import {recordingsView} from './views/recording/recordingsView.js';
import {notificationView} from './views/notificationView.js';
import {headerView} from './views/headerView.js';
import {modalView} from './modalView.js';
import {loadPartial} from './loadPartial.js';
import {viewStore} from './viewStore.js';

Alpine.plugin(focus);

// watching and choosing what to record are two halves of one page, so that the channel list, the
// player and whatever is on stay where they are when you move between them
Alpine.store('view', viewStore());
Alpine.store('view').listen();

// shared between the header, the sidebar and the guide
Alpine.store('ui', {
    menuOpen: false,
    /// What the channel list is pointing at, so either half can pick it up.
    channel: null,
    audioStreamIndex: null,

    toggleMenu() {
        this.menuOpen = !this.menuOpen;
    },

    closeMenu() {
        this.menuOpen = false;
    }
});

/// Which of the four lists on the recording half is showing.
Alpine.store('tabs', {
    all: [
        {key: 'guide', label: 'Guide'},
        {key: 'planned', label: 'Planned'},
        {key: 'recording', label: 'Recording'},
        {key: 'recorded', label: 'Recorded'}
    ],
    current: 'guide',
    /// Shown on the tabs, so what is waiting or under way is visible without opening them.
    counts: {planned: 0, recording: 0, recorded: 0},

    show(key) {
        this.current = key;
    },

    is(key) {
        return this.current === key;
    },

    get label() {
        return this.all.find(tab => tab.key === this.current).label;
    }
});

/// What the planned and the recording lists have to know about each other.
Alpine.store('recordings', {
    underway: []
});

/// Narrowing the three lists, kept in one place so switching tab keeps what you were looking for.
Alpine.store('filter', {
    text: '',
    user: '',
    users: [],

    get active() {
        return this.text.trim() !== '' || this.user !== '';
    },

    clear() {
        this.text = '';
        this.user = '';
    },

    matches({title, subTitle, channelName, userName}) {
        if (this.user && userName !== this.user) {
            return false;
        }

        const needle = this.text.trim().toLowerCase();
        if (!needle) {
            return true;
        }

        return [title, subTitle, channelName]
            .some(field => (field ?? '').toLowerCase().includes(needle));
    },

    offer(names) {
        this.users = [...new Set([...this.users, ...names.filter(Boolean)])].sort();
    }
});

Alpine.store('modal', modalView());

Alpine.data('sidebarView', sidebarView);
Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('playerView', playerView);
Alpine.data('epgView', epgView);
Alpine.data('recordingView', recordingView);
Alpine.data('recordingsView', recordingsView);

window.loadPartialView = loadPartial;
window.Alpine = Alpine;

// a recording still being written is a transport stream, which the browser needs hls.js to demux
window.Hls = Hls;

Alpine.start();
