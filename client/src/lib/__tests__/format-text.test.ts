import { describe, expect, test } from 'vitest';
import { getFaviconUrl, replaceBioSymbols, timeToText } from '../helpers/format.js';

describe('timeToText', () => {
	test('milliseconds read as the largest units, seconds only when nothing bigger', () => {
		expect(timeToText(1000)).toBe('0s');
		expect(timeToText(60000)).toBe('1m');
		expect(timeToText(3600000)).toBe('1h');
	});

	test('with seconds asked for, the seconds are always there', () => {
		expect(timeToText(60000, true)).toBe('1m 0s');
		expect(timeToText(90000, true)).toBe('1m 30s');
	});

	test('zero and negative durations read as 0s', () => {
		expect(timeToText(0)).toBe('0s');
		expect(timeToText(-1000)).toBe('0s');
	});

	test('a day and more lists every unit down to minutes', () => {
		expect(timeToText(90061000)).toBe('1d 1h 1m');
		expect(timeToText(31536000000 + 86400000)).toBe('1y 1d');
	});
});

describe('replaceBioSymbols', () => {
	test('full-width look-alikes turn back into the real characters', () => {
		expect(replaceBioSymbols('＠user')).toBe('@user');
		expect(replaceBioSymbols('＃tag')).toBe('#tag');
		expect(replaceBioSymbols('１＋１＝２')).toBe('１+１=２');
	});

	test('bracket look-alikes turn back too', () => {
		expect(replaceBioSymbols('（hello）')).toBe('(hello)');
		expect(replaceBioSymbols('［test］')).toBe('[test]');
		expect(replaceBioSymbols('｛obj｝')).toBe('{obj}');
	});

	test('runs of spaces collapse to one', () => {
		expect(replaceBioSymbols('hello   world')).toBe('hello world');
	});

	test('anything but a string gives an empty string', () => {
		expect(replaceBioSymbols(null)).toBe('');
		expect(replaceBioSymbols(undefined)).toBe('');
		expect(replaceBioSymbols(123)).toBe('');
		expect(replaceBioSymbols('')).toBe('');
	});

	test('plain ASCII text is left alone', () => {
		expect(replaceBioSymbols('hello world')).toBe('hello world');
	});
});

describe('getFaviconUrl', () => {
	test('a link gets the favicon of its host', () => {
		expect(getFaviconUrl('https://example.com')).toBe('https://icons.duckduckgo.com/ip2/example.com.ico');
		expect(getFaviconUrl('https://sub.example.com:8080/path?q=1#x')).toBe('https://icons.duckduckgo.com/ip2/sub.example.com:8080.ico');
	});

	test('an empty or invalid link has no favicon', () => {
		expect(getFaviconUrl('')).toBe('');
		expect(getFaviconUrl('not a url')).toBe('');
	});
});
