import { describe, expect, test } from 'vitest';
import { vrchatImageAt } from '../api/links.js';

// The original's convertFileUrlToImageUrl, which the port always points at VRChat's API host.
describe('vrchatImageAt', () => {
	test('a file link becomes the image endpoint at 128px', () => {
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_abc123-def456/1/file')).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/1/128');
	});

	test('a file link without the trailing /file, or with a trailing slash, converts too', () => {
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_abc123-def456/1')).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/1/128');
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_abc123-def456/2/')).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/2/128');
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_aabbccdd-1234-5678-9012-abcdef123456/5/file/', 64)).toBe(
			'https://api.vrchat.cloud/api/1/image/file_aabbccdd-1234-5678-9012-abcdef123456/5/64'
		);
	});

	test('the size can be chosen, and an image link changes size', () => {
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/file/file_abc123-def456/1/file', 256)).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/1/256');
		expect(vrchatImageAt('https://api.vrchat.cloud/api/1/image/file_abc123-def456/3/1024', 64)).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/3/64');
	});

	test('any other link is left as it is', () => {
		expect(vrchatImageAt('https://example.com/some/other/path')).toBe('https://example.com/some/other/path');
	});

	test('no link gives an empty string', () => {
		expect(vrchatImageAt('')).toBe('');
		expect(vrchatImageAt(null)).toBe('');
		expect(vrchatImageAt(undefined)).toBe('');
	});
});
