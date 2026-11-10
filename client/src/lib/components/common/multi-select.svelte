<script lang="ts">
	import * as Select from '#lib/components/ui/select/index.js';

	// The original's multiple Select: the trigger lists what is picked, or the placeholder.
	let {
		values,
		options,
		placeholder,
		class: className = '',
		compact = false,
		disabled = false,
		onchange
	}: {
		values: string[];
		/** `separator` starts a new section with a line above it. */
		options: { value: string; label: string; separator?: boolean }[];
		placeholder: string;
		class?: string;
		/** Show only the first pick and a +N badge for the rest. */
		compact?: boolean;
		disabled?: boolean;
		onchange: (values: string[]) => void;
	} = $props();

	const labelOf = (value: string) => options.find((o) => o.value === value)?.label ?? value;
	const shown = $derived(compact ? (values[0] ? labelOf(values[0]) : '') : values.map(labelOf).join(', '));
</script>

<Select.Root type="multiple" value={values} onValueChange={onchange} {disabled}>
	<Select.Trigger class={className} data-placeholder={values.length ? undefined : ''}>
		<span class="truncate">{shown || placeholder}</span>
		{#if compact && values.length > 1}<span class="shrink-0 rounded bg-primary px-1 text-xs text-primary-foreground">+{values.length - 1}</span>{/if}
	</Select.Trigger>
	<Select.Content>
		<Select.Group>
			{#each options as option (option.value)}
				{#if option.separator}<Select.Separator />{/if}
				<Select.Item value={option.value} label={option.label} />
			{/each}
		</Select.Group>
	</Select.Content>
</Select.Root>
