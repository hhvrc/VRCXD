import { describe, expect, test } from 'vitest';
import {
	clockElapsed,
	defaultVisibility,
	formatAppUptime,
	formatUtcHour,
	loadClockCount,
	loadClocks,
	loadVisibility,
	normalizeClock,
	normalizeUtcHour,
	parseClockOffset
} from '#lib/components/layout/status-bar-utils.js';

// The original's statusBarUtils tests.

describe('normalizeUtcHour', () => {
	test('passes through normal integer values', () => {
		expect(normalizeUtcHour(5)).toBe(5);
		expect(normalizeUtcHour(-3)).toBe(-3);
	});
	test('clamps to lower bound -12', () => expect(normalizeUtcHour(-20)).toBe(-12));
	test('clamps to upper bound 14', () => expect(normalizeUtcHour(20)).toBe(14));
	test('rounds fractional values', () => expect(normalizeUtcHour(5.6)).toBe(6));
	test('returns 0 for NaN and Infinity', () => {
		expect(normalizeUtcHour(NaN)).toBe(0);
		expect(normalizeUtcHour(Infinity)).toBe(0);
	});
	test('coerces string numbers', () => expect(normalizeUtcHour('9')).toBe(9));
	test('returns 0 for non-numeric strings', () => expect(normalizeUtcHour('abc')).toBe(0));
});

describe('formatUtcHour', () => {
	test('formats positive offsets with plus sign', () => expect(formatUtcHour(9)).toBe('UTC+9'));
	test('formats negative offsets', () => expect(formatUtcHour(-5)).toBe('UTC-5'));
	test('formats zero as positive', () => expect(formatUtcHour(0)).toBe('UTC+0'));
	test('normalises before formatting', () => expect(formatUtcHour(99)).toBe('UTC+14'));
});

describe('parseClockOffset', () => {
	test('parses numeric input', () => expect(parseClockOffset(3)).toBe(3));
	test('parses plain numeric strings', () => expect(parseClockOffset('-7')).toBe(-7));
	test('parses numeric strings with whitespace', () => expect(parseClockOffset(' 4 ')).toBe(4));
	test('parses UTC+N pattern', () => {
		expect(parseClockOffset('UTC+9')).toBe(9);
		expect(parseClockOffset('UTC-5')).toBe(-5);
	});
	test('parses UTC pattern with half-hour offset', () => {
		expect(parseClockOffset('UTC+5:30')).toBe(6);
		expect(parseClockOffset('UTC-9:30')).toBe(-9);
	});
	test('returns 0 for non-string non-number input', () => {
		expect(parseClockOffset(null)).toBe(0);
		expect(parseClockOffset({})).toBe(0);
	});
	test('returns 0 for unrecognised string patterns', () => expect(parseClockOffset('not a zone')).toBe(0));
});

describe('normalizeClock', () => {
	test('normalises entry with offset key', () => expect(normalizeClock({ offset: '3' })).toEqual({ offset: 3 }));
	test('normalises legacy entry with timezone key', () => expect(normalizeClock({ timezone: 'UTC+2' })).toEqual({ offset: 2 }));
	test('prefers offset over timezone when both present', () => expect(normalizeClock({ offset: 1, timezone: 'UTC+5' })).toEqual({ offset: 1 }));
	test('returns { offset: 0 } for non-object input', () => expect(normalizeClock('x')).toEqual({ offset: 0 }));
	test('returns { offset: 0 } for object without known keys', () => expect(normalizeClock({ a: 1 })).toEqual({ offset: 0 }));
});

describe('loadVisibility', () => {
	test('returns defaults when storage is empty', () => expect(loadVisibility(null)).toEqual(defaultVisibility));
	test('merges saved values with defaults', () => {
		expect(loadVisibility(JSON.stringify({ steamvr: false }))).toEqual({ ...defaultVisibility, steamvr: false });
		expect(loadVisibility({ zoom: false })).toEqual({ ...defaultVisibility, zoom: false });
	});
	test('returns defaults on corrupt JSON', () => expect(loadVisibility('{nope')).toEqual(defaultVisibility));
	test('returns a new object each time (no shared reference)', () => {
		const a = loadVisibility(null);
		a.vrchat = false;
		expect(loadVisibility(null).vrchat).toBe(true);
		expect(defaultVisibility.vrchat).toBe(true);
	});
});

describe('loadClocks', () => {
	const defaults = [{ offset: 1 }, { offset: 0 }, { offset: 9 }];
	test('returns defaults when storage is empty', () => expect(loadClocks(null, defaults)).toEqual(defaults));
	test('loads valid saved clocks', () => expect(loadClocks(JSON.stringify([{ offset: 2 }, { offset: 3 }, { offset: 4 }]), defaults)).toEqual([{ offset: 2 }, { offset: 3 }, { offset: 4 }]));
	test('returns defaults for wrong array length', () => expect(loadClocks([{ offset: 2 }], defaults)).toEqual(defaults));
	test('returns defaults for non-array JSON', () => expect(loadClocks('{"offset":2}', defaults)).toEqual(defaults));
	test('returns defaults on corrupt JSON', () => expect(loadClocks('[', defaults)).toEqual(defaults));
	test('normalises clock entries from storage', () => expect(loadClocks([{ timezone: 'UTC-3' }, { offset: '99' }, 'x'], defaults)).toEqual([{ offset: -3 }, { offset: 14 }, { offset: 0 }]));
	test('returned defaults are independent copies', () => {
		const loaded = loadClocks(null, defaults);
		loaded[0].offset = 7;
		expect(defaults[0].offset).toBe(1);
	});
});

describe('loadClockCount', () => {
	test('returns 2 when storage is empty', () => expect(loadClockCount(null)).toBe(2));
	test('returns 2 for out-of-range values', () => {
		expect(loadClockCount('5')).toBe(2);
		expect(loadClockCount(-1)).toBe(2);
		expect(loadClockCount('0')).toBe(0);
		expect(loadClockCount(3)).toBe(3);
	});
	test('returns 2 for non-numeric values', () => expect(loadClockCount('many')).toBe(2));
	test('returns valid stored count', () => {
		for (const n of [0, 1, 2, 3]) expect(loadClockCount(String(n))).toBe(n);
	});
});

describe('formatAppUptime', () => {
	test('formats zero seconds', () => expect(formatAppUptime(0)).toBe('00:00:00'));
	test('formats seconds only', () => expect(formatAppUptime(42)).toBe('00:00:42'));
	test('formats minutes and seconds', () => expect(formatAppUptime(125)).toBe('00:02:05'));
	test('formats hours, minutes, and seconds', () => expect(formatAppUptime(3725)).toBe('01:02:05'));
	test('handles large values (over 24 hours)', () => expect(formatAppUptime(90061)).toBe('25:01:01'));
	test('treats negative values as zero', () => expect(formatAppUptime(-5)).toBe('00:00:00'));
	test('floors fractional seconds', () => expect(formatAppUptime(59.9)).toBe('00:00:59'));
});

describe('a ProTV producer clock', () => {
	test('runs at its speed, holds while paused and stops at the length', () => {
		const clock = { position: 10, at: 100, speed: 2, paused: false };
		expect(clockElapsed(clock, 200, 105)).toBe(20);
		expect(clockElapsed({ ...clock, paused: true }, 200, 150)).toBe(10);
		expect(clockElapsed(clock, 30, 200)).toBe(30);
		expect(clockElapsed(clock, 0, 110)).toBe(30);
	});
});
