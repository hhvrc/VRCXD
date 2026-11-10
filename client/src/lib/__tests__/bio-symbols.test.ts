import { describe, expect, test } from 'vitest';
import { replaceBioSymbols, sanitizeEntity } from '../helpers/format.js';

// The original's sanitizeEntityJson over names and descriptions: replaceBioSymbols on each field.
describe('look-alike punctuation in names', () => {
	test('full-width look-alikes go back to the real characters', () => {
		expect(replaceBioSymbols('hello？')).toBe('hello?');
		expect(replaceBioSymbols('test＃')).toBe('test#');
		expect(replaceBioSymbols('mail＠home')).toBe('mail@home');
	});

	test('a missing or empty field is an empty string', () => {
		expect(replaceBioSymbols('')).toBe('');
		expect(replaceBioSymbols(null)).toBe('');
		expect(replaceBioSymbols(undefined)).toBe('');
	});
});

// What the world, avatar and group pop-ups do with what VRChat sends them live.
describe('sanitizeEntity', () => {
	test('cleans the named fields of a copy and leaves the rest', () => {
		const world = { id: 'wrld_1', name: 'Club（VR）', description: 'Open 24⁄7', authorName: 'A＠B' };
		expect(sanitizeEntity(world, ['name', 'description'])).toEqual({ id: 'wrld_1', name: 'Club(VR)', description: 'Open 24/7', authorName: 'A＠B' });
		expect(world.name).toBe('Club（VR）');
	});

	test('an empty or missing field stays as it is', () => {
		expect(sanitizeEntity({ name: '', rules: undefined as string | undefined }, ['name', 'rules'])).toEqual({ name: '', rules: undefined });
	});
});
