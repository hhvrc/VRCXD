import { describe, expect, test } from 'vitest';
import { hueToHex } from '../people/user-display.js';

// The original's HSVtoRGB, as getNameColour calls it: saturation 0.6 and value 1 in the dark
// theme, saturation 1 and value 0.7 in the light one.
describe('name colour', () => {
	test('the dark theme colours at the primary hues', () => {
		expect(hueToHex(0, true)).toBe('#ff6666');
		expect(hueToHex(65535 / 6, true)).toBe('#ffff66');
		expect(hueToHex(65535 / 3, true)).toBe('#66ff66');
		expect(hueToHex(65535 / 2, true)).toBe('#66ffff');
		expect(hueToHex((65535 * 2) / 3, true)).toBe('#6666ff');
	});

	test('the light theme colours at the primary hues', () => {
		expect(hueToHex(0, false)).toBe('#b30000');
		expect(hueToHex(65535 / 3, false)).toBe('#00b300');
		expect(hueToHex((65535 * 2) / 3, false)).toBe('#0000b3');
	});

	test('a hue between the primaries mixes them', () => {
		expect(hueToHex(65535 / 12, false)).toBe('#b35900');
	});
});
