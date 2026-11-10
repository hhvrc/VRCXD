import { describe, expect, test } from 'vitest';
import { availablePlatforms, platformText, replaceVrcPackageUrl } from '../details.js';

describe('availablePlatforms', () => {
	test('each platform with a build shows', () => {
		expect([...availablePlatforms([{ platform: 'standalonewindows' }])]).toEqual(['standalonewindows']);
		expect([...availablePlatforms([{ platform: 'android' }])]).toEqual(['android']);
		expect([...availablePlatforms([{ platform: 'ios' }])]).toEqual(['ios']);
		expect([...availablePlatforms([{ platform: 'standalonewindows' }, { platform: 'android' }, { platform: 'ios' }])].sort()).toEqual(['android', 'ios', 'standalonewindows']);
	});

	test('only standard and security builds count, not impostors or other variants', () => {
		const packages = [
			{ platform: 'standalonewindows', variant: 'custom_variant' },
			{ platform: 'ios', variant: 'impostor' },
			{ platform: 'android', variant: 'standard' }
		];
		expect([...availablePlatforms(packages)]).toEqual(['android']);
		expect([...availablePlatforms([{ platform: 'standalonewindows', variant: 'security' }])]).toEqual(['standalonewindows']);
	});

	test('no packages means no platforms', () => {
		expect(availablePlatforms([]).size).toBe(0);
		expect(availablePlatforms(undefined).size).toBe(0);
	});
});

describe('platformText', () => {
	test('one line per platform and Unity version, newest first, variants skipped', () => {
		const packages = [
			{ platform: 'standalonewindows', unityVersion: '2019.4.31f1' },
			{ platform: 'android', unityVersion: '2022.3.22f1', variant: 'impostor' },
			{ platform: 'standalonewindows', unityVersion: '2022.3.22f1', variant: 'security' },
			{ platform: 'standalonewindows', unityVersion: '2022.3.22f1' }
		];
		expect(platformText(packages)).toBe('PC/2022.3.22f1\nPC/2019.4.31f1');
	});
});

describe('replaceVrcPackageUrl', () => {
	test('a package on the API downloads through the website', () => {
		expect(replaceVrcPackageUrl('https://api.vrchat.cloud/api/1/file/123')).toBe('https://vrchat.com/api/1/file/123');
	});

	test('any other link is left alone', () => {
		expect(replaceVrcPackageUrl('https://example.com/test')).toBe('https://example.com/test');
	});

	test('no link gives an empty string', () => {
		expect(replaceVrcPackageUrl('')).toBe('');
		expect(replaceVrcPackageUrl(null)).toBe('');
		expect(replaceVrcPackageUrl(undefined)).toBe('');
	});
});
