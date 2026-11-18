import { onLiveEvent, onReconnect } from '#lib/api/live.svelte.js';
import { debounce } from '#lib/helpers/debounce.js';
import { notificationCenter } from '#lib/notifications/notification-center.svelte.js';
import { sidebarData } from '#lib/people/sidebar-data.svelte.js';

// Keeps the shared sidebar state and the notification center current for the whole session,
// whichever page is open and whether the sidebar is shown (the notifications and the wrist overlay
// read the friend list on every page). Started once from the layout; the sidebar mounting again,
// when leaving a page that hides it, does not read anything again.

const GROUP_INSTANCES_EVERY_MS = 5 * 60 * 1000;

let started = false;

export function startSidebarLive(): void {
	sidebarData.start();
	notificationCenter.start();
	if (started) return;
	started = true;
	const reloadNotifications = debounce(() => notificationCenter.load(), { wait: 500, maxWait: 5000 });
	onLiveEvent((event) => {
		// Following the notifications topic, the server sends the rows themselves: nothing to read.
		if (sidebarData.apply(event).notifications && !notificationCenter.followsTopic) reloadNotifications();
	});
	onReconnect(() => {
		reloadNotifications.cancel();
		sidebarData.resync();
		// The topic subscribes again on the new connection and its snapshot replaces the rows.
		if (!notificationCenter.followsTopic) void notificationCenter.load();
	});
	// The original refreshes the open group instances every five minutes.
	setInterval(() => void sidebarData.loadGroupInstances(), GROUP_INSTANCES_EVERY_MS);
}
