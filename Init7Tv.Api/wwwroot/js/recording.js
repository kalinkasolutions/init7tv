import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';

import {notificationView} from './views/notificationView.js';
import {headerView} from './views/headerView.js';
import {sidebarView} from './views/tv/sidebarView.js';
import {recordingView} from './views/recording/recordingView.js';
import {loadPartial} from './loadPartial.js';

Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('sidebarView', sidebarView);
Alpine.data('recordingView', recordingView);

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

Alpine.start();
