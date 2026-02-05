import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs/dist/module.esm.js';

import {notificationView} from './views/notificationView.js';
import {headerView} from "./views/headerView.js";
import {loadPartial} from "./loadPartial.js";
import {dashboardView} from "./views/dashboard/dashboardView.js";

Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('dashboardView', dashboardView);

window.loadPartialView = loadPartial;

window.signalR = signalR;

Alpine.start();
