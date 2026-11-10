<script lang="ts">
	import { Checkbox as CheckboxPrimitive } from 'bits-ui';
	import Check from '@lucide/svelte/icons/check';
	import { cn, type WithoutChildrenOrChild } from '#lib/utils.js';

	let { ref = $bindable(null), checked = $bindable(false), class: className, ...restProps }: WithoutChildrenOrChild<CheckboxPrimitive.RootProps> = $props();
</script>

<CheckboxPrimitive.Root
	bind:ref
	bind:checked
	data-slot="checkbox"
	class={cn('peer flex size-4 shrink-0 items-center justify-center rounded-[4px] border border-input shadow-xs outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=checked]:text-primary-foreground', className)}
	{...restProps}
>
	{#snippet children({ checked: isChecked })}
		{#if isChecked}<Check class="size-3.5" />{/if}
	{/snippet}
</CheckboxPrimitive.Root>
