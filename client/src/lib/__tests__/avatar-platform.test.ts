import { describe, expect, test } from 'vitest';
import { platformPackage } from '../details.js';

// The original's getPlatformInfo: which package's performance rating the avatar pop-up shows per platform.
describe('avatar platform package', () => {
	test('each platform gets its own package', () => {
		const packages = [
			{ platform: 'standalonewindows', performanceRating: 'Good', variant: 'standard' },
			{ platform: 'android', performanceRating: 'Medium', variant: 'standard' },
			{ platform: 'ios', performanceRating: 'Poor', variant: 'standard' }
		];
		expect(platformPackage(packages, 'standalonewindows')?.performanceRating).toBe('Good');
		expect(platformPackage(packages, 'android')?.performanceRating).toBe('Medium');
		expect(platformPackage(packages, 'ios')?.performanceRating).toBe('Poor');
	});

	test('impostor and other variants are skipped', () => {
		expect(platformPackage([{ platform: 'standalonewindows', performanceRating: 'Good', variant: 'impostor' }], 'standalonewindows')).toBeUndefined();
	});

	test('standard and security variants count', () => {
		expect(platformPackage([{ platform: 'standalonewindows', performanceRating: 'Good', variant: 'security' }], 'standalonewindows')?.performanceRating).toBe('Good');
	});

	test('a later None rating does not replace a real one', () => {
		const packages = [
			{ platform: 'standalonewindows', performanceRating: 'Good', variant: 'standard' },
			{ platform: 'standalonewindows', performanceRating: 'None', variant: 'standard' }
		];
		expect(platformPackage(packages, 'standalonewindows')?.performanceRating).toBe('Good');
	});

	test('a later real rating replaces an earlier one', () => {
		const packages = [
			{ platform: 'android', performanceRating: 'Poor', variant: 'standard' },
			{ platform: 'android', performanceRating: 'Good', variant: 'impostor' },
			{ platform: 'android', performanceRating: 'Medium', variant: 'standard' }
		];
		expect(platformPackage(packages, 'android')?.performanceRating).toBe('Medium');
	});

	test('None is shown when it is the only rating', () => {
		expect(platformPackage([{ platform: 'android', performanceRating: 'None', variant: 'standard' }], 'android')?.performanceRating).toBe('None');
	});

	test('no packages or an empty list has nothing for any platform', () => {
		for (const platform of ['standalonewindows', 'android', 'ios']) {
			expect(platformPackage(undefined, platform)).toBeUndefined();
			expect(platformPackage([], platform)).toBeUndefined();
		}
	});

	test('a package without a variant counts', () => {
		expect(platformPackage([{ platform: 'standalonewindows', performanceRating: 'Good' }], 'standalonewindows')?.performanceRating).toBe('Good');
	});
});
