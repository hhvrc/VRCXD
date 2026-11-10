import { describe, expect, test } from 'vitest';
import { avatarLookupOutcome } from '../content/avatar-lookup.js';

// The original's showAvatarAuthorDialog and refreshUserDialogAvatars(fileId).
describe('opening the avatar a picture shows', () => {
	test('a found avatar opens', () => {
		expect(avatarLookupOutcome({ avatarId: 'avtr_1', ownerId: 'usr_me' }, 'usr_other', 'usr_me')).toEqual({ avatarId: 'avtr_1' });
	});

	test('someone else owning it opens the owner', () => {
		expect(avatarLookupOutcome({ ownerId: 'usr_author' }, 'usr_other', 'usr_me')).toEqual({ ownerId: 'usr_author' });
	});

	test('an own upload that was not found says so', () => {
		expect(avatarLookupOutcome({ ownerId: 'usr_me' }, 'usr_other', 'usr_me')).toEqual({ warning: 'Own avatar not found' });
	});

	test('the wearer owning it, or no owner, is private or not found', () => {
		expect(avatarLookupOutcome({ ownerId: 'usr_other' }, 'usr_other', 'usr_me')).toEqual({ warning: 'Avatar is private or not found' });
		expect(avatarLookupOutcome({}, 'usr_other', 'usr_me')).toEqual({ warning: 'Avatar is private or not found' });
	});
});
