<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { imageSrc } from '#lib/api/links.js';
	import Apple from '@lucide/svelte/icons/apple';
	import Check from '@lucide/svelte/icons/check';
	import ExternalLink from '@lucide/svelte/icons/external-link';
	import Eye from '@lucide/svelte/icons/eye';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Monitor from '@lucide/svelte/icons/monitor';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import Tag from '@lucide/svelte/icons/tag';
	import UserIcon from '@lucide/svelte/icons/user';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import type { OwnAvatar } from '#lib/api/types.js';
	import { formatDateFilter, timeToText } from '#lib/helpers/format.js';
	import { resolveTagColor } from '#lib/content/tag-colors.js';

	// The original's MyAvatarCard.vue: thumbnail with platform dots, name, tag badges, the
	// same context menu as the table rows, and a hover card with the avatar's details.

	export type AvatarAction =
		| 'details'
		| 'wear'
		| 'manageTags'
		| 'makePrivate'
		| 'makePublic'
		| 'rename'
		| 'changeDescription'
		| 'changeTags'
		| 'changeStyles'
		| 'changeImage'
		| 'createImpostor';

	let {
		item,
		cardScale = 0.6,
		onclick,
		onaction
	}: { item: OwnAvatar; cardScale?: number; onclick: () => void; onaction: (action: AvatarAction, item: OwnAvatar) => void } = $props();

	let imageLoadError = $state(false);
	let hoverOpen = $state(false);
	let contextMenuOpen = $state(false);
	const avatar = $derived(item.avatar);
	const isPC = $derived(avatar.platforms.includes('standalonewindows'));
	const isQuest = $derived(avatar.platforms.includes('android'));
	const isIos = $derived(avatar.platforms.includes('ios'));
</script>

<HoverCard.Root
	bind:open={() => hoverOpen, (open) => {
		if (!contextMenuOpen) hoverOpen = open;
	}}
	openDelay={700}
	closeDelay={100}
