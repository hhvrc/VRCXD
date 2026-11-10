<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Bell from '@lucide/svelte/icons/bell';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Search from '@lucide/svelte/icons/search';
	import Settings from '@lucide/svelte/icons/settings';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { notificationCenter } from '#lib/notifications/notification-center.svelte.js';
	import { quickSearch } from '#lib/nav/quick-search.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { SORT_OPTIONS, sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import FriendsPanel from './friends-panel.svelte';
	import GroupOrderDialog from './group-order-dialog.svelte';
	import GroupsPanel from './groups-panel.svelte';
	import NotificationCenter from './notification-center.svelte';

	// The original's Sidebar: search, refresh, notifications and display options above the Friends
	// and Groups tabs.
	const NONE = '__none__';
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);
	let settingsOpen = $state(false);
	let advancedOpen = $state(false);
	let orderOpen = $state(false);
	let tab = $state('friends');

	// VRChat groups first, then a line and the local groups, as the original.
	const groupOptions = $derived(sidebarData.groups.map((g, i, all) => ({ value: g.key, label: g.name, separator: g.key.startsWith('local:') && !all[i - 1]?.key.startsWith('local:') && i > 0 })));
	const allKeys = $derived(groupOptions.map((g) => g.value));
	const shownGroups = $derived(sidebarSettings.favoriteGroups.length ? sidebarSettings.favoriteGroups : allKeys);
	const sortLabel = (value: string) => SORT_OPTIONS.find((o) => o.value === value)?.label ?? 'Sort Order';

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		sidebarData.loadFriends();
		sidebarData.loadMe();
		sidebarData.loadGameInstance();
		sidebarData.loadGroupInstances();
		notificationCenter.load();
		const groupsTimer = setInterval(() => sidebarData.loadGroupInstances(), 5 * 60 * 1000);
		let timer: ReturnType<typeof setTimeout> | undefined;
		let meTimer: ReturnType<typeof setTimeout> | undefined;
		let noteTimer: ReturnType<typeof setTimeout> | undefined;
		const unsubscribe = onLiveEvent((event) => {
			const kind = event.event.case;
			if (kind === 'presence' || kind === 'user' || kind === 'friendship') {
				clearTimeout(timer);
				timer = setTimeout(() => sidebarData.loadFriends(), 1500);
			}
			if (kind === 'notification') {
				clearTimeout(noteTimer);
				noteTimer = setTimeout(() => notificationCenter.load(), 500);
			}
			if (kind === 'content' && event.event.value.kind === 'group') sidebarData.loadGroupInstances();
			if (kind === 'visit' || kind === 'desktop' || kind === 'account') {
				clearTimeout(meTimer);
				meTimer = setTimeout(() => {
					sidebarData.loadMe();
					sidebarData.loadGameInstance();
				}, 1500);
			}
		});
		return () => {
			unsubscribe();
			clearInterval(groupsTimer);
			clearTimeout(timer);
			clearTimeout(meTimer);
			clearTimeout(noteTimer);
		};
	});
</script>

