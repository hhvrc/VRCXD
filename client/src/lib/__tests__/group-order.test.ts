import { describe, expect, it } from 'vitest';
import { compareInGame, moveGroupInOrder, normalizeGroupOrder, parseGroupOrder } from '#lib/groups/group-order.js';

describe('the in-game group order', () => {
	it('reads the registry value, ending NUL and all', () => {
		expect(parseGroupOrder('["grp_a","grp_b"]\0')).toEqual(['grp_a', 'grp_b']);
		expect(parseGroupOrder('nope')).toEqual([]);
		expect(parseGroupOrder(null)).toEqual([]);
	});

	it('keeps saved ids still joined and appends new groups', () => {
		expect(normalizeGroupOrder(['c', 'x', 'a', 'c'], ['a', 'b', 'c'])).toEqual(['c', 'a', 'b']);
	});

	it('moves within range only', () => {
		const order = ['a', 'b', 'c'];
		expect(moveGroupInOrder(order, 'c', 0)).toBe(true);
		expect(order).toEqual(['c', 'a', 'b']);
		expect(moveGroupInOrder(order, 'c', -1)).toBe(false);
		expect(moveGroupInOrder(order, 'b', 3)).toBe(false);
		expect(moveGroupInOrder(order, 'z', 0)).toBe(false);
	});

	it('sorts ordered groups first', () => {
		expect(['z', 'b', 'a'].sort((x, y) => compareInGame(['a', 'b'], x, y))).toEqual(['a', 'b', 'z']);
	});
});
