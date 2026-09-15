import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.17.3/dist/module.esm.js';
import Hls from 'https://cdn.jsdelivr.net/npm/hls.js@1.7.3/dist/hls.mjs';

import {sidebarView} from './views/tv/sidebarView.js';
import {playerView} from './views/tv/playerView.js';
import {notificationView} from './views/notificationView.js';
import {headerView} from "./views/headerView.js";
import {loadPartial} from "./loadPartial.js";
import {epgView} from "./views/tv/epgView.js";

Alpine.data('sidebarView', sidebarView);
Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('playerView', playerView);
Alpine.data('epgView', epgView)

window.loadPartialView = loadPartial;

window.Hls = Hls;

Alpine.start();
