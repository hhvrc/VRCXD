import { describe, expect, test } from 'vitest';
import removeConfusables, { removeWhitespace } from '../people/confusables.js';

// The original's confusables service: look-alike characters back to ASCII, for name searches.
describe('removeConfusables', () => {
	test('plain ASCII comes back as it is, without its spaces', () => {
		expect(removeConfusables('Hello')).toBe('Hello');
		expect(removeConfusables('Hello World')).toBe('HelloWorld');
	});

	test('circled letters become ASCII', () => {
		expect(removeConfusables('Ⓗⓔⓛⓛⓞ')).toBe('Hello');
	});

	test('fullwidth letters become ASCII', () => {
		expect(removeConfusables('Ｈｅｌｌｏ')).toBe('Hello');
	});

	test('Cyrillic look-alikes become Latin letters', () => {
		expect(removeConfusables('АВС')).toBe('ABC');
	});

	test('look-alikes mixed with plain letters', () => {
		expect(removeConfusables('Ⓣest')).toBe('Test');
	});

	test('combining marks are dropped', () => {
		expect(removeConfusables('e\u0301')).toBe('e');
	});

	test('an empty name stays empty', () => {
		expect(removeConfusables('')).toBe('');
	});
});

describe('removeWhitespace', () => {
	test('spaces are removed', () => {
		expect(removeWhitespace('a b c')).toBe('abc');
	});

	test('tabs and newlines are removed', () => {
		expect(removeWhitespace('a\tb\nc')).toBe('abc');
	});

	test('text without whitespace is unchanged', () => {
		expect(removeWhitespace('abc')).toBe('abc');
	});

	test('an empty string stays empty', () => {
		expect(removeWhitespace('')).toBe('');
	});
});
