import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';
import focus from 'https://cdn.jsdelivr.net/npm/@alpinejs/focus@3.17.3/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js@1.7.3/dist/hls.mjs';

import {viewStore} from './stores/viewStore.js';
import {channelStore} from './stores/channelStore.js';
import {menuStore} from './stores/menuStore.js';
import {tabStore} from './stores/tabStore.js';
import {filterStore} from './stores/filterStore.js';
import {recordingStore} from './stores/recordingStore.js';
import {modalStore} from './stores/modalStore.js';
import {userStore} from './stores/userStore.js';

import {headerView} from './views/headerView.js';
import {notificationView} from './views/notificationView.js';
import {lazyView} from './views/lazyView.js';
import {sidebarView} from './views/tv/sidebarView.js';
import {playerView} from './views/tv/playerView.js';
import {epgView} from './views/tv/epgView.js';
import {recordingView} from './views/recording/recordingView.js';
import {recordingsView} from './views/recording/recordingsView.js';
import {usersView} from './views/admin/usersView.js';
import {emailAppSettingsView} from './views/admin/emailAppSettingsView.js';
import {generalSettingsView} from './views/admin/generalSettingsView.js';
import {dashboardView} from './views/dashboard/dashboardView.js';

import {loadPartial} from './loadPartial.js';

// The only bootstrap: it creates the stores and says which components exist. README.md is what the
// rest of the page follows.
Alpine.plugin(focus);

Alpine.store('view', viewStore());
Alpine.store('view').listen();

Alpine.store('channels', channelStore());
Alpine.store('menu', menuStore());
Alpine.store('tabs', tabStore());
Alpine.store('filter', filterStore());
Alpine.store('recordings', recordingStore());
Alpine.store('modal', modalStore());

Alpine.store('user', userStore());
Alpine.store('user').load();

Alpine.data('headerView', headerView);
Alpine.data('notificationView', notificationView);
Alpine.data('lazyView', lazyView);
Alpine.data('sidebarView', sidebarView);
Alpine.data('playerView', playerView);
Alpine.data('epgView', epgView);
Alpine.data('recordingView', recordingView);
Alpine.data('recordingsView', recordingsView);
Alpine.data('usersView', usersView);
Alpine.data('emailAppSettingsView', emailAppSettingsView);
Alpine.data('generalSettingsView', generalSettingsView);
Alpine.data('dashboardView', dashboardView);

window.loadPartialView = loadPartial;
window.Alpine = Alpine;

// live channels, and recordings still being written, are transport streams the browser needs
// hls.js to demux
window.Hls = Hls;

Alpine.start();
