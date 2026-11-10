// The app's one full-size image viewer, the original's showFullscreenImageDialog. The dialog itself
// lives in image-viewer-host.svelte, so nothing else needs its own copy.
class ImageViewer {
	url = $state('');
	fileName = $state<string | undefined>();

	show(url: string | null | undefined, fileName?: string): void {
		if (!url) return;
		this.url = url;
		this.fileName = fileName;
	}

	close(): void {
		this.url = '';
		this.fileName = undefined;
	}
}

export const imageViewer = new ImageViewer();
