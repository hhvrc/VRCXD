<script lang="ts">
	import { sameInstanceGroups } from '#lib/people/friend-groups.js';
	import { PERSON_NAME } from '#lib/details.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import BackToTop from '#lib/components/common/back-to-top.svelte';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import type { Friend } from '#lib/api/types.js';
	import { iconSrc } from '#lib/api/links.js';
	import { friendComparator } from '#lib/people/friend-sort.js';
	import { canInvite, canInviteSelf, desktop, instanceDialogs, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { isRealInstance, parseLocation } from '#lib/instances/location.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
	import { editProfile } from '#lib/content/edit-profile.svelte.js';
	import { statusPresets } from '#lib/settings/status-presets.svelte.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import FriendItem from './friend-item.svelte';
	import { ownStatusClass } from './sidebar-utils';

	// The original's FriendsSidebar: Me, Favorites (optionally split by group), Same Instance, Online,
	// Active and Offline, each section folding and remembered.
	type Row =
		| { type: 'toggle'; key: string; label: string; count?: number; expanded: boolean; padding?: string; onclick: () => void }
		| { type: 'me'; key: string }
		| { type: 'instance'; key: string; location: string; count: number }
		| { type: 'friend'; key: string; friend: Friend; groupByInstance?: boolean; last?: boolean };

	const STATUS_OPTIONS = [
		{ value: 'join me', className: 'joinme', get label() { return m.dialog_user_status_join_me(); } },
		{ value: 'active', className: 'online', get label() { return m.view_friends_locations_online(); } },
		{ value: 'ask me', className: 'askme', get label() { return m.dialog_user_status_ask_me(); } },
		{ value: 'busy', className: 'busy', get label() { return m.dialog_user_status_busy(); } }
	];

	function fold(key: string, fallback = true) {
		let value = $state(readSetting<boolean>(key, fallback));
		return {
			get value() {
				return value;
			},
			toggle() {
				value = !value;
				storeSetting(key, value);
			}
		};
	}
	const meOpen = fold('VRCX_isFriendsGroupMe');
	const favoritesOpen = fold('VRCX_isFriendsGroupFavorites');
	const onlineOpen = fold('VRCX_isFriendsGroupOnline');
	const activeOpen = fold('VRCX_isFriendsGroupActive');
	const offlineOpen = fold('VRCX_isFriendsGroupOffline');
	const instancesCollapsed = fold('VRCX_sidebarGroupByInstanceCollapsed', false);
	let collapsedGroups = $state<string[]>([]);
	const now = $derived(clock(1000).now);
	let notice = $state('');
	let boopFor = $state<Friend | undefined>();
	let boopOpen = $state(false);

	const account = $derived(vrchatAuth.ownerUserId);
	const me = $derived(sidebarData.me);
	const presets = $derived(statusPresets.list);
	const friendIds = $derived(new Set(sidebarData.friends.map((f) => f.userId)));

	const compare = $derived(friendComparator(sidebarSettings.sortMethods));
	const online = $derived(sidebarData.friends.filter((f) => f.state === 'online').sort(compare));
	const active = $derived(sidebarData.friends.filter((f) => f.state === 'active').sort(compare));
	const offline = $derived(sidebarData.friends.filter((f) => f.state !== 'online' && f.state !== 'active').sort(compare));

	const selectedGroups = $derived(sidebarData.vipGroups);
	const favoriteIds = $derived(sidebarData.vipIds);

	const sameInstance = $derived(sameInstanceGroups(online, (friend) => sidebarData.sameInstanceLocation(friend)));
	const sameInstanceIds = $derived(new Set(sameInstance.flatMap(({ members }) => members.map((f) => f.userId))));
	const hideSame = $derived(sidebarSettings.groupByInstance.value && sidebarSettings.hideSameInstance.value);
	const visible = (list: Friend[]) => (hideSame ? list.filter((f) => !sameInstanceIds.has(f.userId)) : list);

	const favoriteOnline = $derived(visible(online.filter((f) => favoriteIds.has(f.userId))));
	const onlineRest = $derived(visible(online.filter((f) => !favoriteIds.has(f.userId))));

	const favoritesByGroup = $derived.by(() => {
		const order = sidebarSettings.favoriteGroupOrder;
		return selectedGroups
			.map((group) => ({ group, members: favoriteOnline.filter((f) => group.members.has(f.userId)) }))
			.filter(({ members }) => members.length > 0)
			.sort((a, b) => {
				const [left, right] = [order.indexOf(a.group.key), order.indexOf(b.group.key)];
				if (left !== -1 && right !== -1) return left - right;
				if (left !== -1) return -1;
				if (right !== -1) return 1;
				return a.group.key.localeCompare(b.group.key);
			});
	});

	function favoriteRows(rows: Row[]) {
		const split = sidebarSettings.divideByFriendGroup.value;
		const count = split ? favoritesByGroup.reduce((sum, g) => sum + g.members.length, 0) : favoriteOnline.length;
		if (count) rows.push({ type: 'toggle', key: 'vip-header', get label() { return m.side_panel_favorite(); }, count, expanded: favoritesOpen.value, onclick: favoritesOpen.toggle });
		if (!favoritesOpen.value) return;
		if (split) {
			for (const { group, members } of favoritesByGroup) {
				const expanded = !collapsedGroups.includes(group.key);
				rows.push({
					type: 'toggle',
					key: `vip-sub:${group.key}`,
					label: group.name,
					count: members.length,
					expanded,
					padding: '4px 0 4px 4px',
					onclick: () => (collapsedGroups = expanded ? [...collapsedGroups, group.key] : collapsedGroups.filter((k) => k !== group.key))
				});
				if (expanded) members.forEach((friend, i) => rows.push({ type: 'friend', key: `vip:${group.key}:${friend.userId}`, friend, last: i === members.length - 1 }));
			}
		} else {
			for (const friend of favoriteOnline) rows.push({ type: 'friend', key: `vip:${friend.userId}`, friend });
		}
	}

	function sameInstanceRows(rows: Row[]) {
		if (!sidebarSettings.groupByInstance.value || !sameInstance.length) return;
		rows.push({ type: 'toggle', key: 'same-header', get label() { return m.side_panel_same_instance(); }, count: sameInstance.length, expanded: !instancesCollapsed.value, onclick: instancesCollapsed.toggle });
		if (instancesCollapsed.value) return;
		for (const { instanceId: location, members } of sameInstance) {
			rows.push({ type: 'instance', key: `instance:${location}`, location, count: members.length });
			members.forEach((friend, i) => rows.push({ type: 'friend', key: `instance:${location}:${friend.userId}`, friend, groupByInstance: true, last: i === members.length - 1 }));
		}
	}

	const rows = $derived.by(() => {
		const list: Row[] = [{ type: 'toggle', key: 'me-header', get label() { return m.side_panel_me(); }, expanded: meOpen.value, padding: '0 0 5px', onclick: meOpen.toggle }];
		if (meOpen.value) list.push({ type: 'me', key: 'me' });
		if (sidebarSettings.sameInstanceAboveFavorites.value) {
			sameInstanceRows(list);
			favoriteRows(list);
		} else {
			favoriteRows(list);
			sameInstanceRows(list);
		}
		const sections: [string, string, Friend[], { value: boolean; toggle: () => void }][] = [
			['online', 'ONLINE', onlineRest, onlineOpen],
			['active', 'ACTIVE', active, activeOpen],
			['offline', 'OFFLINE', offline, offlineOpen]
		];
		for (const [key, label, friends, state] of sections) {
			if (friends.length) list.push({ type: 'toggle', key: `${key}-header`, label, count: friends.length, expanded: state.value, onclick: state.toggle });
			if (state.value) for (const friend of friends) list.push({ type: 'friend', key: `${key}:${friend.userId}`, friend });
		}
		return list;
	});

	function say(text: string) {
		notice = text;
		setTimeout(() => notice === text && (notice = ''), 3000);
	}

	async function act(work: () => Promise<unknown>, done: string) {
		try {
			await work();
			say(done);
		} catch (err) {
			say(ApiError.from(err).message);
		}
	}

	const saveMe = (changes: { status?: string; statusDescription?: string }) => act(() => sidebarData.saveMe(changes), 'Status updated');
	const presetText = (p: { status: string; statusDescription: string }) => p.statusDescription || STATUS_OPTIONS.find((o) => o.value === p.status)?.label || p.status;

	function invite(friend: Friend) {
		let location = sidebarData.currentLocation ?? '';
		if (location === 'traveling') location = me?.travelingToLocation ?? '';
		if (!location) return;
		act(() => api.post(accountPath(account, `/invites/${encodeURIComponent(friend.userId)}`), { location, worldName: parseLocation(location).worldId }), 'Invite sent');
	}

	// The original's BackToTop for this list.
	let scroller = $state<HTMLElement | undefined>();
</script>

<div class="relative h-full">
	<div bind:this={scroller} class="h-full w-full overflow-auto overflow-x-hidden">
		<div class="px-1.5 py-2.5">
			{#each rows as row (row.key)}
				<div style="content-visibility: auto; contain-intrinsic-size: auto {row.type === 'friend' ? 48 : 28}px">
					{#if row.type === 'toggle'}
						<div class="flex cursor-pointer items-center pt-4 pb-1.5 text-xs" style={row.padding ? `padding: ${row.padding}` : undefined} role="button" tabindex="0" onclick={row.onclick} onkeydown={(e) => e.key === 'Enter' && row.onclick()}>
							<ChevronDown class={['size-4 transition-transform duration-200 ease-in-out', !row.expanded && '-rotate-90']} />
							<span class="ml-1.5">{row.label}{#if row.count !== undefined}&nbsp;&horbar; {row.count}{/if}</span>
						</div>
					{:else if row.type === 'me'}
						{#if me}
							<ContextMenu.Root>
								<ContextMenu.Trigger>
									{#snippet child({ props })}
										<div {...props} class="friend-row box-border flex cursor-pointer items-center p-1.5 text-compact hover:rounded-lg hover:bg-muted/50" role="button" tabindex="0" onclick={() => userDialog.open(me.id)} onkeydown={(e) => e.key === 'Enter' && userDialog.open(me.id)}>
											<PersonAvatar src={iconSrc(me.imageUrl)} statusClass={ownStatusClass(me.status, me.platform)} iconFrame={me.iconFrame} frameEnabled={sidebarSettings.cosmetics.value} fallbackClass="size-5" />
											<div class="flex h-9 flex-1 flex-col justify-between overflow-hidden">
												<span class={PERSON_NAME}>{me.displayName}</span>
												{#if desktop.gameRunning && sidebarData.currentLocation}
													<Location class="extra block truncate text-xs" location={sidebarData.currentLocation} traveling={me.travelingToLocation ?? undefined} link={false} />
												{:else if isRealInstance(me.location) || isRealInstance(me.travelingToLocation)}
													<Location class="extra block truncate text-xs" location={me.location} traveling={me.travelingToLocation ?? undefined} link={false} />
												{:else}
													<span class="extra block truncate text-xs">{me.statusDescription}</span>
												{/if}
											</div>
										</div>
									{/snippet}
								</ContextMenu.Trigger>
								<ContextMenu.Content>
									<ContextMenu.Sub>
										<ContextMenu.SubTrigger>Edit Status</ContextMenu.SubTrigger>
										<ContextMenu.SubContent>
											{#each STATUS_OPTIONS as option (option.value)}
												<ContextMenu.CheckboxItem class="gap-2" checked={me.status === option.value} onSelect={() => saveMe({ status: option.value })}>
													<i class={['x-user-status', option.className]}></i>{option.label}
												</ContextMenu.CheckboxItem>
											{/each}
										</ContextMenu.SubContent>
									</ContextMenu.Sub>
									<ContextMenu.Sub>
										<ContextMenu.SubTrigger>Status History</ContextMenu.SubTrigger>
										<ContextMenu.SubContent>
											<ContextMenu.CheckboxItem checked={!me.statusDescription} onSelect={() => saveMe({ statusDescription: '' })}>{m.dialog_gallery_select_none()}</ContextMenu.CheckboxItem>
											{#if me.statusHistory.length}<ContextMenu.Separator />{/if}
											{#each me.statusHistory.slice(0, 10) as item, i (i)}
												<ContextMenu.CheckboxItem checked={me.statusDescription === item} onSelect={() => saveMe({ statusDescription: item })}>{item}</ContextMenu.CheckboxItem>
											{/each}
										</ContextMenu.SubContent>
									</ContextMenu.Sub>
									{#if presets.length}
										<ContextMenu.Sub>
											<ContextMenu.SubTrigger>{m.dialog_social_status_presets()}</ContextMenu.SubTrigger>
											<ContextMenu.SubContent>
												{#each presets as preset, i (i)}
													<ContextMenu.Item class="gap-2" onSelect={() => saveMe({ status: preset.status, statusDescription: preset.statusDescription })}>
														<i class={['x-user-status', STATUS_OPTIONS.find((o) => o.value === preset.status)?.className]}></i>
														<span class="max-w-45 truncate">{presetText(preset)}</span>
													</ContextMenu.Item>
												{/each}
											</ContextMenu.SubContent>
										</ContextMenu.Sub>
									{/if}
									<ContextMenu.Item onSelect={() => editProfile.show()}>{m.dialog_user_actions_edit_profile()}</ContextMenu.Item>
								</ContextMenu.Content>
							</ContextMenu.Root>
						{/if}
					{:else if row.type === 'instance'}
						<div class="mb-1 flex items-center pb-1">
							<Location class="inline text-xs" location={row.location} />
							<span class="ml-1.5 text-xs">({row.count})</span>
						</div>
					{:else}
						{@const friend = row.friend}
						{@const where = friend.location ?? ''}
						{@const hasLocation = friend.state === 'online' && isRealInstance(where)}
						<div style={row.last ? 'padding-bottom: 6px' : undefined}>
							<ContextMenu.Root>
								<ContextMenu.Trigger>
									{#snippet child({ props })}
										<div {...props}><FriendItem {friend} groupByInstance={row.groupByInstance} favorite={favoriteIds.has(friend.userId)} {now} /></div>
									{/snippet}
								</ContextMenu.Trigger>
								<ContextMenu.Content>
									{#if friend.state === 'online'}
										<ContextMenu.Item onSelect={() => act(() => api.post(accountPath(account, `/invite-requests/${encodeURIComponent(friend.userId)}`), {}), 'Request invite sent')}>{m.dialog_user_actions_request_invite()}</ContextMenu.Item>
									{/if}
									{#if desktop.gameRunning}
										<ContextMenu.Item disabled={!sidebarData.currentLocation || !canInvite(sidebarData.currentLocation, account, sidebarData.currentLocation)} onSelect={() => invite(friend)}>{m.dialog_launch_invite()}</ContextMenu.Item>
									{/if}
									<ContextMenu.Item
										disabled={me ? !me.isBoopingEnabled : false}
										onSelect={() => {
											boopFor = friend;
											boopOpen = true;
										}}>{m.dialog_user_actions_send_boop()}</ContextMenu.Item
									>
									{#if hasLocation}
										<ContextMenu.Separator />
										<ContextMenu.Item disabled={!canInviteSelf(where, account, friendIds)} onSelect={() => (instanceDialogs.launch = { location: where })}>{m.dialog_user_info_launch_invite_tooltip()}</ContextMenu.Item>
										<ContextMenu.Item disabled={!canInviteSelf(where, account, friendIds)} onSelect={() => act(() => selfInvite(where), 'Self invite sent')}>{m.dialog_user_info_self_invite_tooltip()}</ContextMenu.Item>
									{/if}
								</ContextMenu.Content>
							</ContextMenu.Root>
						</div>
					{/if}
				</div>
			{/each}
		</div>
	</div>
	{#if notice}<div class="absolute right-2 bottom-2 left-2 rounded-md border bg-card px-3 py-2 text-xs shadow">{notice}</div>{/if}
</div>

{#if boopFor}
	{@const target = boopFor}
	<BoopDialog bind:open={boopOpen} name={target.displayName} onsend={(emoji) => act(() => api.post(accountPath(account, `/users/${encodeURIComponent(target.userId)}/boop`), { emojiId: emoji || null }), 'Boop sent')} />
{/if}

<BackToTop target={scroller} />
