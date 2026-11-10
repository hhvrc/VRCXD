import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { appearance } from '../settings/appearance.svelte.js';
import { formatDateFilter } from '../helpers/format.js';

// The original's dateCoordinator tests. The original read the culture from the desktop host; the
// port reads the browser's language.

beforeEach(() => {
	appearance.dtIsoFormat.value = false;
	appearance.dtHour12.value = false;
	vi.stubGlobal('navigator', { language: 'en-gb' });
});

afterEach(() => {
	vi.unstubAllGlobals();
	vi.restoreAllMocks();
});

describe('formatDateFilter', () => {
	test('a dash for empty and invalid dates', () => {
		expect(formatDateFilter('', 'long')).toBe('-');
		expect(formatDateFilter(null, 'long')).toBe('-');
		expect(formatDateFilter(undefined, 'long')).toBe('-');
		expect(formatDateFilter('invalid-date', 'long')).toBe('-');
	});

	test('the ISO setting gives year-month-day and a 24 hour time for long', () => {
		appearance.dtIsoFormat.value = true;
		vi.stubGlobal('navigator', { language: 'ja-jp' });
		expect(formatDateFilter('2024-01-02T03:04:05Z', 'long')).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/);
	});

	test('a culture without an underscore at index 4 is used as is', () => {
		vi.stubGlobal('navigator', { language: 'en_us' });
		const spy = vi.spyOn(Date.prototype, 'toLocaleDateString').mockReturnValue('01/02/2024');
		formatDateFilter('2024-01-02T03:04:05Z', 'date');
		expect(spy).toHaveBeenCalledWith('en_us', expect.objectContaining({ year: 'numeric' }));
	});

	test('a culture with an underscore at index 4 is cut there', () => {
		vi.stubGlobal('navigator', { language: 'abcd_ef' });
		const spy = vi.spyOn(Date.prototype, 'toLocaleDateString').mockReturnValue('01/02/2024');
		formatDateFilter('2024-01-02T03:04:05Z', 'date');
		expect(spy).toHaveBeenCalledWith('abcd', expect.objectContaining({ year: 'numeric' }));
	});

	test('en-gb when there is no culture', () => {
		vi.stubGlobal('navigator', { language: '' });
		const spy = vi.spyOn(Date.prototype, 'toLocaleDateString').mockReturnValue('02/01/2024');
		formatDateFilter('2024-01-02T03:04:05Z', 'date');
		expect(spy).toHaveBeenCalledWith('en-gb', expect.objectContaining({ year: 'numeric' }));
	});

	test('the 12 hour setting uses h12 and short writes am and pm in lower case', () => {
		appearance.dtHour12.value = true;
		vi.stubGlobal('navigator', { language: 'en-us' });
		const spy = vi.spyOn(Date.prototype, 'toLocaleDateString').mockReturnValue('01/02, 10:30 PM');
		const result = formatDateFilter('2024-01-02T22:30:00Z', 'short');
		expect(spy).toHaveBeenCalledWith('en-us', expect.objectContaining({ hourCycle: 'h12' }));
		expect(result).toContain('pm');
		expect(result).not.toContain('PM');
	});
});
