<script lang="ts">
	import EmojiImage from '#lib/components/tools/emoji-image.svelte';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	type EmojiFile = { frames?: number | null; framesOverTime?: number | null; loopStyle?: string | null };

	// The original's Emoji component for an emoji seen anywhere (a boop): it looks the file up once
	// and plays an animated sheet, or shows the still picture.
	const cache = new Map<string, Promise<EmojiFile>>();

	let { url, class: className = 'rounded' }: { url: string; class?: string } = $props();

	const fileId = $derived(/file_[0-9A-Za-z-]+/.exec(url)?.[0]);
	let file = $state<EmojiFile>({});

	$effect(() => {
		const id = fileId;
		const account = vrchatAuth.ownerUserId;
		file = {};
		if (!id || !account) return;
		let promise = cache.get(id);
		if (!promise) {
			promise = api.get<EmojiFile>(accountPath(account, `/emoji/${encodeURIComponent(id)}`)).catch(() => ({}));
			cache.set(id, promise);
		}
		let current = true;
		void promise.then((found) => current && (file = found));
		return () => (current = false);
	});
</script>

<EmojiImage url={imageSrc(url) ?? url} frames={file.frames} fps={file.framesOverTime} loopStyle={file.loopStyle} class={className} />
