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
