<script lang="ts">
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { timeToText } from '#lib/helpers/format.js';

	// The original's Timer: time since an instant, as "1d 2h 3m", or "-" without one, on one shared
	// clock ticking every 15 seconds.
	let { epoch }: { epoch: number | string | null | undefined } = $props();

	const start = $derived(typeof epoch === 'string' ? Date.parse(epoch) : epoch);
	const text = $derived(start ? timeToText(Math.max(0, clock(15_000).now - start)) : '-');
</script>

<span>{text}</span>
