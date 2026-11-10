<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down';
	import Info from '@lucide/svelte/icons/info';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import InstanceActions from '#lib/components/instance/instance-actions.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import Pager from '#lib/components/common/pager.svelte';
	import PreviousInstanceInfo from '#lib/components/instance/previous-instance-info.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import { formatDate, timeToText } from '#lib/helpers/format.js';
	import { userHref } from '#lib/api/links.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's PreviousInstancesListDialog, for a user, a world or a group. Its delete button is
	// left out on purpose: the visit logs are kept.
	let {
		open = $bindable(false),
		kind,
		id,
		name = ''
	}: { open?: boolean; kind: 'user' | 'world' | 'group'; id: string; name?: string } = $props();

	type Row = { location: string; worldId: string; worldName: string; groupName?: string | null; ownerUserId?: string | null; ownerName?: string | null; createdAt: string; timeMs: number };
	type SortKey = 'createdAt' | 'worldName' | 'timeMs';
	const PAGE_SIZE = 10;

	let rows = $state<Row[] | undefined>();
	let error = $state('');
	let search = $state('');
	let sort = $state<{ key: SortKey; descending: boolean }>({ key: 'createdAt', descending: true });
	let pageNumber = $state(1);
	let info = $state<Row | undefined>();
	let infoLocation = $state<string | undefined>();

	const matching = $derived.by(() => {
		const text = search.trim().toLowerCase();
		const found = (rows ?? []).filter((r) => !text || [r.worldName, r.groupName, r.ownerName, r.location].some((v) => v?.toLowerCase().includes(text)));
		const { key, descending } = sort;
		return found.toSorted((a, b) => {
			const order = key === 'timeMs' ? a.timeMs - b.timeMs : String(a[key]).localeCompare(String(b[key]));
			return descending ? -order : order;
		});
	});
	const totalPages = $derived(Math.max(1, Math.ceil(matching.length / PAGE_SIZE)));
	const shown = $derived(matching.slice((pageNumber - 1) * PAGE_SIZE, pageNumber * PAGE_SIZE));

	$effect(() => {
		if (!open) return;
		const filter = { user: { userId: id }, world: { worldId: id }, group: { groupId: id } }[kind];
		rows = undefined;
		error = '';
		search = '';
		pageNumber = 1;
		api.get<Row[]>(ownPath('/previous-instances'), filter).then(
			(data) => (rows = data),
			(err) => (error = ApiError.from(err).message)
		);
	});

	$effect(() => {
		if (pageNumber > totalPages) pageNumber = totalPages;
	});

	function sortBy(key: SortKey) {
		sort = { key, descending: sort.key === key ? !sort.descending : key !== 'worldName' };
		pageNumber = 1;
	}

	function showInfo(row: Row) {
		info = row;
		infoLocation = row.location;
	}
</script>

{#snippet sortHeader(key: SortKey, label: string)}
	<th class="p-2 font-medium">
		<button type="button" class="flex cursor-pointer items-center gap-1 hover:text-foreground" onclick={() => sortBy(key)}>{label}<ArrowUpDown class="size-3" /></button>
	</th>
{/snippet}

<Dialog.Root bind:open>
	<Dialog.Content class="bg-background sm:max-w-212.5" onOpenAutoFocus={(event) => event.preventDefault()}>
		<Dialog.Header><Dialog.Title>{m.dialog_previous_instances_header()}</Dialog.Title></Dialog.Header>
		<div class="flex items-center justify-between gap-2">
			<span class="min-w-0 truncate text-sm">{name}</span>
			<Input bind:value={search} placeholder={m.dialog_previous_instances_search_placeholder()} class="h-9 w-48 flex-none" oninput={() => (pageNumber = 1)} />
		</div>
		{#if error}
			<p class="text-xs text-destructive">{error}</p>
		{:else if !rows}
			<div class="flex justify-center p-4"><Spinner /></div>
		{:else}
			<div class="overflow-x-auto rounded-md border border-border">
				<table class="w-full text-sm">
					<thead class="border-b border-border bg-muted/40 text-left text-xs text-muted-foreground">
						<tr>
							{@render sortHeader('createdAt', 'Date')}
							{#if kind === 'user'}{@render sortHeader('worldName', 'World')}{:else}<th class="p-2 font-medium">{m.table_previous_instances_instance_name()}</th>{/if}
							<th class="p-2 font-medium">{m.dialog_world_instances_instance_creator()}</th>
							{@render sortHeader('timeMs', 'Time')}
							<th class="p-2 font-medium">{m.dialog_registry_backup_action()}</th>
						</tr>
					</thead>
					<tbody>
						{#each shown as row (`${row.location}|${row.createdAt}`)}
							<tr class="border-b border-border last:border-0">
								<td class="whitespace-nowrap p-2 text-muted-foreground">{formatDate(row.createdAt)}</td>
								<td class="p-2"><Location location={row.location} hint={row.worldName} grouphint={row.groupName ?? ''} /></td>
								<td class="p-2">
									{#if row.ownerUserId?.startsWith('usr_')}<a class="hover:underline" href={userHref(row.ownerUserId)}>{row.ownerName || row.ownerUserId}</a>{/if}
								</td>
								<td class="whitespace-nowrap p-2 text-muted-foreground">{row.timeMs > 0 ? timeToText(row.timeMs) : ''}</td>
								<td class="p-2"><span class="flex items-center gap-1"><InstanceActions location={row.location} /><Button size="icon-sm" variant="ghost" title="Instance Info" aria-label="Instance Info" onclick={() => showInfo(row)}><Info class="size-4" /></Button></span></td>
							</tr>
						{:else}
							<tr><td colspan="5" class="p-4 text-center text-muted-foreground">{m.common_no_data()}</td></tr>
						{/each}
					</tbody>
				</table>
			</div>
			<Pager page={{ page: pageNumber, totalPages, totalCount: matching.length }} bind:pageNumber unit="instances" />
		{/if}
	</Dialog.Content>
</Dialog.Root>

<PreviousInstanceInfo bind:location={infoLocation} worldName={info?.worldName} />
