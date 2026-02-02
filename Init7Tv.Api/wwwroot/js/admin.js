import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs/dist/module.esm.js';
import focus from "https://cdn.jsdelivr.net/npm/@alpinejs/focus@3.x.x/dist/module.esm.js"

import {notificationView} from './views/notificationView.js';
import {headerView} from "./views/headerView.js";
import {loadPartial} from "./loadPartial.js";
import {usersView} from "./views/admin/usersView.js";
import {modalView} from "./modalView.js";

Alpine.plugin(focus)

Alpine.data('notificationView', notificationView);
Alpine.data('headerView', headerView);
Alpine.data('usersView', usersView);

Alpine.store('modal', modalView());

window.loadPartialView = loadPartial;
window.Alpine = Alpine;

Alpine.start();
