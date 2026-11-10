<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { nearestPageSize, readTableState, restoredPageSize, writeTableState } from '#lib/settings/table-state.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import { COUNT_BADGE, LOADING_OVERLAY } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import TablePager from '#lib/components/common/table-pager.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { imageSrc } from '#lib/api/links.js';
	import { onMount, untrack } from 'svelte';
	import Apple from '@lucide/svelte/icons/apple';
	import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down';
	import Check from '@lucide/svelte/icons/check';
	import Ellipsis from '@lucide/svelte/icons/ellipsis';
	import Eye from '@lucide/svelte/icons/eye';
	import ImageIcon from '@lucide/svelte/icons/image';
	import LayoutGrid from '@lucide/svelte/icons/layout-grid';
	import List from '@lucide/svelte/icons/list';
	import ListFilter from '@lucide/svelte/icons/list-filter';
	import Monitor from '@lucide/svelte/icons/monitor';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import SettingsIcon from '@lucide/svelte/icons/settings';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import Tag from '@lucide/svelte/icons/tag';
	import UserIcon from '@lucide/svelte/icons/user';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import ManageTagsDialog from '#lib/components/avatars/manage-tags-dialog.svelte';
	import MyAvatarCard, { type AvatarAction } from '#lib/components/avatars/my-avatar-card.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import type { Avatar, OwnAvatar, OwnAvatarTag } from '#lib/api/types.js';
	import { formatDateFilter, timeToText } from '#lib/helpers/format.js';
	import { colorForTagName, resolveTagColor } from '#lib/content/tag-colors.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's MyAvatars.vue over GET /1/accounts/{id}/avatars: grid or table of the
	// account's own avatars with visibility, platform and tag filters, search, card scale and
	// spacing, and the per-avatar actions (wear, tags, visibility, rename, description,
	// impostor). Changing content tags, styles and the image need the avatar dialog and an
	// upload flow, which this client does not have yet.

	const PAGE_SIZES = $derived(appearance.tablePageSizes.value);
	const KEYS = { view: 'VRCX_MyAvatarsViewMode', scale: 'VRCX_MyAvatarsCardScale', spacing: 'VRCX_MyAvatarsCardSpacing' };

	let avatars = $state<OwnAvatar[]>([]);
	let isLoading = $state(false);
	let loadError = $state('');
	let message = $state('');
	let searchText = $state('');
	let releaseStatusFilter = $state('all');
	let platformFilter = $state('all');
	let tagFilters = $state<Set<string>>(new Set());
	let viewMode = $state<'grid' | 'table'>(readSetting(KEYS.view, 'grid'));
	let cardScale = $state(Math.min(0.9, Math.max(0.3, Number(readSetting(KEYS.scale, '0.6')) || 0.6)));
	let cardSpacing = $state(Math.min(1.5, Math.max(0.5, Number(readSetting(KEYS.spacing, '1')) || 1)));
	let gridWidth = $state(0);
	let gridEl = $state<HTMLDivElement | null>(null);

	// table
	// The sort and page size stay across visits under the original's vrcx:table:my-avatars name
	// (upstream issue 1230); with no saved page size the Table Page Size setting is used.
	type SortKey = 'name' | 'timeSpent' | 'version' | 'impostor' | 'updated_at' | 'created_at' | 'visibility';
	const SORT_KEYS: SortKey[] = ['name', 'timeSpent', 'version', 'impostor', 'updated_at', 'created_at', 'visibility'];
	const TABLE_KEY = 'my-avatars';
	const savedTable = readTableState(TABLE_KEY);
	const savedSort = savedTable.sorting?.[0];
	let sortKey = $state<SortKey>(savedSort && SORT_KEYS.includes(savedSort.id as SortKey) ? (savedSort.id as SortKey) : 'updated_at');
	let sortDesc = $state(savedSort ? savedSort.desc : true);
	$effect(() => {
		writeTableState(TABLE_KEY, { sorting: [{ id: sortKey, desc: sortDesc }] });
	});
	let pageSize = $state(restoredPageSize(savedTable, tableSettings.pageSize));
	let currentPage = $state(1);

	function setPageSize(size: number) {
		pageSize = size;
		currentPage = 1;
		writeTableState(TABLE_KEY, { pageSize: size });
	}

	// A page size no longer offered moves to the closest offered one, as the original's pager does.
	$effect(() => {
		const next = nearestPageSize(pageSize, PAGE_SIZES);
		if (next !== pageSize) untrack(() => setPageSize(next));
	});

	// dialogs
	let manageTagsOpen = $state(false);
	let manageTagsItem = $state<OwnAvatar | null>(null);
	let confirm = $state<{ title: string; description: string; onConfirm: () => void } | null>(null);
	let prompt = $state<{ title: string; description: string; value: string; onConfirm: (value: string) => void } | null>(null);

	const allTags = $derived(Array.from(new Set(avatars.flatMap((a) => a.tags.map((t) => t.tag)))).sort());
	const activeFilterCount = $derived((releaseStatusFilter !== 'all' ? 1 : 0) + tagFilters.size + (platformFilter !== 'all' ? 1 : 0));
	const currentAvatarId = $derived(avatars.find((a) => a.isCurrent)?.avatar.id ?? '');

	const filtered = $derived.by(() => {
		let list = avatars;
		if (releaseStatusFilter === 'public') list = list.filter((a) => a.avatar.releaseStatus === 'public');
		else if (releaseStatusFilter === 'private') list = list.filter((a) => a.avatar.releaseStatus === 'private');
		if (tagFilters.size) list = list.filter((a) => a.tags.some((t) => tagFilters.has(t.tag)));
		if (platformFilter !== 'all') {
			const platform = platformFilter === 'pc' ? 'standalonewindows' : platformFilter;
			list = list.filter((a) => a.avatar.platforms.includes(platform));
		}
		if (searchText) {
			const q = searchText.toLowerCase();
			list = list.filter((a) => a.avatar.name.toLowerCase().includes(q) || a.tags.some((t) => t.tag.toLowerCase().includes(q)));
		}
		return list;
	});

	const sorted = $derived.by(() => {
		const dir = sortDesc ? -1 : 1;
		const key = (a: OwnAvatar): string | number => {
			switch (sortKey) {
				case 'name':
					return a.avatar.name.toLowerCase();
				case 'timeSpent':
					return a.timeSpentMs;
				case 'version':
					return a.avatar.version ?? 0;
				case 'impostor':
					return a.avatar.impostorVersion ?? '';
				case 'visibility':
					return a.avatar.releaseStatus ?? '';
				case 'created_at':
					return a.avatar.createdAt ?? '';
				default:
					return a.avatar.updatedAt ?? '';
			}
		};
		return [...filtered].sort((x, y) => {
			const a = key(x);
			const b = key(y);
			return (a < b ? -1 : a > b ? 1 : 0) * dir;
		});
	});
	const pageRows = $derived(sorted.slice((currentPage - 1) * pageSize, currentPage * pageSize));

	// The original's useAvatarCardGrid: columns from the container width, cards stretched to fill it.
	const gridStyle = $derived.by(() => {
		const minWidth = 200 * cardScale;
		const gap = Math.max(4, 12 * cardSpacing);
		const width = Math.max(gridWidth, 0);
		const count = filtered.length || 1;
		const maxColumns = width > 0 ? Math.max(1, Math.floor((width + gap) / (minWidth + gap)) || 1) : 1;
		const columns = Math.max(1, Math.min(count, maxColumns));
		let cardWidth = minWidth;
		if (filtered.length >= maxColumns) {
			const raw = (width - gap * (columns - 1)) / columns;
			if (Number.isFinite(raw) && raw > 0) cardWidth = Math.max(minWidth, raw);
		}
		return `grid-template-columns: repeat(${columns}, minmax(${Math.round(minWidth)}px, ${Math.round(cardWidth)}px)); gap: ${Math.round(gap)}px`;
	});

	/** Opening the page reads what the server has; Refresh and edits ask it to fetch from VRChat again. */
	async function refreshAvatars(fromVrchat = true) {
		if (!vrchatAuth.ownerUserId || isLoading) return;
		isLoading = true;
		loadError = '';
		try {
			avatars = await api.get<OwnAvatar[]>(ownPath('/avatars'), fromVrchat ? { refresh: true } : undefined);
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			isLoading = false;
		}
	}

	function toggleTagFilter(tag: string) {
		const next = new Set(tagFilters);
		if (next.has(tag)) next.delete(tag);
		else next.add(tag);
		tagFilters = next;
	}
	function clearFilters() {
		releaseStatusFilter = 'all';
		tagFilters = new Set();
		platformFilter = 'all';
	}

	function confirmAndRun(command: string, fn: () => void) {
		confirm = { get title() { return m.confirm_title(); }, description: `Are you sure you want to ${command}?`, onConfirm: fn };
	}

	async function call(fn: () => Promise<unknown>, success: string) {
		try {
			await fn();
			message = success;
			await refreshAvatars();
		} catch (err) {
			message = ApiError.from(err).message;
		}
	}

	function handleWear(item: OwnAvatar) {
		if (item.isCurrent) return;
		confirm = {
			get title() { return m.confirm_title(); },
			description: `Continue? Select Avatar\n${item.avatar.name}`,
			onConfirm: () => call(() => api.post(ownPath(`/avatars/${encodeURIComponent(item.avatar.id)}/select`)), 'Avatar changed')
		};
	}

	function handleAction(action: AvatarAction, item: OwnAvatar) {
		const path = (rest: string) => ownPath(`/avatars/${encodeURIComponent(item.avatar.id)}${rest}`);
		switch (action) {
			case 'details':
				userDialog.openAvatar(item.avatar.id);
				break;
			case 'wear':
				handleWear(item);
				break;
			case 'makePrivate':
				confirmAndRun(m.dialog_avatar_actions_make_private(), () => call(() => api.patch(path(''), { releaseStatus: 'private' }), 'Avatar updated to private'));
				break;
			case 'makePublic':
				confirmAndRun(m.dialog_avatar_actions_make_public(), () => call(() => api.patch(path(''), { releaseStatus: 'public' }), 'Avatar updated to public'));
				break;
			case 'rename':
				prompt = {
					get title() { return m.prompt_rename_avatar_header(); },
					get description() { return m.prompt_rename_avatar_description(); },
					value: item.avatar.name,
					onConfirm: (value) => {
						if (value && value !== item.avatar.name) call(() => api.patch(path(''), { name: value }), 'Avatar renamed');
					}
				};
				break;
			case 'changeDescription':
				prompt = {
					get title() { return m.prompt_change_world_description_header(); },
					get description() { return m.prompt_change_avatar_description_description(); },
					value: item.avatar.description ?? '',
					onConfirm: (value) => {
						if (value && value !== item.avatar.description) call(() => api.patch(path(''), { description: value }), 'Avatar description changed');
					}
				};
				break;
			case 'createImpostor':
				confirmAndRun(m.dialog_avatar_actions_create_impostor(), () => call(() => api.post(path('/impostor')), 'Impostor generation queued'));
				break;
			case 'manageTags':
				manageTagsItem = item;
				manageTagsOpen = true;
				break;
			default:
				message = 'Not available in this client yet.';
		}
	}

	async function saveTags(avatarId: string, tags: OwnAvatarTag[]) {
		try {
			const stored = await api.put<OwnAvatarTag[]>(ownPath(`/avatars/${encodeURIComponent(avatarId)}/tags`), tags);
			avatars = avatars.map((a) => a.avatar.id === avatarId ? { ...a, tags: stored } : a);
		} catch(err) {
			message = ApiError.from(err).message;
		}
	}

	function toggleSort(key: typeof sortKey, descFirst = false) {
		if (sortKey !== key) {
			sortKey = key;
			sortDesc = descFirst;
		} else sortDesc = !sortDesc;
	}

	function tagStyle(entry: OwnAvatarTag): string {
		const hashed = colorForTagName(entry.tag);
		const color = resolveTagColor(entry.tag, entry.color ?? '');
		return entry.color ? `background-color: ${color.bg}; color: ${color.text}` : `background-color: ${hashed.bg}; color: ${hashed.text}`;
	}

	function isPlatform(a: Avatar, p: string) {
		return a.platforms.includes(p);
	}

	onMount(() => {
		const observer = new ResizeObserver((entries) => {
			for (const e of entries) gridWidth = e.contentRect.width;
		});
		if (gridEl) observer.observe(gridEl);
		return () => observer.disconnect();
	});
	$effect(() => {
		if (gridEl) gridWidth = gridEl.clientWidth;
	});
	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(() => refreshAvatars(false));
	});
