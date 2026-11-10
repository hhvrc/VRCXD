import { describe, expect, test } from 'vitest';
import { authorTagsText, compareUnityVersion, contentLabel, contentTagsOf, platformCreatedAt, platformText, replaceVrcPackageUrl } from '../details.js';
import { timeToText } from '../helpers/format.js';

// The world and avatar pop-ups' info rows, as the original's useWorldDialogInfo and AvatarInfo.
describe('platformText', () => {
	test('names PC and Android packages with their Unity version', () => {
		const text = platformText([
			{ platform: 'standalonewindows', unityVersion: '2022.3.6f1', variant: 'standard' },
			{ platform: 'android', unityVersion: '2022.3.6f1', variant: 'standard' }
		]);
		expect(text.split('\n')).toEqual(['Android/2022.3.6f1', 'PC/2022.3.6f1']);
	});

	test('an unknown platform keeps its own name', () => {
		expect(platformText([{ platform: 'ios', unityVersion: '2022.3.6f1' }])).toBe('ios/2022.3.6f1');
	});

	test('each platform and version shows once and no packages is empty', () => {
		const pc = { platform: 'standalonewindows', unityVersion: '2022.3.22f1' };
		expect(platformText([pc, pc])).toBe('PC/2022.3.22f1');
		expect(platformText(undefined)).toBe('');
	});

	test('impostor and other variants are left out, security ones stay', () => {
		const text = platformText([
			{ platform: 'standalonewindows', unityVersion: '2022.3.22f1', variant: 'impostor' },
			{ platform: 'android', unityVersion: '2022.3.22f1', variant: 'security' }
		]);
		expect(text).toBe('Android/2022.3.22f1');
	});

	test('packages newer than the SDK Unity version are left out', () => {
		const packages = [
			{ platform: 'standalonewindows', unityVersion: '2022.3.22f1', unitySortNumber: 20220322000 },
			{ platform: 'standalonewindows', unityVersion: '2023.1.0f1', unitySortNumber: 20230100000 }
		];
		expect(platformText(packages, { sdkUnityVersion: '2022.3.22f1' })).toBe('PC/2022.3.22f1');
		// Before the version is known nothing is filtered; a config without one lets nothing through.
		expect(platformText(packages)).toBe('PC/2023.1.0f1\nPC/2022.3.22f1');
		expect(platformText(packages, { sdkUnityVersion: null })).toBe('');
	});

	test('the world pop-up keeps variants', () => {
		const packages = [{ platform: 'android', unityVersion: '2022.3.22f1', variant: 'impostor' }];
		expect(platformText(packages, { skipVariants: false })).toBe('Android/2022.3.22f1');
	});
});

describe('compareUnityVersion', () => {
	test('a sort number up to the SDK version passes, a newer one does not', () => {
		expect(compareUnityVersion(20220322000, '2022.3.22f1')).toBe(true);
		expect(compareUnityVersion(20190431000, '2022.3.22f1')).toBe(true);
		expect(compareUnityVersion(20220323000, '2022.3.22f1')).toBe(false);
	});

	test('a patch release counts above the final one of the same version', () => {
		expect(compareUnityVersion(20220322010, '2022.3.22p1')).toBe(true);
		expect(compareUnityVersion(20220322010, '2022.3.22f1')).toBe(false);
		expect(compareUnityVersion('50304010', '5.3.4p1')).toBe(true);
	});

	test('no or a malformed SDK version lets nothing through', () => {
		expect(compareUnityVersion(1, undefined)).toBe(false);
		expect(compareUnityVersion(1, '')).toBe(false);
		expect(compareUnityVersion(1, '2022.3')).toBe(false);
	});
});

describe('platformCreatedAt', () => {
	test('is the newest build date per platform', () => {
		expect(
			platformCreatedAt([
				{ platform: 'standalonewindows', created_at: '2024-05-01T00:00:00Z' },
				{ platform: 'standalonewindows', created_at: '2024-05-15T00:00:00Z' },
				{ platform: 'android', variant: 'security', created_at: '2024-05-20T00:00:00Z' }
			])
		).toEqual({ standalonewindows: '2024-05-15T00:00:00Z', android: '2024-05-20T00:00:00Z' });
	});

	test('is nothing without packages', () => {
		expect(platformCreatedAt(undefined)).toBeNull();
	});

	test('impostors and other variants are left out', () => {
		expect(platformCreatedAt([{ platform: 'standalonewindows', variant: 'custom', created_at: '2024-05-15T00:00:00Z' }])).toEqual({});
	});
});

describe('tags', () => {
	test('author tags lose their prefix and join with commas', () => {
		expect(authorTagsText(['author_tag_fun', 'author_tag_social', 'content_horror'])).toBe('fun, social');
		expect(authorTagsText(['content_horror'])).toBe('');
	});

	test('content tags show without the content_ prefix', () => {
		const tags = contentTagsOf(['author_tag_fun', 'content_horror', 'content_sci_fi']);
		expect(tags).toEqual(['content_horror', 'content_sci_fi']);
		expect(tags.map(contentLabel)).toEqual(['Horror', 'sci_fi']);
		expect(contentTagsOf([])).toEqual([]);
	});
});

describe('Download Unity Package', () => {
	test('an API package address is opened through the website', () => {
		expect(replaceVrcPackageUrl('https://api.vrchat.cloud/api/1/file/file_1/1/file')).toBe('https://vrchat.com/api/1/file/file_1/1/file');
		expect(replaceVrcPackageUrl(undefined)).toBe('');
	});
});

describe('time in labs', () => {
	test('is the time from the labs date to the publication date', () => {
		const ms = new Date('2024-06-01T00:00:00Z').getTime() - new Date('2024-05-01T00:00:00Z').getTime();
		expect(timeToText(ms)).toBe('31d');
	});
});
