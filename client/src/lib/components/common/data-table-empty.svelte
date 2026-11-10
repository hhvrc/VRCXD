<script lang="ts">
	import Inbox from '@lucide/svelte/icons/inbox';
	import SearchX from '@lucide/svelte/icons/search-x';
	import * as Empty from '#lib/components/ui/empty/index.js';

	// The original's DataTableEmpty: "No data", or "No matching records" when a search or filter
	// left nothing to show.
	let { type = 'nodata' }: { type?: 'nodata' | 'nomatch' } = $props();
</script>

<Empty.Root>
	<Empty.Header>
		<Empty.Media variant="icon">
			{#if type === 'nomatch'}<SearchX class="text-lg" />{:else}<Inbox class="text-lg" />{/if}
		</Empty.Media>
		<Empty.Description>{type === 'nomatch' ? 'No matching records' : 'No data'}</Empty.Description>
	</Empty.Header>
</Empty.Root>
