<script lang="ts">
	import { CENTERED_NOTE } from '#lib/styles.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { m } from '#lib/paraglide/messages.js';
	import { lookupUser } from '#lib/people/user-lookup.js';
	import { untrack } from 'svelte';
	import Apple from '@lucide/svelte/icons/apple';
	import IdCard from '@lucide/svelte/icons/id-card';
	import Monitor from '@lucide/svelte/icons/monitor';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import * as Table from '#lib/components/ui/table/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { statusClass, timeToText } from '#lib/helpers/format.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { languageClass, languagesOf, trustInfo } from '#lib/people/user-display.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import WidgetHeader from './widget-header.svelte';
	import WidgetSettingsMenu from './widget-settings-menu.svelte';

	// The original's InstanceWidget: who is in the current instance, with the chosen columns.
	let { config = {}, configUpdater }: { config?: Record<string, unknown>; configUpdater?: (config: Record<string, unknown>) => void } = $props();

	type Player = {
		userId: string;
		displayName: string;
		joinedAt: string;
		trustLevel: string;
		tags: string[];
		developerType: string;
		status: string;
		platform?: string | null;
		isFriend: boolean;
		isBlocked: boolean;
		isMuted: boolean;
		ageVerified: boolean;
	};
	type PlayerList = { location?: string | null; world?: { name: string } | null; players: Player[] };

	const ALL_COLUMNS = ['icon', 'displayName', 'rank', 'timer', 'platform', 'language', 'status'] as const;
	const DEFAULT_COLUMNS = ['icon', 'displayName', 'timer'];
	const LABELS: Record<string, string> = { icon: 'Icon', displayName: 'Display Name', rank: 'Rank', timer: 'Timer', platform: 'Platform', language: 'Language', status: 'Status' };

	let data = $state.raw<PlayerList>({ players: [] });
	const now = $derived(clock(1000).now);

	const columns = $derived(Array.isArray(config.columns) && config.columns.length ? (config.columns as string[]) : DEFAULT_COLUMNS);
	const visible = (column: string) => columns.includes(column);
	const worldName = $derived(data.world?.name || 'Unknown World');
	const locationInfo = $derived.by(() => {
		if (!data.location) return '';
		const L = parseLocation(data.location);
		if (L.isOffline) return 'Offline';
		if (L.isPrivate) return 'Private';
		if (L.isTraveling) return 'Traveling';
		return L.instanceId ? `${worldName} ${L.accessTypeName}` : worldName;
	});

	function toggleColumn(column: string) {
		if (!configUpdater || column === 'displayName') return;
		const current = Array.isArray(config.columns) ? (config.columns as string[]) : DEFAULT_COLUMNS;
		configUpdater({ ...config, columns: current.includes(column) ? current.filter((c) => c !== column) : [...current, column] });
	}

	async function load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		try {
			data = await api.get<PlayerList>(accountPath(account, '/player-list'));
		} catch {
			data = { players: [] };
		}
	}

	$effect(() => {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		untrack(load);
		// Their own presence moving is the game changing instance.
		return watchLive((event) => event.event.case === 'visit' || (event.event.case === 'presence' && event.event.value.userId === account), load, 150);
	});
</script>

<div class="flex h-full min-h-0 flex-col">
	<WidgetHeader title={m.dashboard_widget_instance()} icon="ri-group-3-line" href="/player-list">
		<WidgetSettingsMenu options={ALL_COLUMNS} labels={LABELS} {config} {configUpdater} checked={visible} toggle={toggleColumn} disabled={(column) => column === 'displayName'} />
	</WidgetHeader>

	{#if data.players.length}
		<div class="flex items-center gap-2 border-b px-2.5 py-1.5 text-[12px] text-muted-foreground">
			<span class="truncate font-medium text-foreground">{worldName}</span>
			{#if locationInfo}<span class="shrink-0">· {locationInfo}</span>{/if}
			<span class="shrink-0">· {data.players.length} players</span>
		</div>
		<div class="min-h-0 flex-1 overflow-y-auto">
			<Table.Root>
				<Table.Body>
					{#each data.players as player (player.userId || player.displayName)}
						{@const trust = trustInfo(player.tags, player.developerType)}
						<Table.Row class="cursor-pointer" onclick={() => lookupUser(player)}>
							{#if visible('icon')}
								<Table.Cell class="w-12 text-center text-2xs">
									{#if player.isFriend}<span>💚</span>{/if}
									{#if player.isBlocked}<span class="text-destructive">⛔</span>{/if}
									{#if player.isMuted}<span class="text-muted-foreground">🔇</span>{/if}
									{#if player.ageVerified}<IdCard class="x-tag-age-verification inline-block h-3.5 w-3.5" />{/if}
								</Table.Cell>
							{/if}
							<Table.Cell class="truncate font-medium" style="color: {trust.color}">{player.displayName}</Table.Cell>
							{#if visible('rank')}<Table.Cell class="w-24 truncate text-2xs" style="color: {trust.color}">{player.trustLevel}</Table.Cell>{/if}
							{#if visible('platform')}
								<Table.Cell class="w-10 text-center">
									{#if player.platform === 'standalonewindows'}<Monitor class="x-tag-platform-pc inline-block h-3 w-3" />
									{:else if player.platform === 'android'}<Smartphone class="x-tag-platform-quest inline-block h-3 w-3" />
									{:else if player.platform === 'ios'}<Apple class="x-tag-platform-ios inline-block h-3 w-3" />{/if}
								</Table.Cell>
							{/if}
							{#if visible('language')}
								<Table.Cell class="w-14 text-center">
									{#each languagesOf(player.tags).slice(0, 2) as lang (lang.key)}<span class={['flags mr-0.5 inline-block', languageClass(lang.key)]}></span>{/each}
								</Table.Cell>
							{/if}
							{#if visible('status')}<Table.Cell class="w-10 text-center"><i class={['x-user-status', statusClass(player.status)]}></i></Table.Cell>{/if}
							{#if visible('timer')}
								<Table.Cell class="w-20 text-right text-2xs text-muted-foreground tabular-nums">{player.joinedAt ? timeToText(Math.max(0, now - Date.parse(player.joinedAt)), true) : ''}</Table.Cell>
							{/if}
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{:else}
		<div class={CENTERED_NOTE}>{m.dashboard_widget_instance_not_in_game()}</div>
	{/if}
</div>
