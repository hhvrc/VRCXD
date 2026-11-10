<script lang="ts">
	import type { Snippet } from 'svelte';
	import { PANEL, SECTION_TITLE } from '#lib/details.js';

	// The rail and tab cards: a small heading over a rule.
	let { title, class: className = PANEL, children }: { title: string; class?: string; children: Snippet } = $props();
</script>

<div class={className}>
	<div class={SECTION_TITLE}>{title}</div>
	{@render children()}
</div>
