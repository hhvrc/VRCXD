<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { loadFileAnalysis, type FileAnalysis } from '#lib/content/file-analysis.js';
	import CopyId from '#lib/components/common/copy-id.svelte';
	import { onItemChanged } from '#lib/api/live.svelte.js';
	import FavoriteButton from '#lib/components/favorites/favorite-button.svelte';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import Apple from '@lucide/svelte/icons/apple';
	import Check from '@lucide/svelte/icons/check';
	import CheckCircle from '@lucide/svelte/icons/circle-check';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Monitor from '@lucide/svelte/icons/monitor';
	import MoreHorizontal from '@lucide/svelte/icons/ellipsis';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import UserIcon from '@lucide/svelte/icons/user';
	import XCircle from '@lucide/svelte/icons/circle-x';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import AvatarGallery from '#lib/components/details/avatar-gallery.svelte';
	import AvatarListings from '#lib/components/details/avatar-listings.svelte';
	import ContentImageUpload from '#lib/components/details/content-image-upload.svelte';
	import SetAvatarStylesDialog from '#lib/components/details/set-avatar-styles-dialog.svelte';
	import SetAvatarTagsDialog from '#lib/components/details/set-avatar-tags-dialog.svelte';
	import Download from '@lucide/svelte/icons/download';
	import CopyIdRow from '#lib/components/details/copy-id-row.svelte';
	import InfoRow from '#lib/components/details/info-row.svelte';
	import JsonCard from '#lib/components/details/json-card.svelte';
	import MemoCard from '#lib/components/details/memo-card.svelte';
	import SectionCard from '#lib/components/details/section-card.svelte';
	import ShareMenu from '#lib/components/details/share-menu.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { formatDate, sanitizeEntity, timeAgo, timeToText } from '#lib/helpers/format.js';
	import { userHref, vrchatUrl } from '#lib/api/links.js';
	import type { Avatar } from '#lib/api/types.js';
	import { sdkUnityVersion } from '#lib/content/sdk-unity-version.svelte.js';
	import { BundleCache } from '#lib/agent/bundle-cache.svelte.js';
	import { desktop } from '#lib/instances/instance-launch.svelte.js';
	import { authorTagsText, contentLabel, contentTagsOf, NONE, openPackage, platformPackage, platformText, SECTION, TAB_CONTENT, TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER, CARD, HEADER_CARD, RAIL, TITLE_BUTTON } from '#lib/details.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's avatar dialog (AvatarDialog.vue), class for class.
	let { avatarId }: { avatarId: string } = $props();

	const RATINGS: Record<string, string> = { VeryPoor: 'Very Poor' };

	type Package = { platform?: string; unityVersion?: string; unitySortNumber?: number; performanceRating?: string; variant?: string; assetUrl?: string };
	type LiveAvatar = {
		id: string;
		name: string;
		description?: string | null;
		authorId?: string | null;
		authorName?: string | null;
		imageUrl?: string | null;
		thumbnailImageUrl?: string | null;
		releaseStatus?: string | null;
		tags?: string[];
		version?: number | null;
		created_at?: string | null;
		updated_at?: string | null;
		styles?: { primary?: string; secondary?: string } | null;
		unityPackages?: Package[];
		performance?: { standalonewindows?: string; android?: string; ios?: string } | null;
		unityPackageUrl?: string | null;
		productId?: string | null;
		publishedListings?: { listingId: string; displayName?: string; description?: string; priceTokens?: number; imageId?: string | null }[] | null;
	};
	type Details = { avatar?: LiveAvatar | null; isBlocked: boolean; isCurrent: boolean; timeSpentMs: number };

	let avatar = $state<LiveAvatar | undefined>();
	let isBlocked = $state(false);
	let isCurrent = $state(false);
	let timeSpentMs = $state(0);
	let loadError = $state('');
	let refreshing = $state(false);
	let imageError = $state(false);
	let activeTab = $state('info');
	let tagsOpen = $state(false);
	let stylesOpen = $state(false);
	let imageUpload = $state<ContentImageUpload | undefined>();

	const actions = new DetailActions(() => load(avatarId));
	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(`${accountPath(owner)}/avatars/${encodeURIComponent(avatarId)}`);
	const isOwner = $derived(!!avatar && avatar.authorId === owner);
	const tags = $derived(avatar?.tags ?? []);
	const packages = $derived(avatar?.unityPackages ?? []);
	const contentTags = $derived(contentTagsOf(tags));
	const authorTags = $derived(authorTagsText(tags));
	const isQuestFallback = $derived(tags.some((t) => t.includes('quest')));
	const hasImpostor = $derived(packages.some((p) => p.variant === 'impostor'));
	// The original's avatarDialogPlatform: no impostors, and only packages the SDK's Unity can open.
	const platformLines = $derived(platformText(packages, { sdkUnityVersion: sdkUnityVersion(owner) }));

	function ratingFor(platform: string): string | undefined {
		const stored = platform === 'standalonewindows' ? avatar?.performance?.standalonewindows : platform === 'android' ? avatar?.performance?.android : avatar?.performance?.ios;
		const rating = platformPackage(packages, platform)?.performanceRating || stored;
		return rating ? (RATINGS[rating] ?? rating) : undefined;
	}
	const hasPlatform = (platform: string) => packages.some((p) => p.platform === platform) || !!ratingFor(platform);

	// The original's fileAnalysis: each platform's bundle size, shown on its badge.
	let fileAnalysis = $state<Record<string, FileAnalysis>>({});

	// The original's avatar cache row: VRChat's cached bundle on the PC.
	const bundle = new BundleCache();
	$effect(() => {
		const ref = avatar;
		const sdk = sdkUnityVersion(owner);
		bundle.refresh(ref, sdk);
	});

	async function load(id: string) {
		if (!owner || !id) return;
		loadError = '';
		try {
			// The stored row is what the memo hangs off, and what shows when VRChat hides the avatar.
			const stored = await api.get<Avatar>(`/1/avatars/${encodeURIComponent(id)}`).catch((err) => {
				if (ApiError.from(err).status === 404) return api.get<Avatar>(`/1/avatars/${encodeURIComponent(id)}`, { refresh: true, account: owner }).catch(() => undefined);
				throw err;
			});
			const path = `${accountPath(owner)}/avatars/${encodeURIComponent(id)}`;
			const [details] = await Promise.all([
				api.get<Details>(`${path}/details`).catch(() => undefined),
			]);
			if (id !== avatarId) return;
			const fallback = stored && ({
				...stored,
				created_at: stored.createdAt,
				updated_at: stored.updatedAt,
				performance: { standalonewindows: stored.performancePc, android: stored.performanceAndroid, ios: stored.performanceIos }
			} as unknown as LiveAvatar);
			// The original's sanitizeEntityJson on an avatar it applies; the stored row is clean already.
			const live = details?.avatar;
			const next = live ? sanitizeEntity(live, ['name', 'description']) : fallback;
			if (!next) {
				loadError = 'VRChat will not show this avatar, and the server has never seen it.';
				return;
			}
			avatar = next;
			isBlocked = details?.isBlocked ?? false;
			isCurrent = details?.isCurrent ?? false;
			timeSpentMs = details?.timeSpentMs ?? 0;
			userDialog.setLabel(id, next.name);
			loadFileAnalysis(owner, 'avatars', id).then((found) => id === avatarId && (fileAnalysis = found));
		} catch (err) {
			if (id === avatarId) loadError = ApiError.from(err).message;
		}
	}

	async function refresh() {
		refreshing = true;
		await load(avatarId);
		refreshing = false;
	}

	$effect(() => {
		const id = avatarId;
		if (owner && id) {
			avatar = undefined;
			fileAnalysis = {};
			actions.reset();
			activeTab = 'info';
			imageError = false;
			load(id);
		}
	});




	$effect(() => onItemChanged('avatar', () => avatarId, () => load(avatarId)));
