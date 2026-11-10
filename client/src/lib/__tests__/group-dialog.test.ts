import { describe, expect, test, vi } from 'vitest';

vi.mock('svelte-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { mergeGroupMemberSearchResults, splitCalendar, type GroupMember } from '../groups/group-dialog.svelte.js';

const NOW = Date.parse('2026-06-01T12:00:00Z');
const event = (id: string, startsAt: string, endsAt: string, seriesId: string | null = null) => ({ id, startsAt, endsAt, seriesId });

describe('splitCalendar', () => {
	test('an empty calendar has no past or upcoming events', () => {
		expect(splitCalendar([], NOW)).toEqual({ upcoming: [], past: [] });
	});

	test('an event that has not ended is upcoming and not past', () => {
		const running = event('e1', '2026-06-01T11:00:00Z', '2026-06-01T13:00:00Z');
		const { upcoming, past } = splitCalendar([running], NOW);
		expect(upcoming.map((e) => e.id)).toEqual(['e1']);
		expect(past).toEqual([]);
	});

	test('an ended event is past and not upcoming', () => {
		const ended = event('e1', '2020-01-01T00:00:00Z', '2020-01-01T02:00:00Z');
		const { upcoming, past } = splitCalendar([ended], NOW);
		expect(upcoming).toEqual([]);
		expect(past.map((e) => e.id)).toEqual(['e1']);
	});

	test('each side is newest first and a series shows once', () => {
		const calendar = [
			event('s2', '2026-06-08T10:00:00Z', '2026-06-08T11:00:00Z', 'series'),
			event('one', '2026-06-03T10:00:00Z', '2026-06-03T11:00:00Z'),
			event('s1', '2026-06-02T10:00:00Z', '2026-06-02T11:00:00Z', 'series')
		];
		expect(splitCalendar(calendar, NOW).upcoming.map((e) => e.id)).toEqual(['one', 's1']);
	});
});

describe('mergeGroupMemberSearchResults', () => {
	const member = (userId: string, displayName: string): GroupMember => ({ id: `gmem_${userId}`, userId, user: { id: userId, displayName } });

	test('loaded members whose name contains the search join the results once', () => {
		const results = [member('usr_a', 'Alice')];
		const loaded = [member('usr_a', 'Alice'), member('usr_b', 'Malice'), member('usr_c', 'Bob')];
		expect(mergeGroupMemberSearchResults(results, loaded, 'ALI').map((m) => m.userId)).toEqual(['usr_a', 'usr_b']);
	});
});
