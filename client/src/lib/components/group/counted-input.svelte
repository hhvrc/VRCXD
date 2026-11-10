<script lang="ts">
	import * as InputGroup from '#lib/components/ui/input-group/index.js';

	// The original's InputGroupField and InputGroupTextareaField with show-count: the length so far
	// and the limit, at the end of the field.
	let {
		value = $bindable(''),
		maxlength,
		rows = 0,
		disabled = false,
		placeholder = '',
		oninput
	}: { value?: string; maxlength: number; rows?: number; disabled?: boolean; placeholder?: string; oninput?: (value: string) => string } = $props();

	const count = $derived(value?.length ?? 0);
</script>

{#snippet counter()}
	<InputGroup.Text class="gap-1 text-xs tabular-nums">
		<span>{count}</span>
		<span class="text-muted-foreground/70">/ {maxlength}</span>
	</InputGroup.Text>
{/snippet}

<InputGroup.Root class="flex-nowrap">
	{#if rows > 0}
		<InputGroup.Textarea bind:value {maxlength} {rows} {disabled} {placeholder} />
		<InputGroup.Addon align="block-end">{@render counter()}</InputGroup.Addon>
	{:else if oninput}
		<InputGroup.Input bind:value={() => value, (next) => (value = oninput(next))} {maxlength} {disabled} {placeholder} class="h-8" />
		<InputGroup.Addon align="inline-end">{@render counter()}</InputGroup.Addon>
	{:else}
		<InputGroup.Input bind:value {maxlength} {disabled} {placeholder} class="h-8" />
		<InputGroup.Addon align="inline-end">{@render counter()}</InputGroup.Addon>
	{/if}
</InputGroup.Root>
