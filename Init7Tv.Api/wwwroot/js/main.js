import Alpine from 'https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/module.esm.js';
import { sidebar } from './sidebar.js';
import { player } from './player.js';
import { notification } from './notification.js';

Alpine.data('sidebar', sidebar);
Alpine.data('player', player);
Alpine.data('notification', notification);

Alpine.start();
