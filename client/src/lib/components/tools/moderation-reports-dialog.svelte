<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { imageSrc, entityHref } from '#lib/api/links.js';
	import { untrack } from 'svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import ToolRefreshButton from './tool-refresh-button.svelte';
	import ToolTypeSelect from './tool-type-select.svelte';
	import { api } from '#lib/api/client.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { LoadState } from '#lib/helpers/load-state.svelte.js';
	import type { ModerationReport, ReportOptions } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's ModerationReportsDialog: the reports this account submitted, filterable by
	// type, a hundred at a time, with labels from VRChat's config.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const PAGE_SIZE = 100;

	let reports = $state<ModerationReport[]>([]);
	let hasNext = $state(false);
	const loader = new LoadState();
	let typeFilter = $state('all');
	let options = $state<ReportOptions>({ types: [], categories: {}, reasons: {} });

	const typeOptions = $derived([
		{ value: 'all', get label() { return m.dialog_moderation_reports_all(); } },
		...options.types.map((type) => ({ value: type, label: type.charAt(0).toUpperCase() + type.slice(1) }))
	]);

	async function load(reset: boolean) {
		if (reset) {
			reports = [];
			hasNext = false;
		}
		await loader.run(async () => {
			const page = await api.get<{ results: ModerationReport[]; hasNext: boolean }>(ownPath('/moderation-reports'), {
				n: PAGE_SIZE,
				offset: reports.length,
				...(typeFilter !== 'all' ? { type: typeFilter } : {})
			});
			reports = [...reports, ...page.results];
			hasNext = page.hasNext;
		});
	}

	$effect(() => {
		if (!open) return;
		untrack(() => {
			load(true);
			api
				.get<ReportOptions>(ownPath('/moderation-reports/options'))
				.then((o) => (options = o))
				.catch(() => {});
		});
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_moderation_reports_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="flex items-center gap-2">
			<ToolTypeSelect bind:value={typeFilter} options={typeOptions} onValueChange={() => load(true)} />
			<ToolRefreshButton label={m.dialog_note_export_refresh()} loading={loader.isLoading} onclick={() => load(true)} />
		</div>
		{#if loader.error}<p class="text-xs text-destructive">{loader.error}</p>{/if}
		<div class="max-h-[60vh] overflow-y-auto">
			{#if !reports.length && !loader.isLoading}<p class="{EMPTY_STATE} text-xs">{m.common_no_data()}</p>{/if}
			{#each reports as report (report.id)}
				<div class="flex items-start gap-3 border-b py-3 last:border-b-0">
					{#if report.contentThumbnailImageUrl}
						<img src={imageSrc(report.contentThumbnailImageUrl)} alt="" class="size-12 flex-none rounded-md object-cover" loading="lazy" />
					{/if}
					<div class="min-w-0 flex-1 text-sm">
						<div class="flex items-center gap-2">
							{#if report.contentId && entityHref(report.contentId)}
								<a class="truncate font-medium hover:underline" href={entityHref(report.contentId)}>{report.contentName || report.contentId}</a>
							{:else}
								<span class="truncate font-medium">{report.contentName || report.contentId}</span>
							{/if}
							<Badge variant="outline">{report.type}</Badge>
						</div>
						<div class="text-muted-foreground">
							{(report.category && options.categories[report.category]) ?? report.category} · {(report.reason && options.reasons[report.reason]) ?? report.reason}
						</div>
						{#if report.description}<div class="mt-1 wrap-break-word whitespace-pre-wrap">{report.description}</div>{/if}
						<div class="mt-1 text-xs text-muted-foreground">{formatDateFilter(report.created, 'long')}</div>
					</div>
				</div>
			{/each}
		</div>
		{#if hasNext}
			<Dialog.Footer>
				<Button size="sm" variant="outline" class="w-full" disabled={loader.isLoading} onclick={() => load(false)}>{m.dialog_moderation_reports_load_more()}</Button>
			</Dialog.Footer>
		{/if}
	</Dialog.Content>
</Dialog.Root>
