// The original's VRChatConfigDialog rules for VRChat's config.json (src/views/Settings/dialogs/
// VRChatConfigDialog.vue and shared/constants/settings.js), apart from the dialog itself.

export type ConfigValue = string | number | boolean;
export type VrchatConfig = Record<string, ConfigValue>;

export type Resolution = { name: string; width: number | ''; height: number | '' };

export const SCREENSHOT_RESOLUTIONS: Resolution[] = [
	{ name: '1280x720 (720p)', width: 1280, height: 720 },
	{ name: '1920x1080 (1080p Default)', width: '', height: '' },
	{ name: '2560x1440 (1440p)', width: 2560, height: 1440 },
	{ name: '3840x2160 (4K)', width: 3840, height: 2160 }
];

export const CAMERA_RESOLUTIONS: Resolution[] = [...SCREENSHOT_RESOLUTIONS, { name: '7680x4320 (8K)', width: 7680, height: 4320 }];

/** The three resolution pickers: which config keys each writes and which list it offers. */
export const RESOLUTION_FIELDS = [
	{ key: 'camera', width: 'camera_res_width', height: 'camera_res_height', rows: CAMERA_RESOLUTIONS },
	{ key: 'spout', width: 'camera_spout_res_width', height: 'camera_spout_res_height', rows: SCREENSHOT_RESOLUTIONS },
	{ key: 'screenshot', width: 'screenshot_res_width', height: 'screenshot_res_height', rows: SCREENSHOT_RESOLUTIONS }
] as const;

export const DEFAULT_RESOLUTION_KEY = '__default__';

const RESOLUTION_NAMES: Record<string, string> = {
	'1280x720': '1280x720 (720p)',
	'1920x1080': '1920x1080 (1080p)',
	'2560x1440': '2560x1440 (1440p)',
	'3840x2160': '3840x2160 (4K)',
	'7680x4320': '7680x4320 (8K)'
};

/** getVRChatResolution: a known size's name, anything else marked Custom. */
export const resolutionLabel = (res: string | undefined): string => RESOLUTION_NAMES[res ?? ''] ?? `${res} (Custom)`;

/** getVRChatResolutionKey: "<w>x<h>", or the default key when either is unset. */
export function resolutionKey(width: unknown, height: unknown): string {
	const w = Number(width);
	const h = Number(height);
	return w > 0 && h > 0 ? `${w}x${h}` : DEFAULT_RESOLUTION_KEY;
}

/** readVRChatConfigFile: the file over the dialog's values, with sorting by date on unless the file says otherwise. */
export function parseConfig(text: string): VrchatConfig {
	const parsed: unknown = text ? JSON.parse(text) : {};
	if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Invalid JSON in config.json');
	const config = { ...(parsed as VrchatConfig) };
	if (typeof config.picture_output_split_by_date === 'undefined') config.picture_output_split_by_date = true;
	return config;
}

/**
 * saveVRChatConfigFile: drops what is VRChat's default (empty text, false, and sorting by date
 * being on), and turns numeric text into numbers, then tab-indented JSON as the original writes.
 */
export function configToSave(config: VrchatConfig): string {
	const out: VrchatConfig = {};
	for (const [key, value] of Object.entries(config)) {
		if (key === 'picture_output_split_by_date') {
			if (!value) out[key] = value;
			continue;
		}
		if (value === '' || value === false) continue;
		if (typeof value === 'string') {
			const parsed = parseInt(value, 10);
			out[key] = Number.isNaN(parsed) ? value : parsed;
			continue;
		}
		out[key] = value;
	}
	return JSON.stringify(out, null, '\t');
}
