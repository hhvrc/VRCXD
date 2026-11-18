import { vrchatImageAt } from '#lib/api/links.js';

// The app's one full-size image viewer, the original's showFullscreenImageDialog. The dialog itself
// lives in image-viewer-host.svelte, so nothing else needs its own copy.

/**
 * The size asked of VRChat's image endpoint for the viewer: the largest it has. VRChat sends icons
 * as an image link at a small size now, so the original asks for 10000 (upstream 119199b6, 4552bb67).
 */
export const FULL_IMAGE_SIZE = 10_000;

class ImageViewer {
	url = $state('');
	fileName = $state<string | undefined>();

	show(url: string | null | undefined, fileName?: string): void {
		if (!url) return;
		this.url = vrchatImageAt(url, FULL_IMAGE_SIZE);
		this.fileName = fileName;
	}

	close(): void {
		this.url = '';
		this.fileName = undefined;
	}
}

export const imageViewer = new ImageViewer();
