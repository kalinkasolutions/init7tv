import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js/dist/hls.mjs';

import {sidebarView} from './views/tv/sidebarView.js';
import {playerView} from './views/tv/playerView.js';
import {notificationView} from './views/notificationView.js';
import {headerView} from "./views/headerView.js";
import {loadPartial} from "./loadPartial.js";

Alpine.data('sidebarView', sidebarView);
Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('playerView', playerView);

window.loadPartialView = loadPartial;

window.Hls = Hls;

Alpine.start();
