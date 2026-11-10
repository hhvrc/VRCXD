import { browser } from '$app/env';
import { appearance } from '#lib/settings/appearance.svelte.js';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

// The table options from the original's appearance store, under the same storage names, and the
// Shift key that turns a table's delete button into an immediate delete (uiStore.shiftHeld).
export { readSetting, storeSetting };

const read = readSetting;

class TableSettings {
	#pageSize = $state(read('VRCX_tablePageSize', 20));
	#striped = $state(read('VRCX_dataTableStriped', false));
	#hideUnfriends = $state(read('VRCX_hideUnfriends', false));
	#randomUserColours = $state(read('VRCX_randomUserColours', false));
	/** The page sizes tables offer (Settings > Interface > Table Page Sizes). */
	get pageSizes() {
		return appearance.tablePageSizes.value;
	}

	/** The page size a table starts at until it has its own, saved under vrcx:table:<key>. */
	get pageSize() {
		// The original moves the page size onto the first offered one when it is no longer offered.
		const sizes = appearance.tablePageSizes.value;
		return sizes.includes(this.#pageSize) ? this.#pageSize : sizes[0];
	}
	set pageSize(value: number) {
		this.#pageSize = value;
		storeSetting('VRCX_tablePageSize', value);
	}
	get striped() {
		return this.#striped;
	}
	set striped(value: boolean) {
		this.#striped = value;
		storeSetting('VRCX_dataTableStriped', value);
	}
	get randomUserColours() {
		return this.#randomUserColours;
	}
	set randomUserColours(value: boolean) {
		this.#randomUserColours = value;
		storeSetting('VRCX_randomUserColours', value);
	}
	get hideUnfriends() {
		return this.#hideUnfriends;
	}
	set hideUnfriends(value: boolean) {
		this.#hideUnfriends = value;
		storeSetting('VRCX_hideUnfriends', value);
	}
}

export const tableSettings = new TableSettings();

class ShiftKey {
	held = $state(false);
}

export const shiftKey = new ShiftKey();

if (browser) {
	window.addEventListener('keydown', (event) => shiftKey.held = event.shiftKey);
	window.addEventListener('keyup', (event) => shiftKey.held = event.shiftKey);
	window.addEventListener('blur', () => shiftKey.held = false);
}
