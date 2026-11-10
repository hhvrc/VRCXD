<script lang="ts">
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { formatDateFilter } from '#lib/helpers/format.js';

	// The date column every original table shares: short date, long date on hover.
	let { value }: { value: string | null | undefined } = $props();
</script>

<Tooltip.Root>
	<Tooltip.Trigger>
		{#snippet child({ props })}
			<span {...props}>{formatDateFilter(value, 'short')}</span>
		{/snippet}
	</Tooltip.Trigger>
	<Tooltip.Content side="right">{formatDateFilter(value, 'long')}</Tooltip.Content>
</Tooltip.Root>
