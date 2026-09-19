import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';
import focus from 'https://cdn.jsdelivr.net/npm/@alpinejs/focus@3.17.3/dist/module.esm.js';

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
window.Alpine = Alpine;

Alpine.start();
