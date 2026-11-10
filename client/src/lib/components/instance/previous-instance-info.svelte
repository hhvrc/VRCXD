<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ChartBar from '@lucide/svelte/icons/chart-bar';
	import TableIcon from '@lucide/svelte/icons/table';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import InstanceActivityDetail, { type DetailEntry } from '#lib/components/charts/instance-activity-detail.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import { formatDate, timeToText } from '#lib/helpers/format.js';
	import { userHref } from '#lib/api/links.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's PreviousInstancesInfoDialog: who was in one instance, as a table or a chart.
	let { location = $bindable(), worldName = '' }: { location?: string; worldName?: string } = $props();

	type Player = { userId: string; displayName: string; firstJoinedAt: string; timeMs: number; count: number; isFriend?: boolean; isFavorite?: boolean };
	type Stay = { id: number; userId: string; displayName: string; joinedAt: string; leftAt?: string | null; isFriend?: boolean; isFavorite?: boolean };

	let players = $state<Player[] | undefined>();
	let stays = $state<Stay[]>([]);
	let error = $state('');
	let search = $state('');
	let view = $state<'table' | 'chart'>('table');

	const shown = $derived((players ?? []).filter((p) => p.displayName.toLowerCase().includes(search.trim().toLowerCase())));
	const entries = $derived<DetailEntry[]>(
		stays.map((s) => {
			const joinTime = new Date(s.joinedAt);
			const leaveTime = s.leftAt ? new Date(s.leftAt) : joinTime;
			return { id: s.id, userId: s.userId, displayName: s.displayName, location: location ?? '', worldName, joinTime, leaveTime, time: leaveTime.getTime() - joinTime.getTime(), isFriend: s.isFriend ?? null, isFavorite: s.isFavorite ?? null };
		})
	);

	$effect(() => {
		const at = location;
		players = undefined;
		stays = [];
		error = '';
		search = '';
		view = 'table';
		if (!at) return;
		api.get<{ players: Player[]; stays: Stay[] }>(ownPath('/previous-instances/players'), { location: at }).then(
			(data) => {
				if (at !== location) return;
				players = data.players;
				stays = data.stays;
			},
			(err) => {
				if (at === location) error = ApiError.from(err).message;
			}
		);
	});
</script>

<Dialog.Root open={!!location} onOpenChange={(open) => { if (!open) location = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-212.5" onOpenAutoFocus={(event) => event.preventDefault()}>
		<Dialog.Header><Dialog.Title>Instance Info</Dialog.Title></Dialog.Header>
		<div class="flex items-center justify-between gap-2">
			<Location class="min-w-0" {location} hint={worldName} />
			<div class="flex flex-none items-center gap-2">
				{#if view === 'table'}<Input bind:value={search} placeholder={m.view_favorite_worlds_search()} class="h-9 w-48" />{/if}
				<Button size="icon" variant={view === 'table' ? 'default' : 'outline'} title={m.view_game_log_sessions_switch_to_table()} aria-label={m.view_game_log_sessions_switch_to_table()} onclick={() => (view = 'table')}><TableIcon /></Button>
				<Button size="icon" variant={view === 'chart' ? 'default' : 'outline'} title="Chart view" aria-label="Chart view" onclick={() => (view = 'chart')}><ChartBar /></Button>
			</div>
		</div>
		{#if error}
			<p class="text-xs text-destructive">{error}</p>
		{:else if !players}
			<div class="flex justify-center p-4"><Spinner /></div>
		{:else if view === 'chart'}
			<div class="max-h-[60vh] overflow-y-auto"><InstanceActivityDetail {entries} barWidth={20} accountId={vrchatAuth.ownerUserId} fullRange /></div>
		{:else}
			<div class="max-h-[60vh] overflow-y-auto rounded-md border border-border">
				<table class="w-full text-sm">
					<thead class="border-b border-border bg-muted/40 text-left text-xs text-muted-foreground">
						<tr><th class="p-2 font-medium">{m.dialog_registry_backup_date()}</th><th class="p-2 font-medium">{m.dialog_group_member_moderation_display_name()}</th><th class="p-2 font-medium">{m.table_previous_instances_time()}</th><th class="p-2 font-medium">{m.table_previous_instances_count()}</th></tr>
					</thead>
					<tbody>
						{#each shown as player (player.userId)}
							<tr class="border-b border-border last:border-0">
								<td class="whitespace-nowrap p-2 text-muted-foreground">{formatDate(player.firstJoinedAt)}</td>
								<td class="p-2">{#if player.isFriend}<span class="mr-1">{player.isFavorite ? '⭐' : '💚'}</span>{/if}<a class="hover:underline" href={userHref(player.userId)}>{player.displayName}</a></td>
								<td class="whitespace-nowrap p-2 text-muted-foreground">{player.timeMs > 0 ? timeToText(player.timeMs) : ''}</td>
								<td class="p-2 text-muted-foreground">{player.count}</td>
							</tr>
						{:else}
							<tr><td colspan="4" class="p-4 text-center text-muted-foreground">{m.common_no_data()}</td></tr>
						{/each}
					</tbody>
				</table>
			</div>
		{/if}
	</Dialog.Content>
</Dialog.Root>
