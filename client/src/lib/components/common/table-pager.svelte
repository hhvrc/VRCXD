<script lang="ts">
	import { PAGER_ROW } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import * as Pagination from '#lib/components/ui/pagination/index.js';
	import * as Select from '#lib/components/ui/select/index.js';

	// The original's table pagination footer: rows per page on the left, the pager in the middle.
	// The caller owns the page size so it can store it the way its page does.
	let {
		count,
		pageSize,
		pageSizes,
		onPageSizeChange,
		page = $bindable(1)
	}: {
		count: number;
		pageSize: number;
		pageSizes: readonly number[];
		onPageSizeChange: (value: string) => void;
		page?: number;
	} = $props();
</script>

<div class={PAGER_ROW}>
	<div class="inline-flex flex-1 items-center justify-end gap-2">
		<span class="truncate text-xs text-muted-foreground">{m.table_pagination_rows_per_page()}</span>
		<Select.Root type="single" value={String(pageSize)} onValueChange={onPageSizeChange}>
			<Select.Trigger size="sm">{pageSize}</Select.Trigger>
			<Select.Content>
				{#each pageSizes as size (size)}
					<Select.Item value={String(size)} label={String(size)} />
				{/each}
			</Select.Content>
		</Select.Root>
	</div>
	<Pagination.Root {count} perPage={pageSize} siblingCount={1} bind:page class="w-auto flex-none">
		{#snippet children({ pages, currentPage })}
			<Pagination.Content>
				<Pagination.Previous />
				{#each pages as item (item.key)}
					{#if item.type === 'ellipsis'}
						<Pagination.Ellipsis />
					{:else}
						<Pagination.Item>
							<Pagination.Link page={item} isActive={currentPage === item.value}>{item.value}</Pagination.Link>
						</Pagination.Item>
					{/if}
				{/each}
				<Pagination.Next />
			</Pagination.Content>
		{/snippet}
	</Pagination.Root>
	<div class="flex-1"></div>
</div>
