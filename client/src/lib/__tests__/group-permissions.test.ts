import { describe, expect, test } from 'vitest';
import { hasGroupPermission, mergeGroupMemberSearchResults, type GroupMember } from '../groups/group-dialog.svelte.js';
import { hasModerationPermission } from '../groups/group-moderation.svelte.js';

// The original's hasGroupPermission, hasGroupModerationPermission and mergeGroupMemberSearchResults.
describe('group permissions', () => {
	test('a held permission is granted', () => {
		expect(hasGroupPermission({ myMember: { permissions: ['group-bans-manage', 'group-audit-view'] } }, 'group-bans-manage')).toBe(true);
	});

	test('the wildcard grants every permission', () => {
		expect(hasGroupPermission({ myMember: { permissions: ['*'] } }, 'group-bans-manage')).toBe(true);
	});

	test('a permission not held is not granted', () => {
		expect(hasGroupPermission({ myMember: { permissions: ['group-bans-manage'] } }, 'group-audit-view')).toBe(false);
		expect(hasGroupPermission({ myMember: { permissions: [] } }, 'group-bans-manage')).toBe(false);
	});

	test('no membership, no group or no permission list grants nothing', () => {
		expect(hasGroupPermission({ myMember: null }, 'x')).toBe(false);
		expect(hasGroupPermission(null, 'x')).toBe(false);
		expect(hasGroupPermission(undefined, 'x')).toBe(false);
		expect(hasGroupPermission({ myMember: {} }, 'x')).toBe(false);
	});
});

describe('group moderation permission', () => {
	test('any one moderation permission is enough', () => {
		const permissions = [
			'group-invites-manage',
			'group-moderates-manage',
			'group-audit-view',
			'group-bans-manage',
			'group-data-manage',
			'group-members-manage',
			'group-members-remove',
			'group-roles-assign',
			'group-roles-manage',
			'group-default-role-manage'
		];
		for (const permission of permissions) expect(hasModerationPermission([permission])).toBe(true);
	});

	test('the wildcard is enough', () => {
		expect(hasModerationPermission(['*'])).toBe(true);
	});

	test('other permissions, none, or no list are not', () => {
		expect(hasModerationPermission(['group-announcements-manage'])).toBe(false);
		expect(hasModerationPermission([])).toBe(false);
		expect(hasModerationPermission(null)).toBe(false);
	});
});

describe('group member search', () => {
	const member = (userId: string, displayName: string) => ({ id: userId, userId, user: { displayName } }) as GroupMember;

	test('loaded members that match are added after the search results, once', () => {
		const results = [member('usr_2', 'Jammy')];
		const members = [member('usr_1', 'Jamie Doe'), member('usr_2', 'Jammy'), member('usr_3', 'Other User')];
		expect(mergeGroupMemberSearchResults(results, members, 'jam').map((m) => m.userId)).toEqual(['usr_2', 'usr_1']);
	});

	test('names match whatever their case', () => {
		expect(mergeGroupMemberSearchResults([], [member('usr_1', 'JAMIE Doe')], 'jam').map((m) => m.userId)).toEqual(['usr_1']);
	});
});