{#snippet toggle(label: string, value: boolean, set: (v: boolean) => void)}
	<div class="flex items-center justify-between gap-2">
		<Label class="text-xs font-normal">{label}</Label>
		<Switch checked={value} onCheckedChange={set} aria-label={label} />
	</div>
{/snippet}

{#snippet sortSelect(index: 0 | 1 | 2, disabled: boolean)}
	{@const value = sidebarSettings.sortMethods[index] ?? ''}
	<Select.Root type="single" value={value || (index ? NONE : '')} {disabled} onValueChange={(v) => sidebarSettings.setSort(index, v === NONE ? '' : v)}>
		<Select.Trigger size="sm" class="w-full">{value ? sortLabel(value) : index ? 'None' : 'Sort Order'}</Select.Trigger>
		<Select.Content>
			{#if index}<Select.Item value={NONE} label={m.dialog_gallery_select_none()} />{/if}
			{#each SORT_OPTIONS as option (option.value)}
				<Select.Item value={option.value} label={option.label} />
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

<div class="flex h-full min-h-0 flex-col bg-sidebar pl-2">
	<div class="flex items-baseline">
		<div class="flex-1 p-2 pl-0">
			<button type="button" class="flex h-9 w-full cursor-pointer items-center gap-2 overflow-hidden rounded-md border border-input bg-transparent px-3 shadow-xs hover:border-ring dark:bg-input/30" onclick={() => quickSearch.show()}>
				<Search class="size-4 shrink-0 opacity-50" />
				<span class="min-w-0 flex-1 truncate text-left text-sm text-muted-foreground">{m.side_panel_search_placeholder()}</span>
				<kbd class="pointer-events-none inline-flex h-5 shrink-0 items-center rounded-sm bg-muted px-1 font-sans text-xs font-medium text-muted-foreground select-none">{isMac ? '⌘' : 'Ctrl'}</kbd>
				<kbd class="pointer-events-none inline-flex h-5 shrink-0 items-center rounded-sm bg-muted px-1 font-sans text-xs font-medium text-muted-foreground select-none">K</kbd>
			</button>
		</div>
		<div class="mx-1 flex items-center gap-1">
			<Button class="rounded-full" variant="ghost" size="icon-sm" title={m.side_panel_refresh_tooltip()} aria-label={m.side_panel_refresh_tooltip()} disabled={sidebarData.refreshing} onclick={() => sidebarData.refresh()}>
				{#if sidebarData.refreshing}<Spinner />{:else}<RefreshCw />{/if}
			</Button>
			<ContextMenu.Root>
				<ContextMenu.Trigger>
					{#snippet child({ props })}
						<Button
							{...props}
							class="relative rounded-full"
							variant="ghost"
							size="icon-sm"
							title={m.side_panel_notification_center_title()}
							aria-label={m.side_panel_notification_center_title()}
							onclick={() => (notificationCenter.open = !notificationCenter.open)}
							oncontextmenu={(event: MouseEvent) => {
								// The original has no menu when nothing is unread, only this note.
								if (notificationCenter.hasUnseen) return (props.oncontextmenu as ((e: MouseEvent) => void) | undefined)?.(event);
								event.preventDefault();
								event.stopPropagation();
								toast.info(m.side_panel_notification_center_no_unseen_notifications());
							}}
						>
							<Bell />
							{#if notificationCenter.hasUnseen}<span class="absolute top-1 right-1.25 size-1.5 rounded-full bg-red-500"></span>{/if}
						</Button>
					{/snippet}
				</ContextMenu.Trigger>
				<ContextMenu.Content>
					<ContextMenu.Item onSelect={() => notificationCenter.markAllSeen()}>Mark All Read</ContextMenu.Item>
				</ContextMenu.Content>
			</ContextMenu.Root>
			<Popover.Root bind:open={settingsOpen}>
				<Popover.Trigger>
					{#snippet child({ props })}
						<Button {...props} class="rounded-full" variant="ghost" size="icon-sm" aria-label={m.view_login_settings()}><Settings /></Button>
					{/snippet}
				</Popover.Trigger>
				<Popover.Content side="bottom" align="end" class="w-64 p-3" onOpenAutoFocus={(e) => e.preventDefault()}>
					<div class="flex flex-col gap-2.5 text-xs">
						<span class="text-2xs font-medium tracking-wide text-muted-foreground uppercase">{m.view_settings_appearance_display_header()}</span>
						{@render toggle('Group by Instance', sidebarSettings.groupByInstance.value, (v) => (sidebarSettings.groupByInstance.value = v))}
						{#if sidebarSettings.groupByInstance.value}
							{@render toggle('Hide Grouped Friends', sidebarSettings.hideSameInstance.value, (v) => (sidebarSettings.hideSameInstance.value = v))}
							{@render toggle('Prioritize Same Instance', sidebarSettings.sameInstanceAboveFavorites.value, (v) => (sidebarSettings.sameInstanceAboveFavorites.value = v))}
						{/if}
						{@render toggle('Show Favorite Groups', sidebarSettings.divideByFriendGroup.value, (v) => (sidebarSettings.divideByFriendGroup.value = v))}
						{@render toggle('Show Cosmetics', sidebarSettings.cosmetics.value, (v) => (sidebarSettings.cosmetics.value = v))}
						<Separator />
						<button type="button" class="flex w-full cursor-pointer items-center justify-between py-0.5 text-2xs font-medium tracking-wide text-muted-foreground uppercase transition-colors hover:text-foreground" onclick={() => (advancedOpen = !advancedOpen)}>
							{m.view_settings_category_advanced()}
							<ChevronDown class={['size-3.5 transition-transform duration-200', advancedOpen && 'rotate-180']} />
						</button>
						{#if advancedOpen}
							<div class="flex flex-col gap-2.5">
								<span class="text-3xs font-medium tracking-wide text-muted-foreground/70 uppercase">{m.side_panel_settings_sorting()}</span>
								{@render sortSelect(0, false)}
								<Label class="text-xs font-normal">{m.side_panel_settings_sort_tertiary()}</Label>
								{@render sortSelect(1, !sidebarSettings.sortMethods[0])}
								<Label class="text-xs font-normal">{m.side_panel_settings_sort_tertiary()}</Label>
								{@render sortSelect(2, !sidebarSettings.sortMethods[1])}
								<Separator />
								<span class="text-3xs font-medium tracking-wide text-muted-foreground/70 uppercase">{m.dialog_world_info_favorites()}</span>
								<Label class="text-xs font-normal">{m.side_panel_settings_favorite_groups()}</Label>
								<MultiSelect class="w-full" compact values={shownGroups} options={groupOptions} placeholder={m.side_panel_settings_favorite_groups_placeholder()} onchange={(v) => sidebarSettings.setFavoriteGroups(v, allKeys)} />
								{#if sidebarSettings.divideByFriendGroup.value}
									<Button
										variant="outline"
										size="sm"
										class="w-full text-sm"
										onclick={() => {
											settingsOpen = false;
											orderOpen = true;
										}}>{m.side_panel_settings_edit_group_order()}</Button
									>
								{/if}
							</div>
						{/if}
					</div>
				</Popover.Content>
			</Popover.Root>
		</div>
	</div>
	<Tabs.Root bind:value={tab} class="mt-1.5 min-h-0 flex-1 gap-0">
		<Tabs.List variant="line" class="w-full">
			<Tabs.Trigger value="friends">{m.dialog_group_tags_friends()} <span class="text-muted-foreground">({sidebarData.onlineCount}/{sidebarData.friends.length})</span></Tabs.Trigger>
			<Tabs.Trigger value="groups">{m.dialog_user_groups_header()} <span class="text-muted-foreground">({sidebarData.groupInstances.length})</span></Tabs.Trigger>
		</Tabs.List>
		<Tabs.Content value="friends" class="min-h-0 overflow-hidden"><FriendsPanel /></Tabs.Content>
		<Tabs.Content value="groups" class="min-h-0 overflow-hidden"><GroupsPanel /></Tabs.Content>
	</Tabs.Root>
</div>
<GroupOrderDialog bind:open={orderOpen} />
<NotificationCenter />
