import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { FREE_STORE_ITEM_NOTIFICATIONS_KEY, INVENTORY_BUNDLE_NOTIFICATIONS_KEY } from '#lib/content/store-checks.js';

// The original's two notification settings for the bundle and free store item popups, per
// browser under its config keys. Both default to on.
class StoreCheckSettings {
	inventoryBundles = $state(readSetting(INVENTORY_BUNDLE_NOTIFICATIONS_KEY, true));
	freeStoreItems = $state(readSetting(FREE_STORE_ITEM_NOTIFICATIONS_KEY, true));

	setInventoryBundles(value: boolean) {
		this.inventoryBundles = value;
		storeSetting(INVENTORY_BUNDLE_NOTIFICATIONS_KEY, value);
	}

	setFreeStoreItems(value: boolean) {
		this.freeStoreItems = value;
		storeSetting(FREE_STORE_ITEM_NOTIFICATIONS_KEY, value);
	}
}

export const storeCheckSettings = new StoreCheckSettings();
