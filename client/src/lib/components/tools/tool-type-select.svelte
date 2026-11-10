<script lang="ts">
	import * as Select from '#lib/components/ui/select/index.js';

	// A tool dialog's type filter, showing the picked option's label.
	let {
		value = $bindable(),
		options,
		onValueChange
	}: { value: string; options: { value: string; label: string }[]; onValueChange?: (value: string) => void } = $props();
</script>

<Select.Root type="single" bind:value {onValueChange}>
	<Select.Trigger size="sm" class="w-40">{options.find((o) => o.value === value)?.label ?? 'Type'}</Select.Trigger>
	<Select.Content>
		{#each options as option (option.value)}
			<Select.Item value={option.value}>{option.label}</Select.Item>
		{/each}
	</Select.Content>
</Select.Root>