</script>

{#snippet platformBadge(platform: string, colour: string, Icon: typeof Monitor)}
	{#if hasPlatform(platform)}
		<Badge variant="outline" class="mt-1.5 mr-1.5 text-platform-{colour} border-platform-{colour}!">
			<Icon class="text-platform-{colour} h-4 w-4" />
			{#if ratingFor(platform)}<span class="text-platform-{colour} ml-1.5 border-l-[0.8px] border-solid pl-1.5">{ratingFor(platform)}</span>{/if}
			{#if fileAnalysis[platform]?._fileSize}<span class="text-platform-{colour} ml-1.5 border-l-[0.8px] border-solid pl-1.5">{fileAnalysis[platform]._fileSize}</span>{/if}
		</Badge>
	{/if}
{/snippet}

{#if loadError && !avatar}
	<div class="flex flex-col items-start gap-2 p-1.5">
		<p class="text-sm text-destructive">{loadError}</p>
		<CopyId id={avatarId} />
		<Button variant="outline" size="sm" disabled={refreshing} onclick={refresh}>{m.dialog_avatar_actions_refresh()}</Button>
	</div>
{:else if !avatar}
	<div class="flex h-40 items-center justify-center"><Spinner /></div>
{:else}
	<div class="flex min-h-0 min-w-0 flex-1 flex-row" data-slot="avatar-details">
		<div class={RAIL}>
			<div class={HEADER_CARD}>
				<div class="relative aspect-4/3">
					{#if avatar.thumbnailImageUrl && !imageError}
						<ViewableImage src={avatar.thumbnailImageUrl} full={avatar.imageUrl} class="absolute inset-0" imageClass="size-full object-cover" onerror={() => (imageError = true)} />
					{:else}
						<div class="absolute inset-0 flex items-center justify-center bg-muted"><ImageIcon class="size-8 text-muted-foreground" /></div>
					{/if}
				</div>
				<div class="grid grid-cols-[minmax(0,1fr)_auto] gap-x-2 p-3">
					<div class="col-start-1 row-start-1 overflow-hidden">
						<button type="button" class={TITLE_BUTTON} title={avatar.name || avatarId} onclick={() => actions.copy(avatar?.name ?? '')}>{avatar.name || avatarId}</button>
					</div>
					<div class="col-start-1 row-start-2 flex flex-col gap-1">
						{#if avatar.authorId}
							<a class="flex max-w-30 cursor-pointer flex-wrap font-mono text-xs text-muted-foreground" href={userHref(avatar.authorId)}>{avatar.authorName || avatar.authorId}</a>
						{/if}
					</div>
					<div class="col-span-2 flex flex-wrap items-center">
						<Badge variant="outline" class="mt-1.5 mr-1.5">{avatar.releaseStatus === 'public' ? 'Public' : 'Private'}</Badge>
						{@render platformBadge('standalonewindows', 'pc', Monitor)}
						{@render platformBadge('android', 'quest', Smartphone)}
						{@render platformBadge('ios', 'ios', Apple)}
						{#if bundle.inCache}<Badge variant="outline" class="mt-1.5 mr-1.5 cursor-pointer" onclick={() => bundle.open()}>{bundle.size}&nbsp;{m.dialog_avatar_tags_cache()}</Badge>{/if}
						{#if avatar.styles?.primary || avatar.styles?.secondary}
							<Badge variant="outline" class="mt-1.5 mr-1.5">
								Styles
								{#if avatar.styles.primary}<span class="ml-1.5 border-l-[0.8px] border-solid pl-1.5 text-muted-foreground">{avatar.styles.primary}</span>{/if}
								{#if avatar.styles.secondary}<span class="ml-1.5 border-l-[0.8px] border-solid pl-1.5 text-muted-foreground">{avatar.styles.secondary}</span>{/if}
							</Badge>
						{/if}
						{#if isQuestFallback}<Badge variant="outline" class="mt-1.5 mr-1.5">{m.dialog_avatar_tags_fallback()}</Badge>{/if}
						{#if hasImpostor}<Badge variant="outline" class="mt-1.5 mr-1.5">{m.dialog_avatar_tags_impostor()}</Badge>{/if}
						{#if avatar.unityPackageUrl}<Badge variant="outline" class="mt-1.5 mr-1.5">{m.dialog_avatar_tags_future_proofing()}</Badge>{/if}
						{#each contentTags as tag (tag)}
							<Badge variant="outline" class="mt-1.5 mr-1.5">{contentLabel(tag)}</Badge>
						{/each}
					</div>
					<div class="col-start-2 row-span-2 row-start-1 grid flex-none grid-cols-2 items-center justify-end gap-2 self-start">
						<FavoriteButton type="avatar" targetId={avatarId} />
						<DropdownMenu.Root>
							<DropdownMenu.Trigger>
								{#snippet child({ props })}
									<Button {...props} variant={isBlocked ? 'destructive' : 'outline'} size="icon" class="rounded-lg" aria-label={m.nav_tooltip_manage()}><MoreHorizontal /></Button>
								{/snippet}
							</DropdownMenu.Trigger>
							<DropdownMenu.Content class="w-auto whitespace-nowrap">
								<DropdownMenu.Item disabled={refreshing} onSelect={refresh}><RefreshCw class="size-4" /> {m.dialog_avatar_actions_refresh()}</DropdownMenu.Item>
								<ShareMenu kind="avatar" url={vrchatUrl('avatar', avatarId)} name={avatar.name} id={avatarId} {actions} />
								<DropdownMenu.Separator />
								{#if isBlocked}
									<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_unblock(), () => api.delete(`${base}/block`), 'Avatar unblocked')}><CheckCircle class="size-4" /> {m.dialog_avatar_actions_unblock()}</DropdownMenu.Item>
								{:else}
									<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_block(), () => api.post(`${base}/block`), 'Avatar blocked')}><XCircle class="size-4" /> {m.dialog_avatar_actions_block()}</DropdownMenu.Item>
								{/if}
								{#if isQuestFallback}
									<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_select_fallback(), () => api.post(`${base}/fallback`), 'Fallback avatar changed')}><Check class="size-4" /> {m.dialog_avatar_actions_select_fallback()}</DropdownMenu.Item>
								{/if}
								{#if isOwner}
									<DropdownMenu.Separator />
									{#if avatar.releaseStatus === 'public'}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_make_private(), () => api.put(base, { releaseStatus: 'private' }), 'Avatar updated to private')}><UserIcon class="size-4" /> {m.dialog_avatar_actions_make_private()}</DropdownMenu.Item>
									{:else}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_make_public(), () => api.put(base, { releaseStatus: 'public' }), 'Avatar updated to public')}><UserIcon class="size-4" /> {m.dialog_avatar_actions_make_public()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Item
										onSelect={() =>
											(actions.prompting = {
												get title() { return m.prompt_rename_avatar_header(); },
												get description() { return m.prompt_rename_avatar_description(); },
												errorMessage: 'Valid name is required',
												value: avatar?.name ?? '',
												run: (name) => actions.act(() => api.put(base, { name }), 'Avatar renamed')
											})}><Pencil class="size-4" /> {m.dialog_avatar_actions_rename()}</DropdownMenu.Item
									>
									<DropdownMenu.Item
										onSelect={() =>
											(actions.prompting = {
												get title() { return m.prompt_change_avatar_description_header(); },
												get description() { return m.prompt_change_avatar_description_description(); },
												errorMessage: 'Valid description is required',
												value: avatar?.description ?? '',
												multiline: true,
												run: (description) => actions.act(() => api.put(base, { description }), 'Avatar description changed')
											})}><Pencil class="size-4" /> {m.dialog_avatar_actions_change_description()}</DropdownMenu.Item
									>
									<DropdownMenu.Item onSelect={() => (tagsOpen = true)}><Pencil class="size-4" /> {m.dialog_avatar_actions_change_content_tags()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => (stylesOpen = true)}><Pencil class="size-4" /> {m.dialog_avatar_actions_change_styles_author_tags()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => imageUpload?.pick()}><ImageIcon class="size-4" /> {m.dialog_avatar_actions_change_image()}</DropdownMenu.Item>
									{#if avatar.unityPackageUrl}
										<DropdownMenu.Item onSelect={() => openPackage(avatar?.unityPackageUrl)}><Download class="size-4" /> {m.dialog_avatar_actions_download_package()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Separator />
									{#if hasImpostor}
										<DropdownMenu.Item
											onSelect={() =>
												actions.confirm(
													m.dialog_avatar_actions_regenerate_impostor(),
													async () => {
														await api.delete(`${base}/impostor`);
														await api.post(`${base}/impostor`);
													},
													'Impostor queued for regeneration'
												)}><RefreshCw class="size-4" /> {m.dialog_avatar_actions_regenerate_impostor()}</DropdownMenu.Item
										>
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_delete_impostor(), () => api.delete(`${base}/impostor`), 'Impostor deleted')}><Trash2 class="size-4" /> {m.dialog_avatar_actions_delete_impostor()}</DropdownMenu.Item>
									{:else}
										<DropdownMenu.Item onSelect={() => actions.confirm(m.dialog_avatar_actions_create_impostor(), () => api.post(`${base}/impostor`), 'Impostor queued for creation')}><UserIcon class="size-4" /> {m.dialog_avatar_actions_create_impostor()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Item
										variant="destructive"
										onSelect={() =>
											(actions.confirming = {
												command: 'Delete',
												run: async () => {
													await actions.act(() => api.delete(base), 'Avatar deleted', false);
													if (!actions.error) userDialog.back();
												}
											})}><Trash2 class="size-4" /> {m.dialog_avatar_actions_delete()}</DropdownMenu.Item
									>
								{/if}
							</DropdownMenu.Content>
						</DropdownMenu.Root>
						{#if bundle.inCache}
							<Button variant="outline" size="icon" class="col-start-1 row-start-2 rounded-lg" title={m.dialog_avatar_actions_delete_cache_tooltip()} aria-label={m.dialog_avatar_actions_delete_cache_tooltip()} disabled={desktop.gameRunning && bundle.locked} onclick={() => bundle.delete(avatar, sdkUnityVersion(owner))}><Trash2 /></Button>
						{/if}
						<Button
							variant="outline"
							size="icon"
							class="rounded-lg"
							title={m.dialog_avatar_actions_select()}
							aria-label={m.dialog_avatar_actions_select()}
							disabled={isCurrent || actions.busy}
							onclick={() => actions.act(() => api.post(`${base}/select`), 'Avatar changed')}><Check /></Button
						>
					</div>
					{#if actions.error}
						<p class="col-span-2 mt-2 text-xs text-destructive">{actions.error}</p>
					{/if}
				</div>
			</div>

			<div class="mt-2.5 flex flex-col gap-2.5">
				<SectionCard title={m.dialog_avatar_info_header()}>
					<div class="flex flex-col gap-1.5">
						<InfoRow label={m.dialog_avatar_info_created_at()} value={timeAgo(avatar.created_at)} title={formatDate(avatar.created_at ?? undefined)} truncate />
						<InfoRow label={m.dialog_avatar_info_last_updated()} value={timeAgo(avatar.updated_at)} title={formatDate(avatar.updated_at ?? undefined)} truncate />
						<InfoRow label={m.dialog_avatar_info_tags()} value={authorTags || NONE} title={authorTags} truncate />
						<InfoRow label={m.dialog_avatar_info_version()} value={avatar.version ? String(avatar.version) : NONE} truncate />
						<InfoRow label={m.dialog_avatar_info_platform()} value={platformLines ? platformLines.split('\n')[0] : NONE} title={platformLines} truncate />
						<CopyIdRow label={m.dialog_avatar_info_id()} oncopy={() => actions.copy(avatarId)} />
					</div>
				</SectionCard>
				<SectionCard title={m.dialog_user_info_vrcx_info()}>
					<InfoRow label={m.dialog_avatar_info_time_spent()} value={timeSpentMs ? timeToText(timeSpentMs) : NONE} truncate />
				</SectionCard>
			</div>
		</div>

		<div class="flex min-h-0 min-w-0 flex-1 flex-col pl-4">
			<TabsPrimitive.Root bind:value={activeTab} class="flex min-h-0 w-full min-w-0 flex-1 flex-col">
				<div class={TAB_LIST_WRAP}>
					<TabsPrimitive.List class={TAB_LIST}>
						<TabsPrimitive.Trigger value="info" class={TAB_TRIGGER}>{m.dialog_avatar_info_header()}</TabsPrimitive.Trigger>
						<TabsPrimitive.Trigger value="json" class={TAB_TRIGGER}>{m.dialog_avatar_json_header()}</TabsPrimitive.Trigger>
					</TabsPrimitive.List>
				</div>
				<TabsPrimitive.Content value="info" class={TAB_CONTENT}>
					{#if avatar.description && avatar.name !== avatar.description}
						<div class={CARD}>
							<div class="{SECTION} border-b border-muted-foreground/20 pb-2">{m.dialog_world_info_description()}</div>
							<div class="flex items-start"><span class="flex-1 py-2 wrap-break-word whitespace-pre-wrap">{avatar.description}</span></div>
						</div>
					{/if}
					<AvatarGallery path={base} filesPath={`${accountPath(owner)}/gallery/files`} {isOwner} />
					{#if avatar.publishedListings?.length}
						<AvatarListings accountPath={accountPath(owner)} {owner} listings={avatar.publishedListings} productId={avatar.productId} />
					{/if}
					<MemoCard path={`/1/avatars/${encodeURIComponent(avatarId)}/memo`} />
				</TabsPrimitive.Content>
				<TabsPrimitive.Content value="json" class={TAB_CONTENT}>
					<JsonCard value={avatar} fileName={avatarId} extra={fileAnalysis} onrefresh={refresh} />
				</TabsPrimitive.Content>
			</TabsPrimitive.Root>
		</div>
	</div>

	<ActionDialogs bind:confirming={actions.confirming} bind:prompting={actions.prompting} busy={actions.busy} />
	{#if isOwner}
		<SetAvatarTagsDialog
			bind:open={tagsOpen}
			{owner}
			avatar={{ id: avatar.id, name: avatar.name, thumbnailImageUrl: avatar.thumbnailImageUrl, releaseStatus: avatar.releaseStatus, tags: avatar.tags ?? [] }}
			onsaved={refresh}
		/>
		<SetAvatarStylesDialog bind:open={stylesOpen} path={base} stylesPath={accountPath(owner, '/avatar-styles')} styles={avatar.styles} tags={avatar.tags ?? []} onsaved={refresh} />
		<ContentImageUpload bind:this={imageUpload} path={base} imageUrl={avatar.imageUrl ?? ''} title={m.dialog_change_content_image_avatar()} onuploaded={refresh} />
	{/if}

{/if}
