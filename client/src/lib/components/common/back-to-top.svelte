<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ArrowUp from '@lucide/svelte/icons/arrow-up';
	import { fade } from 'svelte/transition';
	import { Button } from '#lib/components/ui/button/index.js';

	// The original's BackToTop: once a list is scrolled down far enough, a round button in the
	// window's bottom right corner scrolls it back to the top.
	let { target, visibilityHeight = 400, bottom = 20, right = 20 }: { target: HTMLElement | null | undefined; visibilityHeight?: number; bottom?: number; right?: number } = $props();

	let visible = $state(false);

	$effect(() => {
		const el = target;
		if (!el) return;
		const onScroll = () => (visible = el.scrollTop >= visibilityHeight);
		el.addEventListener('scroll', onScroll, { passive: true });
		onScroll();
		return () => el.removeEventListener('scroll', onScroll);
	});
</script>

{#if visible}
	<div class="fixed z-50" style:right="{right}px" style:bottom="{bottom}px" transition:fade={{ duration: 160 }}>
		<Button size="icon" variant="secondary" class="rounded-full shadow" aria-label={m.accessibility_back_to_top()} onclick={() => target?.scrollTo({ top: 0, behavior: 'smooth' })}>
			<ArrowUp class="size-4" />
		</Button>
	</div>
{/if}
