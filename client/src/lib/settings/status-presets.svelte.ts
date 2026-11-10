import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';

// The original's useStatusPresets: up to ten saved status and status text pairs, kept per browser
// under the original's config key. The sidebar's Presets menu and the Edit Profile dialog share them.
export type StatusPreset = { status: string; statusDescription: string };

const STORAGE_KEY = 'VRCX_statusPresets';
export const MAX_PRESETS = 10;

const STATUS_CLASS_MAP: Record<string, string> = {
	'join me': 'joinme',
	active: 'online',
	'ask me': 'askme',
	busy: 'busy',
	offline: 'offline'
};

class StatusPresets {
	list = $state<StatusPreset[]>(readSetting<StatusPreset[]>(STORAGE_KEY, []));

	add(status: string, statusDescription: string): 'ok' | 'exists' | 'limit' {
		if (this.list.length >= MAX_PRESETS) return 'limit';
		if (this.list.some((p) => p.status === status && p.statusDescription === statusDescription)) return 'exists';
		this.list.push({ status, statusDescription });
		storeSetting(STORAGE_KEY, this.list);
		return 'ok';
	}

	remove(index: number) {
		this.list.splice(index, 1);
		storeSetting(STORAGE_KEY, this.list);
	}
}

export const statusPresets = new StatusPresets();

export function getStatusClass(status: string): string {
	return STATUS_CLASS_MAP[status] || 'online';
}
