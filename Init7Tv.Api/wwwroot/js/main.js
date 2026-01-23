import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js/dist/hls.mjs';

import {sidebarView} from './views/sidebarView.js';
import {playerView} from './views/playerView.js';
import {notificationView} from './views/notificationView.js';
import {headerView} from "./views/headerView.js";
import {loadPartial} from "./loader.js";

Alpine.data('sidebarView', sidebarView);
Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('playerView', playerView);

window.loadPartial = loadPartial;
window.Hls = Hls;

Alpine.start();
