import { describe, expect, test } from 'vitest';
import { vrchatImageAt } from '../api/links.js';
import { imageViewer } from '../content/image-viewer.svelte.js';

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

// Upstream 119199b6 and 4552bb67: the full-size viewer showed a VRChat icon at the small size
// VRChat links it at; the original asks the image endpoint for the largest size.
describe('the full-size image viewer', () => {
	test('a VRChat image is asked for at the largest size', () => {
		imageViewer.show('https://api.vrchat.cloud/api/1/image/file_abc123-def456/3/256');
		expect(imageViewer.url).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/3/10000');
		imageViewer.show('https://api.vrchat.cloud/api/1/file/file_abc123-def456/1/file', 'print.png');
		expect(imageViewer.url).toBe('https://api.vrchat.cloud/api/1/image/file_abc123-def456/1/10000');
		expect(imageViewer.fileName).toBe('print.png');
	});

	test('any other picture is shown as it is', () => {
		imageViewer.show('/1/images?url=https%3A%2F%2Fapi.vrchat.cloud%2Fapi%2F1%2Ffile%2Ffile_x%2F1%2Ffile&token=t');
		expect(imageViewer.url).toBe('/1/images?url=https%3A%2F%2Fapi.vrchat.cloud%2Fapi%2F1%2Ffile%2Ffile_x%2F1%2Ffile&token=t');
		imageViewer.close();
		expect(imageViewer.url).toBe('');
	});
});
