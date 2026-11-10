import { directAccessParse } from '#lib/nav/direct-access.svelte.js';

// The original's openExternalLink and its Open External Link dialog: a VRChat link opens here, any
// other link asks first (Copy or Open), and a VRCDN stream opens its preview page.

const VRCDN_STREAM_PREFIX = 'rtspt://stream.vrcdn.live/live/';

/** The preview page a VRCDN stream address opens, if it is one. */
export function vrcdnPreviewUrl(link: string): string | undefined {
	return link.startsWith(VRCDN_STREAM_PREFIX) ? `https://panel.vrcdn.live/preview/${link.slice(VRCDN_STREAM_PREFIX.length)}` : undefined;
}

class ExternalLink {
	link = $state<string | undefined>();

	open(link: string) {
		if (!link) return;
		if (directAccessParse(link)) return;
		this.link = String(link);
	}

	/** Opens the asked-for link in a new tab, or its VRCDN preview. */
	confirm() {
		const link = this.link;
		this.link = undefined;
		if (link) window.open(vrcdnPreviewUrl(link) ?? link, '_blank', 'noopener,noreferrer');
	}
}

export const externalLink = new ExternalLink();

export const openExternalLink = (link: string) => externalLink.open(link);
