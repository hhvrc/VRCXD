import { describe, expect, it } from 'vitest';
import { notificationMessage, notificationText, notyImage, type Noty } from '../notifications/noty.js';

// The original's notificationMessage.test.js and overlayDispatch.test.js notyGetImage, case by case.
const msg = (over: Partial<Noty>, message = '', name?: string) =>
	notificationMessage({ type: 'Online', createdAt: '', isFriend: false, isFavorite: false, ...over }, message, name);
const never = async (): Promise<null> => {
	throw new Error('no lookup expected');
};

describe('getNotificationMessage', () => {
	it("returns null for unknown type", () => {
		expect(msg({ type: 'unknown' })).toBeNull();
	});
	it("OnPlayerJoined", () => {
		expect(msg({ type: 'OnPlayerJoined', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'has joined' });
	});
	it("OnPlayerLeft", () => {
		expect(msg({ type: 'OnPlayerLeft', displayName: 'Bob' })).toEqual({ title: 'Bob', body: 'has left' });
	});
	it("OnPlayerJoining", () => {
		expect(msg({ type: 'OnPlayerJoining', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'is joining' });
	});
	it("GPS", () => {
		const r = msg({ type: 'GPS', displayName: 'Alice', location: 'wrld_123', worldName: 'TestWorld' });
		expect(r?.title).toBe('Alice');
		expect(r?.body).toContain('is in');
		expect(r?.body).toContain('TestWorld');
	});
	it("Online with worldName", () => {
		const r = msg({ type: 'Online', displayName: 'Alice', worldName: 'Lobby', location: 'wrld_456' });
		expect(r?.title).toBe('Alice');
		expect(r?.body).toContain('has logged in');
		expect(r?.body).toContain('Lobby');
	});
	it("Online without worldName", () => {
		expect(msg({ type: 'Online', displayName: 'Alice', worldName: '' })).toEqual({ title: 'Alice', body: 'has logged in' });
	});
	it("Offline", () => {
		expect(msg({ type: 'Offline', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'has logged out' });
	});
	it("Status", () => {
		expect(msg({ type: 'Status', displayName: 'Alice', status: 'busy', statusDescription: 'working' })).toEqual({ title: 'Alice', body: 'status is now busy working' });
	});
	it("invite", () => {
		const r = msg({ type: 'invite', senderUsername: 'Bob', details: { worldId: 'wrld_1', worldName: 'Hub' } }, ' (msg)');
		expect(r?.title).toBe('Bob');
		expect(r?.body).toContain('has invited you to');
		expect(r?.body).toContain('Hub');
		expect(r?.body).toContain('(msg)');
	});
	it("friendRequest", () => {
		expect(msg({ type: 'friendRequest', senderUsername: 'Charlie' })).toEqual({ title: 'Charlie', body: 'has sent you a friend request' });
	});
	it("Friend", () => {
		expect(msg({ type: 'Friend', displayName: 'Dave' })).toEqual({ title: 'Dave', body: 'is now your friend' });
	});
	it("DisplayName", () => {
		expect(msg({ type: 'DisplayName', previousDisplayName: 'OldName', displayName: 'NewName' })).toEqual({ title: 'OldName', body: 'changed their name to NewName' });
	});
	it("boop", () => {
		expect(msg({ type: 'boop', senderUsername: 'Eve', message: 'boop!' })).toEqual({ title: 'Eve', body: 'boop!' });
	});
	it("groupChange", () => {
		expect(msg({ type: 'groupChange', senderUsername: 'Mod', message: 'rank up' })).toEqual({ title: 'Mod', body: 'rank up' });
	});
	it("group.announcement", () => {
		expect(msg({ type: 'group.announcement', message: 'Hello all' })).toEqual({ title: 'Group Announcement', body: 'Hello all' });
	});
	it("PortalSpawn with displayName", () => {
		const r = msg({ type: 'PortalSpawn', displayName: 'Alice', location: 'wrld_1:1', worldName: 'Room' });
		expect(r?.title).toBe('Alice');
		expect(r?.body).toContain('has spawned a portal to');
	});
	it("PortalSpawn without displayName", () => {
		expect(msg({ type: 'PortalSpawn', displayName: '' })).toEqual({ title: '', body: 'User has spawned a portal' });
	});
	it("VideoPlay", () => {
		expect(msg({ type: 'VideoPlay', notyName: 'Cool Song' })).toEqual({ title: 'Now playing', body: 'Cool Song' });
	});
	it("BlockedOnPlayerJoined", () => {
		expect(msg({ type: 'BlockedOnPlayerJoined', displayName: 'Troll' })).toEqual({ title: 'Troll', body: 'Blocked user has joined' });
	});
	it("Event", () => {
		expect(msg({ type: 'Event', data: 'something happened' })?.body).toBe('something happened');
	});
	it("External", () => {
		expect(msg({ type: 'External', message: 'ext msg' })?.body).toBe('ext msg');
	});
	it("Unfriend", () => {
		expect(msg({ type: 'Unfriend', displayName: 'Eve' })).toEqual({ title: 'Eve', body: 'is no longer your friend' });
	});
	it("AvatarChange", () => {
		expect(msg({ type: 'AvatarChange', displayName: 'Alice', data: 'CoolAvatar' })).toEqual({ title: 'Alice', body: 'changed into avatar CoolAvatar' });
	});
	it("Blocked", () => {
		expect(msg({ type: 'Blocked', displayName: 'Troll' })).toEqual({ title: 'Troll', body: 'has blocked you' });
	});
	it("Unblocked", () => {
		expect(msg({ type: 'Unblocked', displayName: 'Troll' })).toEqual({ title: 'Troll', body: 'has unblocked you' });
	});
	it("Muted", () => {
		expect(msg({ type: 'Muted', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'has muted you' });
	});
	it("Unmuted", () => {
		expect(msg({ type: 'Unmuted', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'has unmuted you' });
	});
	it("BlockedOnPlayerLeft", () => {
		expect(msg({ type: 'BlockedOnPlayerLeft', displayName: 'Troll' })).toEqual({ title: 'Troll', body: 'Blocked user has left' });
	});
	it("MutedOnPlayerJoined", () => {
		expect(msg({ type: 'MutedOnPlayerJoined', displayName: 'MutedUser' })).toEqual({ title: 'MutedUser', body: 'Muted user has joined' });
	});
	it("MutedOnPlayerLeft", () => {
		expect(msg({ type: 'MutedOnPlayerLeft', displayName: 'MutedUser' })).toEqual({ title: 'MutedUser', body: 'Muted user has left' });
	});
	it("group.informative", () => {
		expect(msg({ type: 'group.informative', message: 'Info msg' })).toEqual({ title: 'Group Informative', body: 'Info msg' });
	});
	it("group.invite", () => {
		expect(msg({ type: 'group.invite', message: 'Join us' })).toEqual({ title: 'Group Invite', body: 'Join us' });
	});
	it("group.joinRequest", () => {
		expect(msg({ type: 'group.joinRequest', message: 'Request' })).toEqual({ title: 'Group Join Request', body: 'Request' });
	});
	it("group.transfer", () => {
		expect(msg({ type: 'group.transfer', message: 'Transfer ownership' })).toEqual({ title: 'Group Transfer Request', body: 'Transfer ownership' });
	});
	it("group.queueReady", () => {
		expect(msg({ type: 'group.queueReady', message: 'Queue is ready' })).toEqual({ title: 'Instance Queue Ready', body: 'Queue is ready' });
	});
	it("instance.closed", () => {
		expect(msg({ type: 'instance.closed', message: 'Closed' })).toEqual({ title: 'Instance Closed', body: 'Closed' });
	});
});

describe('toNotificationText', () => {
	it("body-only types return just the body", () => {
		expect(notificationText('Eve', 'boop!', 'boop')).toBe('boop!');
		expect(notificationText('Group Announcement', 'Hello', 'group.announcement')).toBe('Hello');
		expect(notificationText('Event', 'data', 'Event')).toBe('data');
		expect(notificationText('External', 'msg', 'External')).toBe('msg');
		expect(notificationText('Instance Closed', 'closing', 'instance.closed')).toBe('closing');
	});
	it('colon separator types put a colon after the title', () => {
		expect(notificationText('Mod', 'rank up', 'groupChange')).toBe('Mod: rank up');
		expect(notificationText('Now playing', 'Song', 'VideoPlay')).toBe('Now playing: Song');
	});
	it("colon separator with empty title returns body only", () => {
		expect(notificationText('', 'rank up', 'groupChange')).toBe('rank up');
	});
	it("custom format messages for blocked/muted", () => {
		expect(notificationText('Troll', 'x', 'BlockedOnPlayerJoined')).toBe('Blocked user Troll has joined');
		expect(notificationText('Troll', 'x', 'BlockedOnPlayerLeft')).toBe('Blocked user Troll has left');
		expect(notificationText('Troll', 'x', 'MutedOnPlayerJoined')).toBe('Muted user Troll has joined');
		expect(notificationText('Troll', 'x', 'MutedOnPlayerLeft')).toBe('Muted user Troll has left');
	});
	it("default types use space separator", () => {
		expect(notificationText('Alice', 'has joined', 'OnPlayerJoined')).toBe('Alice has joined');
		expect(notificationText('Bob', 'has logged out', 'Offline')).toBe('Bob has logged out');
	});
	it("default with empty title returns body only", () => {
		expect(notificationText('', 'User has spawned a portal', 'PortalSpawn')).toBe('User has spawned a portal');
	});
});

describe('getNotificationMessage with displayNameOverride', () => {
	it("overrides displayName in title", () => {
		expect(msg({ type: 'OnPlayerJoined', displayName: 'Alice' }, '', 'NickAlice')).toEqual({ title: 'NickAlice', body: 'has joined' });
	});
	it("overrides senderUsername in sender-based types", () => {
		expect(msg({ type: 'friendRequest', senderUsername: 'Bob' }, '', 'NickBob')).toEqual({ title: 'NickBob', body: 'has sent you a friend request' });
	});
	it("overrides previousDisplayName in DisplayName type", () => {
		expect(msg({ type: 'DisplayName', previousDisplayName: 'OldName', displayName: 'NewName' }, '', 'NickOld')).toEqual({ title: 'NickOld', body: 'changed their name to NewName' });
	});
	it("falls back to noty fields when override is empty", () => {
		expect(msg({ type: 'OnPlayerLeft', displayName: 'Alice' }, '', '')).toEqual({ title: 'Alice', body: 'has left' });
	});
	it("falls back to noty fields when override is undefined", () => {
		expect(msg({ type: 'OnPlayerLeft', displayName: 'Alice' })).toEqual({ title: 'Alice', body: 'has left' });
	});
});

describe('notyGetImage', () => {
	it("returns the notification image when it has one", async () => {
		expect(await notyImage({ imageUrl: 'https://img.jpg', userId: 'usr_1' }, never)).toBe('https://img.jpg');
	});
	it("looks up the user icon, then the profile picture, then the avatar thumbnail", async () => {
		expect(await notyImage({ userId: 'usr_1' }, async () => ({ iconUrl: 'https://icon.jpg', profilePicOverride: 'https://profile.jpg', currentAvatarThumbnailImageUrl: 'https://avatar.jpg' }))).toBe('https://icon.jpg');
		expect(await notyImage({ userId: 'usr_1' }, async () => ({ profilePicOverride: 'https://profile.jpg', currentAvatarThumbnailImageUrl: 'https://avatar.jpg' }))).toBe('https://profile.jpg');
		expect(await notyImage({ userId: 'usr_1' }, async () => ({ currentAvatarThumbnailImageUrl: 'https://avatar.jpg' }))).toBe('https://avatar.jpg');
	});
	it("a group gets no image and no lookup", async () => {
		expect(await notyImage({ userId: 'grp_1' }, never)).toBe('');
	});
	it("a failed or empty lookup gives no image", async () => {
		expect(await notyImage({ userId: 'usr_1' }, async () => { throw new Error('down'); })).toBe('');
		expect(await notyImage({ userId: 'usr_1' }, async () => null)).toBe('');
	});
});
