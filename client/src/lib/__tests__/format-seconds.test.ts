import { describe, expect, it } from 'vitest';
import { formatSeconds } from '../helpers/format.js';

describe('formatSeconds', () => {
	it('reads as the original: mm:ss, hh:mm:ss from an hour', () => {
		expect(formatSeconds(0)).toBe('00:00');
		expect(formatSeconds(213.7)).toBe('03:33');
		expect(formatSeconds(3605)).toBe('01:00:05');
		expect(formatSeconds(6298.292)).toBe('01:44:58');
		expect(formatSeconds(-4)).toBe('00:00');
	});
});
