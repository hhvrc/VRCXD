import { describe, expect, test } from 'vitest';
import { csvField as memberCsvField } from '../groups/group-moderation.svelte.js';
import { csvField } from '../nav/tools.js';

// The original's needsCsvQuotes and formatCsvField, as the export dialogs and the group member
// CSV export use them.
describe('csv fields', () => {
	test('plain text and empty strings are not quoted', () => {
		expect(csvField('hello world')).toBe('hello world');
		expect(csvField('')).toBe('');
	});

	test('commas, double quotes and control characters are quoted', () => {
		expect(csvField('a,b')).toBe('"a,b"');
		expect(csvField('line1\nline2')).toBe('"line1\nline2"');
		expect(csvField('tab\there')).toBe('"tab\there"');
	});

	test('double quotes inside are doubled', () => {
		expect(csvField('say "hi"')).toBe('"say ""hi"""');
	});

	test('the member export takes any value: nothing is empty, numbers are text', () => {
		expect(memberCsvField(null)).toBe('');
		expect(memberCsvField(undefined)).toBe('');
		expect(memberCsvField(42)).toBe('42');
		expect(memberCsvField('hello')).toBe('hello');
		expect(memberCsvField('a,"b"')).toBe('"a,""b"""');
	});
});
