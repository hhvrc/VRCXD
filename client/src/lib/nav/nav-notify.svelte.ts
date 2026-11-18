// The original's notifiedMenus: a red dot on a nav entry when something new arrives for a page
// that isn't open (Notifications, Friend Log), cleared by opening it or by Mark All Read.
class NavNotify {
	menus = $state<string[]>([]);

	notify(key: string, currentPath: string) {
		if (currentPath !== `/${key}` && !this.menus.includes(key)) this.menus = [...this.menus, key];
	}

	remove(key: string) {
		if (this.menus.includes(key)) this.menus = this.menus.filter((k) => k !== key);
	}

	clear() {
		this.menus = [];
	}

	has(key: string) {
		return this.menus.includes(key);
	}
}

export const navNotify = new NavNotify();

/** The Notifications page's type filter, which also decides what lights its dot. */
export const NOTIFICATION_FILTERS_KEY = 'VRCX_notificationTableFilters';

/**
 * The original's handleNotification and handleNotificationV2 (upstream e55577c1): a notification
 * lights the Notifications dot when the page's type filter would show it. A v1 notification
 * also never does when the account sent it, or for a friend request, ignored friend request or
 * dotted type.
 */
export function notificationLightsNav(n: { version: number; type: string; senderUserId: string }, filters: string[], accountId: string | undefined): boolean {
	if (filters.length > 0 && !filters.includes(n.type)) return false;
	if (n.version === 2) return true;
	if (n.senderUserId && n.senderUserId === accountId) return false;
	return n.type !== 'friendRequest' && n.type !== 'ignoredFriendRequest' && !n.type.includes('.');
}