>
	<HoverCard.Trigger>
		{#snippet child({ props })}
			<div {...props}>
				<ContextMenu.Root
					onOpenChange={(open) => {
						contextMenuOpen = open;
						if (open) hoverOpen = false;
					}}
				>
					<ContextMenu.Trigger>
						{#snippet child({ props })}
							<div {...props} class="avatar-card-wrapper rounded-lg" role="button" tabindex="0" {onclick} onkeydown={(e) => e.key === 'Enter' && onclick()}>
								<Card.Root
									class="avatar-card relative flex cursor-pointer flex-col gap-0 overflow-hidden rounded-lg p-0 hover:bg-accent hover:shadow-sm {item.isCurrent ? 'ring-2 ring-primary' : 'border border-border/50'}"
								>
									<div class="relative aspect-5/2 w-full overflow-hidden bg-muted">
										{#if avatar.thumbnailImageUrl && !imageLoadError}
											<img src={imageSrc(avatar.thumbnailImageUrl)} alt={avatar.name} loading="lazy" decoding="async" class="dim-thumbnail block h-full w-full object-cover" onerror={() => (imageLoadError = true)} />
										{:else}
											<div class="grid h-full w-full place-items-center"><ImageIcon class="size-6 text-muted-foreground" /></div>
										{/if}
										{#if isQuest || isIos}
											<div class="absolute top-1 right-1 flex -space-x-1">
												{#if isPC}<span class="size-2.5 rounded-full border bg-platform-pc opacity-70"></span>{/if}
												{#if isQuest}<span class="size-2.5 rounded-full border bg-platform-quest opacity-70"></span>{/if}
												{#if isIos}<span class="size-2.5 rounded-full border bg-platform-ios opacity-70"></span>{/if}
											</div>
										{/if}
									</div>
									<div class="flex min-h-0 flex-col gap-0.5" style="padding: {Math.round(6 * cardScale)}px {Math.round(8 * cardScale)}px">
										<span class="line-clamp-2 block min-h-[2.75em] overflow-hidden leading-snug" style="font-size: {Math.max(9, Math.round(18 * cardScale))}px">{avatar.name}</span>
										{#if item.tags.length}
											<div class="flex flex-nowrap gap-0.5 overflow-hidden" style="max-height: {Math.max(14, Math.round(22 * cardScale))}px">
												{#each item.tags as entry (entry.tag)}
													{@const color = resolveTagColor(entry.tag, entry.color ?? '')}
													<Badge variant="outline" class="shrink-0 rounded-sm px-1 py-0 leading-tight" style="font-size: {Math.max(8, Math.round(14 * cardScale))}px; border-color: {color.bg}; color: {color.text}">{entry.tag}</Badge>
												{/each}
											</div>
										{/if}
									</div>
								</Card.Root>
							</div>
						{/snippet}
					</ContextMenu.Trigger>
					<ContextMenu.Content>
						<ContextMenu.Item onSelect={() => onaction('details', item)}><Eye class="size-4" />{m.dialog_avatar_actions_view_details()}</ContextMenu.Item>
						<ContextMenu.Item disabled={item.isCurrent} onSelect={() => onaction('wear', item)}><Check class="size-4" />{m.dialog_avatar_actions_select()}</ContextMenu.Item>
						<ContextMenu.Separator />
						<ContextMenu.Item onSelect={() => onaction('manageTags', item)}><Tag class="size-4" />{m.dialog_avatar_actions_manage_tags()}</ContextMenu.Item>
						<ContextMenu.Separator />
						<ContextMenu.Item onSelect={() => onaction(avatar.releaseStatus === 'public' ? 'makePrivate' : 'makePublic', item)}>
							<UserIcon class="size-4" />{avatar.releaseStatus === 'public' ? 'Make Private' : 'Make Public'}
						</ContextMenu.Item>
						<ContextMenu.Item onSelect={() => onaction('rename', item)}><Pencil class="size-4" />{m.dialog_avatar_actions_rename()}</ContextMenu.Item>
						<ContextMenu.Item onSelect={() => onaction('changeDescription', item)}><Pencil class="size-4" />{m.dialog_avatar_actions_change_description()}</ContextMenu.Item>
						<ContextMenu.Item disabled onSelect={() => onaction('changeTags', item)}><Pencil class="size-4" />{m.dialog_avatar_actions_change_content_tags()}</ContextMenu.Item>
						<ContextMenu.Item disabled onSelect={() => onaction('changeStyles', item)}><Pencil class="size-4" />{m.dialog_avatar_actions_change_styles_author_tags()}</ContextMenu.Item>
						<ContextMenu.Item disabled onSelect={() => onaction('changeImage', item)}><ImageIcon class="size-4" />{m.dialog_avatar_actions_change_image()}</ContextMenu.Item>
						<ContextMenu.Item onSelect={() => onaction('createImpostor', item)}><RefreshCw class="size-4" />{m.dialog_avatar_actions_create_impostor()}</ContextMenu.Item>
					</ContextMenu.Content>
				</ContextMenu.Root>
			</div>
		{/snippet}
	</HoverCard.Trigger>
	<HoverCard.Content class="w-80 p-3 text-sm" side="right" sideOffset={8} align="start">
		<div class="flex flex-col gap-2">
			<div class="flex items-start gap-1">
				<div class="min-w-0 flex-1 truncate text-base font-medium">{avatar.name}</div>
				<Button size="icon-sm" variant="ghost" class="size-6 shrink-0 rounded-full" aria-label={m.dialog_avatar_actions_view_details()} onclick={() => onaction('details', item)}><ExternalLink class="size-3" /></Button>
			</div>
			{#if item.tags.length}
				<div class="flex flex-wrap gap-1">
					{#each item.tags as entry (entry.tag)}
						{@const color = resolveTagColor(entry.tag, entry.color ?? '')}
						<Badge variant="outline" class="px-1.5 py-0 text-xs" style="border-color: {color.bg}; color: {color.text}">{entry.tag}</Badge>
					{/each}
				</div>
			{/if}
			<Separator />
			<div class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
				<span class="text-muted-foreground">{m.dialog_avatar_info_visibility()}</span>
				<span><Badge variant="outline" class="px-1.5 py-0 text-xs">{avatar.releaseStatus === 'public' ? 'Public' : 'Private'}</Badge></span>
				<span class="text-muted-foreground">{m.dialog_avatar_info_platform()}</span>
				<div class="flex items-center gap-1">
					{#if isPC}<Badge class="border-platform-pc! text-platform-pc" variant="outline"><Monitor class="h-3 w-3" /></Badge>{/if}
					{#if isQuest}<Badge class="border-platform-quest! text-platform-quest" variant="outline"><Smartphone class="h-3 w-3" /></Badge>{/if}
					{#if isIos}<Badge class="border-platform-ios text-platform-ios" variant="outline"><Apple class="h-3 w-3" /></Badge>{/if}
				</div>
				{#if avatar.performancePc}<span class="text-muted-foreground">{m.dialog_avatar_info_pc_performance()}</span><span>{avatar.performancePc}</span>{/if}
				{#if avatar.performanceAndroid}<span class="text-muted-foreground">{m.dialog_avatar_info_android_performance()}</span><span>{avatar.performanceAndroid}</span>{/if}
				{#if avatar.performanceIos}<span class="text-muted-foreground">{m.dialog_avatar_info_ios_performance()}</span><span>{avatar.performanceIos}</span>{/if}
				<span class="text-muted-foreground">{m.dialog_avatar_info_version()}</span>
				<span>{avatar.version ?? '-'}</span>
				{#if item.timeSpentMs}<span class="text-muted-foreground">{m.dialog_avatar_info_time_spent()}</span><span>{timeToText(item.timeSpentMs)}</span>{/if}
				<span class="text-muted-foreground">{m.dialog_avatar_info_last_updated()}</span>
				<span>{formatDateFilter(avatar.updatedAt, 'long')}</span>
				<span class="text-muted-foreground">{m.dialog_avatar_info_created_at()}</span>
				<span>{formatDateFilter(avatar.createdAt, 'long')}</span>
			</div>
		</div>
	</HoverCard.Content>
</HoverCard.Root>

