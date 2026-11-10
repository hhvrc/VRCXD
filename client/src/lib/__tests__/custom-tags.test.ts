import { describe, expect, it } from 'vitest';
import { applyTagChange, tagsFromRows, tagStyle } from '../people/custom-tags.js';

describe('custom tags', () => {
	it('reads the list by user id, skipping empty tags', () => {
		const tags = tagsFromRows([
			{ userId: 'usr_a', tag: 'Friend', colour: '#ff0000' },
			{ userId: 'usr_b', tag: 'Plain', colour: null },
			{ userId: 'usr_c', tag: '' }
		]);
		expect([...tags.keys()]).toEqual(['usr_a', 'usr_b']);
		expect(tags.get('usr_b')).toEqual({ tag: 'Plain', colour: '' });
	});

	it('sets, replaces and removes a tag as addCustomTag does', () => {
		const tags = tagsFromRows([]);
		applyTagChange(tags, { userId: 'usr_a', tag: 'One', colour: 'red', cleared: false });
		applyTagChange(tags, { userId: 'usr_a', tag: 'Two', colour: 'blue', cleared: false });
		expect(tags.get('usr_a')).toEqual({ tag: 'Two', colour: 'blue' });
		applyTagChange(tags, { userId: 'usr_a', tag: '', colour: '', cleared: false });
		expect(tags.has('usr_a')).toBe(false);
	});

	it('drops every tag on ClearCustomTags', () => {
		const tags = tagsFromRows([
			{ userId: 'usr_a', tag: 'A' },
			{ userId: 'usr_b', tag: 'B' }
		]);
		applyTagChange(tags, { userId: '', tag: '', colour: '', cleared: true });
		expect(tags.size).toBe(0);
	});

	it('colours the text and border with the tag colour', () => {
		expect(tagStyle({ tag: 'A', colour: '#00ff00' })).toBe('color: #00ff00; border-color: #00ff00');
		expect(tagStyle({ tag: 'A', colour: '' })).toBe('');
	});
});
