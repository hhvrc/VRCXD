import { afterEach, describe, expect, test, vi } from 'vitest';
import { externalLink, vrcdnPreviewUrl } from '../nav/external-link.svelte.js';

// The original's Open External Link dialog: another site's link is shown first and opens only on Open.
describe('external link dialog', () => {
	afterEach(() => vi.unstubAllGlobals());

	test('a link to another site is held for the dialog instead of opening', () => {
		const open = vi.fn();
		vi.stubGlobal('window', { open });
		externalLink.open('https://example.com/page');
		expect(externalLink.link).toBe('https://example.com/page');
		expect(open).not.toHaveBeenCalled();
	});

	test('Open opens the link in a new tab and closes the dialog', () => {
		const open = vi.fn();
		vi.stubGlobal('window', { open });
		externalLink.open('https://example.com/page');
		externalLink.confirm();
		expect(open).toHaveBeenCalledWith('https://example.com/page', '_blank', 'noopener,noreferrer');
		expect(externalLink.link).toBeUndefined();
	});

	test('a VRCDN stream opens its preview page', () => {
		expect(vrcdnPreviewUrl('rtspt://stream.vrcdn.live/live/abc')).toBe('https://panel.vrcdn.live/preview/abc');
		expect(vrcdnPreviewUrl('https://example.com')).toBeUndefined();
		const open = vi.fn();
		vi.stubGlobal('window', { open });
		externalLink.open('rtspt://stream.vrcdn.live/live/abc');
		externalLink.confirm();
		expect(open).toHaveBeenCalledWith('https://panel.vrcdn.live/preview/abc', '_blank', 'noopener,noreferrer');
	});

	test('an empty link shows nothing', () => {
		externalLink.link = undefined;
		externalLink.open('');
		expect(externalLink.link).toBeUndefined();
	});
});
