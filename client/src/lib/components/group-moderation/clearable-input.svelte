<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import X from '@lucide/svelte/icons/x';
	import { Input } from '#lib/components/ui/input/index.js';

	// The original's clearable InputGroupField: an input with an x that empties it. oninput runs
	// once the bound value has reached the caller.
	let {
		value = $bindable(''),
		placeholder = '',
		disabled = false,
		class: className = '',
		oninput,
		onenter
	}: { value?: string; placeholder?: string; disabled?: boolean; class?: string; oninput?: () => void; onenter?: () => void } = $props();
</script>

<div class="relative {className}">
	<Input
		bind:value
		{placeholder}
		{disabled}
		class="h-8 pr-7"
		oninput={() => queueMicrotask(() => oninput?.())}
		onkeydown={(event) => {
			if (event.key === 'Enter') onenter?.();
		}}
	/>
	{#if value && !disabled}
		<button
			type="button"
			class="absolute top-1/2 right-2 -translate-y-1/2 cursor-pointer text-muted-foreground hover:text-foreground"
			aria-label={m.view_favorite_clear()}
			onclick={() => {
				value = '';
				oninput?.();
			}}><X class="size-3.5" /></button
		>
	{/if}
</div>
