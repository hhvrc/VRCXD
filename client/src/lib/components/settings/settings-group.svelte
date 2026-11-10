<script lang="ts">
	import type { Snippet } from 'svelte';
	import * as Card from '#lib/components/ui/card/index.js';

	// Port of the Vue app's views/Settings/components/SettingsGroup.vue.
	let { title = '', description, children }: { title?: string; description?: Snippet; children: Snippet } =
		$props();
</script>

<div class="flex flex-col gap-3.5">
	{#if title || description}
		<div class="flex flex-col gap-1.5 pl-0.5">
			{#if title}
				<h3 class="m-0 text-base font-semibold text-foreground">{title}</h3>
			{/if}
			{#if description}
				<div class="text-sm text-muted-foreground">{@render description()}</div>
			{/if}
		</div>
	{/if}
	<Card.Root class="p-0">
		<Card.Content class="flex flex-col gap-1 px-5.5 py-4.5">
			{@render children()}
		</Card.Content>
	</Card.Root>
</div>
