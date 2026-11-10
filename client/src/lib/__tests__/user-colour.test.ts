import { describe, expect, test } from 'vitest';
import { md5, nameHue } from '../people/user-colour.js';

const hex = (bytes: Uint8Array) => [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');

describe('user colour', () => {
	test('md5 matches the standard digests', () => {
		expect(hex(md5(''))).toBe('d41d8cd98f00b204e9800998ecf8427e');
		expect(hex(md5('The quick brown fox jumps over the lazy dog'))).toBe('9e107d9d372bb6826bd81d3542a419d6');
		expect(hex(md5('a'.repeat(100)))).toBe('36a92cc94a9e0fa21f625f8bfb007adf');
	});

	test('the hue is the original GetColourFromUserID, as the server computes it', () => {
		expect(nameHue('usr_c1644b5b-3ca4-45b4-97c6-a2a0de70d469')).toBe(22041);
		expect(nameHue('usr_e2e_alice')).toBe(42029);
	});
});
