import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/module.esm.js';
import {sidebarView} from './views/sidebarView.js';
import {playerView} from './views/playerView.js';
import {notificationView} from './views/notificationView.js';

Alpine.data('sidebarView', sidebarView);
Alpine.data('playerView', playerView);
Alpine.data('notificationView', notificationView);

Alpine.start();
