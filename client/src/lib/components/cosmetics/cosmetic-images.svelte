<script lang="ts">
	import { imageSrc } from '#lib/api/links.js';
	import { cosmetics, type Cosmetic } from '#lib/settings/cosmetics.svelte.js';

	// The three images the original's IconFrame, ProfileEffect and NameplateEffect switch between:
	// the intro animation once, then the main animation, or the still base image when not animating.
	let { cosmetic, class: className = '' }: { cosmetic: Cosmetic | undefined; class?: string } = $props();

	const asset = (type: string) => cosmetic?.assets.find((a) => a.type === type);
	const base = $derived(asset('base'));
	const main = $derived(asset('mainAnimation'));
	const intro = $derived(asset('introAnimation'));
	const animated = $derived(cosmetics.animated);

	let introActive = $state(false);
	let timer: ReturnType<typeof setTimeout> | undefined;

	$effect(() => {
		// A new cosmetic starts with its intro, if it has one.
		introActive = !!intro;
		return () => clearTimeout(timer);
	});

	$effect(() => {
		if (animated) return;
		clearTimeout(timer);
		introActive = false;
	});

	function startIntro() {
		clearTimeout(timer);
		timer = setTimeout(() => (introActive = false), intro?.totalDurationMs ?? 0);
	}
</script>

{#if main}<img src={imageSrc(main.url)} alt="" class={className} hidden={!animated || introActive} />{/if}
{#if intro && animated}<img src={imageSrc(intro.url)} alt="" class={className} hidden={!introActive} onload={startIntro} />{/if}
{#if base}<img src={imageSrc(base.url)} alt="" class={className} hidden={animated && !!(main || intro)} />{/if}
