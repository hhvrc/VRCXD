<script lang="ts">
	import type { Snippet } from 'svelte';
	import Expand from '@lucide/svelte/icons/expand';
	import { imageSrc } from '#lib/api/links.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { cn } from '#lib/utils.js';

	// An image that can be seen full size, but only by asking: clicking the picture itself does nothing,
	// so a stray click never opens it. The button shows on hover or keyboard focus.
	let {
		src,
		full,
		fileName,
		alt = '',
		class: className = '',
		imageClass = '',
		onerror,
		children
	}: {
		src: string | null | undefined;
		/** The full-size image, when `src` is only a thumbnail. */
		full?: string | null;
		fileName?: string;
		alt?: string;
		class?: string;
		imageClass?: string;
		onerror?: () => void;
		/** Drawn in place of the image, for anything that is not a plain picture. */
		children?: Snippet;
	} = $props();
</script>

<div class={cn('group/image relative', className)}>
	{#if children}
		{@render children()}
	{:else}
		<img class={imageClass} src={imageSrc(src)} {alt} loading="lazy" {onerror} />
	{/if}
	<button
		type="button"
		class="absolute top-1/2 left-1/2 flex size-7 -translate-x-1/2 -translate-y-1/2 cursor-pointer items-center justify-center rounded-md bg-black/60 text-white opacity-0 transition-opacity group-hover/image:opacity-100 hover:bg-black/80 focus-visible:opacity-100"
		title="View full image"
		aria-label="View full image"
		onclick={(event) => {
			event.stopPropagation();
			imageViewer.show(full || src, fileName);
		}}><Expand class="size-4" /></button
	>
</div>
