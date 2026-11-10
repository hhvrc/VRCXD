<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';

	// Prev and Next under a paged table.
	let { page, pageNumber = $bindable(), unit = '' }: { page: { page: number; totalPages: number; totalCount?: number }; pageNumber: number; unit?: string } = $props();
</script>

{#if page.totalPages > 1}
	<div class="flex items-center justify-between px-1.5 text-xs text-muted-foreground">
		<span>Page {page.page} of {page.totalPages}{unit ? ` · ${page.totalCount} ${unit}` : ''}</span>
		<div class="flex gap-2">
			<Button variant="outline" size="sm" disabled={pageNumber <= 1} onclick={() => (pageNumber -= 1)}>Prev</Button>
			<Button variant="outline" size="sm" disabled={pageNumber >= page.totalPages} onclick={() => (pageNumber += 1)}>{m.table_pagination_next()}</Button>
		</div>
	</div>
{/if}
