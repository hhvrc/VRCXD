<script lang="ts">
	import type { Snippet } from 'svelte';

	// Port of the Vue app's views/Settings/components/SettingsItem.vue. A toggle row is a label, so
	// clicking anywhere on it flips its switch.
	let {
		label,
		description = '',
		toggle = false,
		children
	}: { label: string; description?: string; toggle?: boolean; children: Snippet } = $props();
</script>

<svelte:element
	this={toggle ? 'label' : 'div'}
	class="-mx-2 flex min-h-8 items-center justify-between gap-6 rounded-md p-2 transition-colors has-[[data-slot=switch]:disabled]:cursor-not-allowed {toggle ? 'cursor-pointer hover:bg-accent' : ''}"
>
	<div class="flex min-w-0 flex-1 flex-col gap-0.5">
		<span class="text-sm leading-snug text-foreground">{label}</span>
		{#if description}
			<span class="text-xs leading-tight text-muted-foreground">{description}</span>
		{/if}
	</div>
	<div class="flex shrink-0 items-center gap-2">
		{@render children()}
	</div>
</svelte:element>
