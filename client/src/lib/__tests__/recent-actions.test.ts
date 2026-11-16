import { describe, expect, it } from 'vitest';
import { pruneRecentActions } from '../api/recent-actions.svelte.js';

describe('pruneRecentActions', () => {
	const now = 10 * 60_000;

	it('drops entries past the cooldown and empty ones', () => {
		const actions = { 'usr_a:Invite': now - 60_000, 'usr_b:Invite': now - 5 * 60_000, 'usr_c:Request Invite': 0 };
		expect(pruneRecentActions(actions, 5 * 60_000, now)).toEqual({ 'usr_a:Invite': now - 60_000 });
	});

	it('returns the same object when nothing expired', () => {
		const actions = { 'usr_a:Invite': now - 60_000 };
		expect(pruneRecentActions(actions, 5 * 60_000, now)).toBe(actions);
	});
});