</script>

<svelte:head>
	<title>My Avatars · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	{#if !vrchatAuth.ownerUserId}
		<Card.Root>
			<Card.Header><Card.Description>Log in to VRChat to see your avatars.</Card.Description></Card.Header>
		</Card.Root>
	{:else}
		<div class="grid h-full min-h-0 grid-rows-[auto_1fr] gap-4 overflow-hidden">
			<div class="flex items-center gap-2 px-0.5 pt-1.5">
				<ToggleGroup.Root
					type="single"
					value={viewMode}
					variant="outline"
					onValueChange={(v) => {
						if (v === 'grid' || v === 'table') {
							viewMode = v;
							storeSetting(KEYS.view, v);
						}
					}}
				>
					<ToggleGroup.Item value="grid" class="px-2" aria-label={m.view_my_avatars_grid_view()} title={m.view_my_avatars_grid_view()}><LayoutGrid class="size-4" /></ToggleGroup.Item>
					<ToggleGroup.Item value="table" class="px-2" aria-label={m.view_my_avatars_table_view()} title={m.view_my_avatars_table_view()}><List class="size-4" /></ToggleGroup.Item>
				</ToggleGroup.Root>

				<Popover.Root>
					<Popover.Trigger>
						{#snippet child({ props })}
							<Button {...props} variant="outline" size="sm" class="h-8 gap-1.5">
								<ListFilter class="size-4" />
								Filter
								{#if activeFilterCount}<Badge variant="secondary" class={COUNT_BADGE}>{activeFilterCount}</Badge>{/if}
							</Button>
						{/snippet}
					</Popover.Trigger>
					<Popover.Content class="w-80 p-3" align="start">
						<div class="flex flex-col gap-3">
							<div>
								<Label>{m.dialog_group_info_visibility()}</Label>

								<ToggleGroup.Root
									type="single"
									value={releaseStatusFilter}
									variant="outline"
									class="mt-1"
									onValueChange={(v) => releaseStatusFilter = v || 'all'}
								>
									<ToggleGroup.Item value="all" class="px-2.5">{m.view_search_avatar_all()}</ToggleGroup.Item>
									<ToggleGroup.Item value="public" class="px-2.5">{m.dialog_world_tags_public()}</ToggleGroup.Item>
									<ToggleGroup.Item value="private" class="px-2.5">{m.view_favorite_private()}</ToggleGroup.Item>
								</ToggleGroup.Root>
							</div>
							<div>
								<Label>{m.dialog_world_info_platform()}</Label>

								<ToggleGroup.Root
									type="single"
									value={platformFilter}
									variant="outline"
									class="mt-1"
									onValueChange={(v) => platformFilter = v || 'all'}
								>
									<ToggleGroup.Item value="all" class="px-2.5">{m.view_search_avatar_all()}</ToggleGroup.Item>
									<ToggleGroup.Item value="pc" class="px-2.5">PC</ToggleGroup.Item>
									<ToggleGroup.Item value="android" class="px-2.5">{m.dialog_group_event_edit_platform_values_android()}</ToggleGroup.Item>
									<ToggleGroup.Item value="ios" class="px-2.5">{m.dialog_group_event_edit_platform_values_ios()}</ToggleGroup.Item>
								</ToggleGroup.Root>
							</div>
							{#if allTags.length}
								<div>
									<Label>{m.dialog_avatar_info_tags()}</Label>
									<div class="mt-1 flex flex-wrap gap-1">
										{#each allTags as tag (tag)}
											{@const color = colorForTagName(tag)}
											<Badge
												variant={tagFilters.has(tag) ? 'default' : 'outline'}
												class="cursor-pointer select-none"
												style={tagFilters.has(tag) ? `background-color: ${color.bg}; color: ${color.text}` : `border-color: ${color.bg}; color: ${color.text}`}
												onclick={() => toggleTagFilter(tag)}>{tag}</Badge
											>
										{/each}
									</div>
								</div>
							{/if}
							{#if activeFilterCount}
								<Button variant="outline" size="sm" class="w-full" onclick={clearFilters}>{m.view_moderation_clear_all()}</Button>
							{/if}
						</div>
					</Popover.Content>
				</Popover.Root>

				<div class="flex-1"></div>
				{#if isLoading}<span class="text-sm text-muted-foreground">{m.view_friends_locations_loading_more()}</span>{/if}
				<Input bind:value={searchText} placeholder={m.view_favorite_worlds_search()} class="h-8 w-80" />

				{#if viewMode === 'grid'}
					<DropdownMenu.Root>
						<DropdownMenu.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="rounded-full" size="icon-sm" variant="ghost" aria-label={m.view_settings_category_appearance()}><SettingsIcon class="size-4" /></Button>
							{/snippet}
						</DropdownMenu.Trigger>
						<DropdownMenu.Content class="w-60 p-3" align="end">
							<div class="grid gap-3">
								<div class="flex items-center justify-between">
									<span class="text-compact font-medium">{m.view_friends_locations_scale()}</span>
									<span class="min-w-[42px] text-right text-xs font-semibold">{Math.round(cardScale * 100)}%</span>
								</div>
								<Slider type="single" bind:value={cardScale} min={0.3} max={0.9} step={0.01} onValueCommit={(v) => storeSetting(KEYS.scale, String(v))} />
								<div class="flex items-center justify-between">
									<span class="text-compact font-medium">{m.view_friends_locations_spacing()}</span>
									<span class="min-w-[42px] text-right text-xs font-semibold">{Math.round(cardSpacing * 100)}%</span>
								</div>
								<Slider type="single" bind:value={cardSpacing} min={0.5} max={1.5} step={0.05} onValueCommit={(v) => storeSetting(KEYS.spacing, String(v))} />
							</div>
						</DropdownMenu.Content>
					</DropdownMenu.Root>
				{/if}

				<Button size="icon-sm" variant="ghost" disabled={isLoading} onclick={() => refreshAvatars()} aria-label={m.dialog_note_export_refresh()}><RefreshCw class={isLoading ? 'animate-spin' : ''} /></Button>
			</div>

			{#if loadError}
				<p class="text-sm text-destructive">Couldn't load your avatars: {loadError}</p>
			{:else if viewMode === 'table'}
				<div class="flex min-h-0 flex-col overflow-hidden">
					<div class="relative min-h-0 flex-1 overflow-auto rounded-md border">
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head class="sticky top-0 z-10 w-10 bg-background" />
									<Table.Head class="sticky top-0 z-10 w-16 bg-background" />

									<Table.Head class="sticky top-0 z-10 bg-background">
										<Button
											variant="ghost"
											size="sm"
											class="-ml-2 h-8 px-2"
											onclick={() => toggleSort('name')}
										>{m.dialog_group_edit_name()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button>
									</Table.Head>

									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_avatar_info_tags()}</Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_world_info_platform()}</Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_group_info_visibility()}</Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background text-right"><Button variant="ghost" size="sm" class="-ml-2 h-8 px-2" onclick={() => toggleSort('timeSpent', true)}>{m.dialog_world_info_time_spent()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button></Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background text-right"><Button variant="ghost" size="sm" class="-ml-2 h-8 px-2" onclick={() => toggleSort('version', true)}>{m.dialog_world_info_version()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button></Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background text-right"><Button variant="ghost" size="sm" class="-ml-2 h-8 px-2" onclick={() => toggleSort('impostor', true)}>{m.dialog_avatar_tags_impostor()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button></Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_avatar_info_pc_performance()}</Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_avatar_info_android_performance()}</Table.Head>
									<Table.Head class="sticky top-0 z-10 bg-background">{m.dialog_avatar_info_ios_performance()}</Table.Head>

									<Table.Head class="sticky top-0 z-10 bg-background">
										<Button
											variant="ghost"
											size="sm"
											class="-ml-2 h-8 px-2"
											onclick={() => toggleSort('updated_at', true)}
										>{m.dialog_world_info_last_updated()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button>
									</Table.Head>

									<Table.Head class="sticky top-0 z-10 bg-background">
										<Button
											variant="ghost"
											size="sm"
											class="-ml-2 h-8 px-2"
											onclick={() => toggleSort('created_at', true)}
										>{m.dialog_avatar_info_created_at()}<ArrowUpDown class="ml-1 h-4 w-4" /></Button>
									</Table.Head>

									<Table.Head class="sticky top-0 z-10 w-24 bg-background" />
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each pageRows as item (item.avatar.id)}
									{@const a = item.avatar}

									<Table.Row
										class="cursor-pointer {item.isCurrent ? 'bg-primary/10 hover:bg-primary/15' : ''}"
									>
										<Table.Cell>
											<div class="flex items-center justify-center">
												<Check
													class="h-4 w-4 {item.isCurrent ? 'text-primary' : 'invisible'}"
												/>
											</div>
										</Table.Cell>

										<Table.Cell>
											{#if a.thumbnailImageUrl}
												<img src={imageSrc(a.thumbnailImageUrl)} alt="" class="dim-thumbnail rounded-sm object-cover" style="width: 34px; height: 22px" loading="lazy" />
											{:else}
												<div class="flex items-center justify-center rounded-sm bg-muted" style="width: 34px; height: 22px"><ImageIcon class="h-3 w-3 text-muted-foreground" /></div>
											{/if}
										</Table.Cell>
										<Table.Cell>{a.name}</Table.Cell>
										<Table.Cell>
											{#if item.tags.length}
												<div class="flex flex-nowrap gap-1 overflow-hidden">
													{#each item.tags as entry (entry.tag)}<Badge variant="secondary" class="text-xs" style={tagStyle(entry)}>{entry.tag}</Badge>{/each}
												</div>
											{/if}
										</Table.Cell>
										<Table.Cell>
											<div class="flex items-center gap-1">
												{#if isPlatform(a, 'standalonewindows')}<Badge class="border-platform-pc! text-platform-pc" variant="outline"><Monitor class="h-3.5 w-3.5" /></Badge>{/if}
												{#if isPlatform(a, 'android')}<Badge class="border-platform-quest! text-platform-quest" variant="outline"><Smartphone class="h-3.5 w-3.5" /></Badge>{/if}
												{#if isPlatform(a, 'ios')}<Badge class="border-platform-ios text-platform-ios" variant="outline"><Apple class="h-3.5 w-3.5" /></Badge>{/if}
											</div>
										</Table.Cell>
										<Table.Cell><Badge variant="outline">{a.releaseStatus === 'public' ? 'Public' : 'Private'}</Badge></Table.Cell>
										<Table.Cell class="text-right">{item.timeSpentMs ? timeToText(item.timeSpentMs) : '-'}</Table.Cell>
										<Table.Cell class="text-right">{a.version ?? '-'}</Table.Cell>
										<Table.Cell class="text-right">{a.impostorVersion ? `v${a.impostorVersion}` : '-'}</Table.Cell>
										<Table.Cell>{#if a.performancePc}{a.performancePc}{:else}<span class="text-muted-foreground">-</span>{/if}</Table.Cell>
										<Table.Cell>{#if a.performanceAndroid}{a.performanceAndroid}{:else}<span class="text-muted-foreground">-</span>{/if}</Table.Cell>
										<Table.Cell>{#if a.performanceIos}{a.performanceIos}{:else}<span class="text-muted-foreground">-</span>{/if}</Table.Cell>
										<Table.Cell>{formatDateFilter(a.updatedAt, 'short')}</Table.Cell>
										<Table.Cell>{formatDateFilter(a.createdAt, 'short')}</Table.Cell>
										<Table.Cell>
											<DropdownMenu.Root>
												<DropdownMenu.Trigger>
													{#snippet child({ props })}
														<Button {...props} class="h-6 w-6 rounded-full" size="icon" variant="ghost" aria-label="More actions"><Ellipsis class="h-4 w-4" /></Button>
													{/snippet}
												</DropdownMenu.Trigger>
												<DropdownMenu.Content>
													<DropdownMenu.Item onSelect={() => handleAction('details', item)}><Eye class="size-4" />{m.dialog_avatar_actions_view_details()}</DropdownMenu.Item>
													<DropdownMenu.Item disabled={item.isCurrent} onSelect={() => handleAction('wear', item)}><Check class="size-4" />{m.dialog_avatar_actions_select()}</DropdownMenu.Item>
													<DropdownMenu.Separator />
													<DropdownMenu.Item onSelect={() => handleAction('manageTags', item)}><Tag class="size-4" />{m.dialog_avatar_actions_manage_tags()}</DropdownMenu.Item>
													<DropdownMenu.Separator />
													<DropdownMenu.Item onSelect={() => handleAction(a.releaseStatus === 'public' ? 'makePrivate' : 'makePublic', item)}><UserIcon class="size-4" />{a.releaseStatus === 'public' ? 'Make Private' : 'Make Public'}</DropdownMenu.Item>
													<DropdownMenu.Item onSelect={() => handleAction('rename', item)}><Pencil class="size-4" />{m.dialog_world_actions_rename()}</DropdownMenu.Item>
													<DropdownMenu.Item onSelect={() => handleAction('changeDescription', item)}><Pencil class="size-4" />{m.dialog_world_actions_change_description()}</DropdownMenu.Item>
													<DropdownMenu.Item disabled><Pencil class="size-4" />{m.dialog_avatar_actions_change_content_tags()}</DropdownMenu.Item>
													<DropdownMenu.Item disabled><Pencil class="size-4" />{m.dialog_avatar_actions_change_styles_author_tags()}</DropdownMenu.Item>
													<DropdownMenu.Item disabled><ImageIcon class="size-4" />{m.dialog_world_actions_change_image()}</DropdownMenu.Item>
													<DropdownMenu.Item onSelect={() => handleAction('createImpostor', item)}><RefreshCw class="size-4" />{m.dialog_avatar_actions_create_impostor()}</DropdownMenu.Item>
												</DropdownMenu.Content>
											</DropdownMenu.Root>
										</Table.Cell>
									</Table.Row>
								{:else}
									<Table.Row>
										<Table.Cell colspan={15} class="h-24 text-center">
											{#if !isLoading}
												<DataTableEmpty type="nomatch" />
											{/if}
										</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
						{#if isLoading}
							<div class={LOADING_OVERLAY}><Spinner class="size-6" /></div>
						{/if}
					</div>
					<TablePager
						count={filtered.length}
						{pageSize}
						pageSizes={PAGE_SIZES}
						onPageSizeChange={(v) => setPageSize(Number(v))}
						bind:page={currentPage}
					/>
				</div>
			{:else}
				<div class="min-h-0 overflow-auto py-2" bind:this={gridEl}>
					{#if filtered.length}
						<div class="grid p-1" style={gridStyle}>
							{#each filtered as item (item.avatar.id)}
								<MyAvatarCard
									item={item}
									cardScale={cardScale}
									onclick={() => handleWear(item)}
									onaction={handleAction}
								/>
							{/each}
						</div>
					{:else if !isLoading}
						<div class="grid min-h-60 place-items-center text-[15px]">
							<DataTableEmpty type="nomatch" />
						</div>
					{/if}
				</div>
			{/if}
			{#if message}<p class="text-xs text-muted-foreground">{message}</p>{/if}
		</div>

		<ManageTagsDialog bind:open={manageTagsOpen} avatarName={manageTagsItem?.avatar.name ?? ''} avatarId={manageTagsItem?.avatar.id ?? ''} initialTags={manageTagsItem?.tags ?? []} onsave={saveTags} />

		<Dialog.Root open={confirm !== null} onOpenChange={(o) => !o && (confirm = null)}>
			<Dialog.Content>
				<Dialog.Header>
					<Dialog.Title>{confirm?.title}</Dialog.Title>
					<Dialog.Description class="whitespace-pre-line">{confirm?.description}</Dialog.Description>
				</Dialog.Header>
				<Dialog.Footer>
					<Button variant="outline" onclick={() => confirm = null}>{m.view_login_qr_cancel()}</Button>
					<Button
						onclick={() => {
							const c = confirm;
							confirm = null;
							c?.onConfirm();
						}}>{m.dialog_alertdialog_confirm()}</Button
					>
				</Dialog.Footer>
			</Dialog.Content>
		</Dialog.Root>

		<Dialog.Root open={prompt !== null} onOpenChange={(o) => !o && (prompt = null)}>
			<Dialog.Content>
				<Dialog.Header>
					<Dialog.Title>{prompt?.title}</Dialog.Title>
					<Dialog.Description>{prompt?.description}</Dialog.Description>
				</Dialog.Header>
				{#if prompt}
					<Input bind:value={prompt.value} />
				{/if}
				<Dialog.Footer>
					<Button variant="outline" onclick={() => prompt = null}>{m.view_login_qr_cancel()}</Button>
					<Button
						onclick={() => {
							const p = prompt;
							prompt = null;
							if (p) p.onConfirm(p.value.trim());
						}}>{m.dialog_alertdialog_ok()}</Button
					>
				</Dialog.Footer>
			</Dialog.Content>
		</Dialog.Root>
	{/if}
</Tooltip.Provider>

