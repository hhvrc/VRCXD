import { describe, expect, it } from 'vitest';
import { entryToCheck, entryToDelete, fileIdOf, fileVersionOf, variantVersionOf } from '#lib/agent/cache.js';

const SDK = '2022.3.22f1';
const url = (v: number, q = '') => `https://api.vrchat.cloud/api/1/file/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/${v}/file${q}`;

describe('the bundle VRChat caches', () => {
	it('reads the file id, version and variant version from an asset URL', () => {
		expect(fileIdOf(url(3))).toBe('file_0e8c4e32-7444-44ea-ade4-313c010d4bae');
		expect(fileVersionOf(url(3))).toBe('3');
		expect(variantVersionOf(url(3, '?v=7'))).toBe('7');
		expect(variantVersionOf(url(3))).toBe('0');
		expect(variantVersionOf('not a url')).toBe('0');
	});

	it('checks the newest Windows package the SDK opens, as security', () => {
		const ref = {
			unityPackages: [
				{ platform: 'standalonewindows', unitySortNumber: 20220322000, assetUrl: url(2) },
				{ platform: 'android', unitySortNumber: 20220322000, assetUrl: url(9) },
				{ platform: 'standalonewindows', unitySortNumber: 20990101000, assetUrl: url(8) }
			]
		};
		expect(entryToCheck(ref, SDK)).toEqual({ id: fileIdOf(url(2)), version: 2, variant: 'security', variantVersion: 0 });
	});

	it('skips standard variants for the check but not for the delete', () => {
		const ref = { unityPackages: [{ platform: 'standalonewindows', unitySortNumber: 20220322000, assetUrl: url(4), variant: 'standard' }], assetUrl: url(1) };
		expect(entryToCheck(ref, SDK)?.version).toBe(1);
		expect(entryToDelete(ref, SDK)?.version).toBe(4);
		expect(entryToCheck({}, SDK)).toBeNull();
	});
});
