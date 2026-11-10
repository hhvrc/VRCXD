<script lang="ts">
	// The original's Emoji component: a still image, or a flipbook sheet stepped through by a CSS
	// animation. The sheet is always 1024x1024, in rows of 2, 4 or 8 frames.

	let { url, frames, fps, loopStyle, class: className = 'rounded-t-md' }: { url: string; frames?: number | null; fps?: number | null; loopStyle?: string | null; class?: string } = $props();

	let width = $state(0);

	function framesPerLine(count: number): number {
		if (count > 16) return 8;
		return count > 4 ? 4 : 2;
	}

	function ensureKeyframes(count: number): string {
		const name = `animated-emoji-${count}`;
		if (!document.getElementById(name)) {
			const perLine = framesPerLine(count);
			const size = 1024 / perLine;
			let steps = '';
			for (let i = 0; i < count; i++) {
				steps += `${(i / count) * 100}% { background-position: -${(i % perLine) * size}px -${Math.floor(i / perLine) * size}px; }`;
			}
			const style = document.createElement('style');
			style.id = name;
			style.textContent = `@keyframes ${name} { ${steps} }`;
			document.head.appendChild(style);
		}
		return name;
	}

	const animation = $derived.by(() => {
		if (!frames || !fps) return '';
		const size = 1024 / framesPerLine(frames);
		const duration = (1000 / fps) * frames;
		const direction = loopStyle === 'pingpong' ? 'alternate' : 'none';
		return `transform:scale(${width / size});transform-origin:top left;width:${size}px;height:${size}px;background:url('${url}') 0 0;animation:${duration}ms steps(1) 0s infinite ${direction} running ${ensureKeyframes(frames)}`;
	});
</script>

<div class={['relative aspect-square w-full overflow-hidden', className]} bind:clientWidth={width}>
	{#if animation}
		<div class="absolute top-0 left-0" style={animation}></div>
	{:else}
		<img src={url} alt="" loading="lazy" class="size-full object-cover" />
	{/if}
</div>
