import { describe, expect, test } from 'vitest';
import { vrchatImageAt } from '../api/links.js';
import { userImage, userImageFull } from '../groups/group-moderation.svelte.js';

// The original's userImage and userImageFull: which of a user's pictures to show.
describe('user picture', () => {
	test('no user has no picture', () => {
		expect(userImage(null)).toBe('');
		expect(userImage(undefined)).toBe('');
	});

	test('the icon comes first, then the thumbnail', () => {
		expect(userImage({ id: 'u', displayName: 'U', iconUrl: 'icon', thumbnailUrl: 'thumb' })).toBe('icon');
		expect(userImage({ id: 'u', displayName: 'U', thumbnailUrl: 'thumb', currentAvatarThumbnailImageUrl: 'avatar-thumb' })).toBe('thumb');
	});

	test('without those, the avatar thumbnail', () => {
		expect(userImage({ id: 'u', displayName: 'U', currentAvatarThumbnailImageUrl: 'avatar-thumb', currentAvatarImageUrl: 'avatar' })).toBe('avatar-thumb');
	});

	test('the avatar picture is the last resort', () => {
		expect(userImage({ id: 'u', displayName: 'U', currentAvatarImageUrl: 'avatar' })).toBe('avatar');
	});

	test('a user with no picture fields has none', () => {
		expect(userImage({ id: 'u', displayName: 'U' })).toBe('');
	});
});

describe('full size user picture', () => {
	test('no user has no full size picture', () => {
		expect(userImageFull(null)).toBe('');
	});

	test('the profile picture override comes before the avatar picture', () => {
		expect(userImageFull({ id: 'u', displayName: 'U', profilePicOverride: 'full', currentAvatarImageUrl: 'avatar' })).toBe('full');
	});

	test('the avatar picture is the fallback', () => {
		expect(userImageFull({ id: 'u', displayName: 'U', currentAvatarImageUrl: 'avatar' })).toBe('avatar');
	});
});

describe('icon size', () => {
	const file = 'file_0a1b2c3d-1111-2222-3333-444455556666';

	test('an avatar thumbnail is asked for at the icon resolution', () => {
		expect(vrchatImageAt(`https://api.vrchat.cloud/api/1/image/${file}/1/256`, 64)).toBe(`https://api.vrchat.cloud/api/1/image/${file}/1/64`);
	});

	test('an avatar picture file becomes its image at the icon resolution', () => {
		expect(vrchatImageAt(`https://api.vrchat.cloud/api/1/file/${file}/1/file`)).toBe(`https://api.vrchat.cloud/api/1/image/${file}/1/128`);
	});
});
