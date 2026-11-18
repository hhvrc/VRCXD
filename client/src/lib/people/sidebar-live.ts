import { onLiveEvent, onReconnect } from '#lib/api/live.svelte.js';
import { notificationCenter } from '#lib/notifications/notification-center.svelte.js';
import { sidebarData } from '#lib/people/sidebar-data.svelte.js';

// Keeps the shared sidebar state and the notification center current for the whole session,
// whichever page is open and whether the sidebar is shown (the notifications and the wrist overlay
// read the friend list on every page). Started once from the layout; the sidebar mounting again,
// when leaving a page that hides it, does not read anything again.
//
// The friend list and the notifications follow their topics and need nothing from here. What has
// no topic yet (favorites, the account's own profile, the game's instance, group instances) is
// applied or read again from the events.

const GROUP_INSTANCES_EVERY_MS = 5 * 60 * 1000;

let started = false;

export function startSidebarLive(): void {
	sidebarData.start();
	notificationCenter.start();
	if (started) return;
	started = true;
	onLiveEvent((event) => void sidebarData.apply(event));
	onReconnect(() => sidebarData.resync());
	// The original refreshes the open group instances every five minutes.
	setInterval(() => void sidebarData.loadGroupInstances(), GROUP_INSTANCES_EVERY_MS);
}
