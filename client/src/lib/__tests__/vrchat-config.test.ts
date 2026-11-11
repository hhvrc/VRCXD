import { describe, expect, it } from 'vitest';
import { configToSave, DEFAULT_RESOLUTION_KEY, parseConfig, resolutionKey, resolutionLabel } from '#lib/agent/vrchat-config.js';
import { isRegistryData } from '#lib/agent/agent.js';

describe('VRChat config.json as the original dialog keeps it', () => {
	it('turns on sorting pictures by date unless the file says otherwise', () => {
		expect(parseConfig('').picture_output_split_by_date).toBe(true);
		expect(parseConfig('{"picture_output_split_by_date":false}').picture_output_split_by_date).toBe(false);
		expect(() => parseConfig('[1]')).toThrow();
		expect(() => parseConfig('{nope')).toThrow();
	});

	it('drops defaults and makes numbers of numeric text when saving', () => {
		const saved = JSON.parse(configToSave({ cache_size: '40', cache_directory: '', disableRichPresence: false, picture_output_split_by_date: true, fpv_steadycam_fov: 60, picture_output_folder: 'D:\Pics' }));
		expect(saved).toEqual({ cache_size: 40, fpv_steadycam_fov: 60, picture_output_folder: 'D:\Pics' });
		expect(JSON.parse(configToSave({ picture_output_split_by_date: false }))).toEqual({ picture_output_split_by_date: false });
		expect(configToSave({ a: 1 })).toBe('{\n\t"a": 1\n}');
	});

	it('keys a resolution by its size, unset sizes being the default', () => {
		expect(resolutionKey(2560, 1440)).toBe('2560x1440');
		expect(resolutionKey('', '')).toBe(DEFAULT_RESOLUTION_KEY);
		expect(resolutionKey(undefined, 720)).toBe(DEFAULT_RESOLUTION_KEY);
	});
});

describe('getVRChatResolution', () => {
	it.each([
		['1280x720', '1280x720 (720p)'],
		['1920x1080', '1920x1080 (1080p)'],
		['2560x1440', '2560x1440 (1440p)'],
		['3840x2160', '3840x2160 (4K)'],
		['7680x4320', '7680x4320 (8K)']
	])('maps %s to %s', (input, expected) => {
		expect(resolutionLabel(input)).toBe(expected);
	});

	it('returns Custom for unknown resolutions', () => {
		expect(resolutionLabel('1024x768')).toBe('1024x768 (Custom)');
		expect(resolutionLabel('800x600')).toBe('800x600 (Custom)');
	});

	it('handles empty/undefined input', () => {
		expect(resolutionLabel('')).toBe(' (Custom)');
		expect(resolutionLabel(undefined)).toBe('undefined (Custom)');
	});
});

describe('a registry backup file', () => {
	it('needs a type number and data on every key', () => {
		expect(isRegistryData({ A: { data: 1, type: 4 }, B: { data: 'x', type: 3 } })).toBe(true);
		expect(isRegistryData({ A: { data: 1 } })).toBe(false);
		expect(isRegistryData([])).toBe(false);
		expect(isRegistryData(null)).toBe(false);
	});
});
