import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';
import focus from 'https://cdn.jsdelivr.net/npm/@alpinejs/focus@3.17.3/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js@1.7.3/dist/hls.mjs';

import {notificationView} from './views/notificationView.js';
import {headerView} from './views/headerView.js';
import {sidebarView} from './views/tv/sidebarView.js';
import {recordingView} from './views/recording/recordingView.js';
import {recordingsView} from './views/recording/recordingsView.js';
import {modalView} from './modalView.js';
import {loadPartial} from './loadPartial.js';

Alpine.plugin(focus);

Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('sidebarView', sidebarView);
Alpine.data('recordingView', recordingView);
Alpine.data('recordingsView', recordingsView);

Alpine.store('modal', modalView());

/// Which of the four views is showing. A store rather than component state
/// because the guide and the recordings are separate components and both the
/// tab bar and they themselves need to read it.
Alpine.store('tabs', {
    all: [
        {key: 'guide', label: 'Guide'},
        {key: 'planned', label: 'Planned'},
        {key: 'recording', label: 'Recording'},
        {key: 'recorded', label: 'Recorded'}
    ],
    current: 'guide',
    /// Shown on the tabs, so what is waiting or under way is visible without
    /// opening them. The views that own each list keep these up to date.
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

/// What the two lists have to know about each other. A pick whose capture has
/// started has moved on from waiting, so the planned list leaves it out — but the
/// pick itself stays, because the guide still has to show the programme as taken
/// and pressing it again is what stops the recording.
Alpine.store('recordings', {
    underway: []
});

/// Narrowing the three lists. One store because the same filter applies to all of
/// them, so switching tab keeps what you were looking for.
Alpine.store('filter', {
    text: '',
    user: '',
    /// Everybody with something in any of the lists, for the picker. Admins see
    /// everybody's, so without this they cannot tell one household apart.
    users: [],

    get active() {
        return this.text.trim() !== '' || this.user !== '';
    },

    clear() {
        this.text = '';
        this.user = '';
    },

    /// True when the entry should be shown. Takes the fields rather than an entry,
    /// because a pick and a recording do not have the same shape.
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

    /// Called by whichever list loaded, so the picker offers who is actually there.
    offer(names) {
        const all = new Set([...this.users, ...names.filter(Boolean)]);
        this.users = [...all].sort();
    }
});

// the channel list is a drawer on narrow screens, the same as on the tv page
Alpine.store('ui', {
    menuOpen: false,
    // a channel has to be chosen here rather than assumed
    restoreLastChannel: false,
    toggleMenu() {
        this.menuOpen = !this.menuOpen;
    },
    closeMenu() {
        this.menuOpen = false;
    }
});

window.loadPartialView = loadPartial;
// a recording still being written is a transport stream, which the browser needs hls.js to demux
window.Hls = Hls;
window.Alpine = Alpine;

Alpine.start();
