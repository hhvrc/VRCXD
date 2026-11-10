<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import Video from '@lucide/svelte/icons/video';
	import { tick, untrack } from 'svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import ToolRefreshButton from './tool-refresh-button.svelte';
	import { api } from '#lib/api/client.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { LoadState } from '#lib/helpers/load-state.svelte.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InfoPushDialog: help articles, quick menu banners and loading screens, with
	// the same field fallbacks, article rendering and links between articles.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	type Localized = string | { fallback?: string } | null | undefined;
	type LinkSource = { command?: string; parameters?: string[]; name?: string };
	type Item = {
		id: string;
		tags?: string[];
		isEnabled?: boolean;
		startDate?: string;
		endDate?: string;
		createdAt?: string;
		requireClientTags?: string[];
		data?: {
			name?: Localized;
			title?: string;
			contentList?: { name?: Localized };
			finalName?: string;
			worldTag?: string;
			version?: string;
			description?: Localized;
			subtitle?: string;
			tooltipDescription?: Localized;
			disclaimerText?: string;
			body?: string;
			search?: { searchQuery?: string };
			authorName?: string;
			imageUrl?: string;
			thumbnailImageUrl?: string;
			bannerImageUrl?: string;
			iconImageUrl?: string;
			onPressed?: LinkSource;
			domainList?: { domain: string }[];
			article?: {
				content?: { imageUrl?: string; videoUrl?: string; text?: string }[];
				moreInfoLinks?: LinkSource[];
				embeddedLinkData?: LinkSource[];
			};
		};
	};
	type Link = { command: string; target: string; name: string };

	const BANNER_CATEGORIES = ['quick-menu-banner', 'loading-screen-banners'];
	const CATEGORY_ORDER = ['help-article', ...BANNER_CATEGORIES];
	const CATEGORY_LABELS: Record<string, string> = {
		'help-article': 'Help Articles',
		'quick-menu-banner': 'Quick Menu Banners',
		'loading-screen-banners': 'Loading Screens'
	};
	const STATUS_LABELS: Record<string, string> = { disabled: 'Disabled', expired: 'Expired', scheduled: 'Scheduled' };

	let items = $state<Item[]>([]);
	const loader = new LoadState();
	let activeCategory = $state(CATEGORY_ORDER[0]);
	let expanded = $state<string[]>([]);
	let listRef = $state<HTMLElement | null>(null);

	const getCategory = (item: Item) => item.tags?.find((tag) => CATEGORY_ORDER.includes(tag)) ?? 'other';
	const categoryRank = (name: string) => {
		const index = CATEGORY_ORDER.indexOf(name);
		return index === -1 ? CATEGORY_ORDER.length : index;
	};
	const sortDate = (item: Item) => Date.parse(item.startDate ?? item.createdAt ?? '') || 0;
	const localize = (value: Localized) => (typeof value === 'string' ? value : (value?.fallback ?? ''));

	const itemsById = $derived(new Map(items.map((item) => [item.id, item])));
	const categories = $derived.by(() => {
		const counts = new Map<string, number>();
		for (const item of items) counts.set(getCategory(item), (counts.get(getCategory(item)) ?? 0) + 1);
		return [...counts].map(([name, count]) => ({ name, count })).sort((a, b) => categoryRank(a.name) - categoryRank(b.name) || b.count - a.count || a.name.localeCompare(b.name));
	});
	const filtered = $derived(items.filter((item) => getCategory(item) === activeCategory));

	function getName(item: Item): string {
		const d = item.data ?? {};
		return localize(d.name) || d.title || localize(d.contentList?.name) || d.finalName || d.worldTag || d.version || item.id;
	}

	function getDescription(item: Item): string {
		const d = item.data ?? {};
		return localize(d.description) || d.subtitle || localize(d.tooltipDescription) || d.disclaimerText || d.body || d.search?.searchQuery || d.authorName || '';
	}

	function getImageUrl(item: Item): string {
		const d = item.data ?? {};
		return d.imageUrl || d.thumbnailImageUrl || d.bannerImageUrl || d.iconImageUrl || '';
	}

	function getStatus(item: Item): string {
		if (!item.isEnabled) return 'disabled';
		const now = Date.now();
		if (item.endDate && Date.parse(item.endDate) < now) return 'expired';
		if (item.startDate && Date.parse(item.startDate) > now) return 'scheduled';
		return '';
	}

	const toPlainText = (text: string) =>
		text
			.replace(/<br\s*\/?>/gi, '\n')
			.replace(/<[^>]+>/g, '')
			.replace(/&nbsp;/g, ' ')
			.trim();

	function articleBlocks(item: Item) {
		const blocks: { type: 'image' | 'video' | 'heading' | 'text'; value: string }[] = [];
		for (const content of item.data?.article?.content ?? []) {
			if (content.imageUrl) blocks.push({ type: 'image', value: content.imageUrl });
			else if (content.videoUrl) blocks.push({ type: 'video', value: content.videoUrl });
			else if (content.text) {
				const value = toPlainText(content.text);
				if (value) blocks.push({ type: /^<size=[^>]+>\s*<b>/i.test(content.text.trim()) ? 'heading' : 'text', value });
			}
		}
		return blocks;
	}

	const toLink = (source: LinkSource): Link => ({ command: source.command ?? '', target: source.parameters?.[0] ?? '', name: source.name ?? '' });
	const getAction = (item: Item) => (item.data?.onPressed?.command ? toLink(item.data.onPressed) : null);

	function getLinks(item: Item): Link[] {
		const article = item.data?.article;
		const links = new Map<string, Link>();
		for (const source of [...(article?.moreInfoLinks ?? []), ...(article?.embeddedLinkData ?? [])]) {
			if (!source.command) continue;
			const link = toLink(source);
			const key = `${link.command}:${link.target}`;
			if (!links.has(key)) links.set(key, link);
		}
		return [...links.values()];
	}

	function isActionable(link: Link): boolean {
		if (link.command === 'OpenURL') return Boolean(link.target);
		return link.command === 'OpenHelpArticle' && itemsById.has(link.target);
	}

	function linkLabel(link: Link): string {
		if (link.name) return link.name;
		const article = link.command === 'OpenHelpArticle' ? itemsById.get(link.target) : undefined;
		if (article) return getName(article);
		return link.command === 'OpenURL' ? 'Open link' : link.command;
	}

	const openExternal = (url: string) => window.open(url, '_blank', 'noopener,noreferrer');

	async function runLink(link: Link) {
		if (link.command === 'OpenURL') openExternal(link.target);
		else if (link.command === 'OpenHelpArticle') {
			const article = itemsById.get(link.target);
			if (!article) return;
			activeCategory = getCategory(article);
			if (!expanded.includes(link.target)) expanded = [...expanded, link.target];
			await tick();
			listRef?.querySelector(`[data-info-push="${CSS.escape(link.target)}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
		}
	}

	const toggle = (id: string) => (expanded = expanded.includes(id) ? expanded.filter((e) => e !== id) : [...expanded, id]);

	async function load() {
		items = [];
		expanded = [];
		await loader.run(async () => {
			const list = await api.get<Item[]>(ownPath('/info-push'));
			items = [...(list ?? [])].sort((a, b) => sortDate(b) - sortDate(a));
			if (!categories.some((c) => c.name === activeCategory)) activeCategory = categories[0]?.name ?? CATEGORY_ORDER[0];
		});
	}

	$effect(() => {
		if (open) untrack(load);
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_info_push_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="flex items-center gap-2">
			<ToggleGroup.Root type="single" variant="outline" size="sm" value={activeCategory} onValueChange={(value) => value && (activeCategory = value)}>
				{#each categories as category (category.name)}
					<ToggleGroup.Item value={category.name}>{CATEGORY_LABELS[category.name] ?? category.name} ({category.count})</ToggleGroup.Item>
				{/each}
			</ToggleGroup.Root>
			<ToolRefreshButton label={m.dialog_note_export_refresh()} loading={loader.isLoading} onclick={load} />
		</div>
		{#if loader.error}<p class="text-xs text-destructive">{loader.error}</p>{/if}
		<div bind:this={listRef} class="max-h-[60vh] overflow-y-auto">
			{#if !filtered.length && !loader.isLoading}
				<p class="{EMPTY_STATE} text-xs">{m.common_no_data()}</p>
			{:else if BANNER_CATEGORIES.includes(activeCategory)}
				<div class="space-y-4">
					{#each filtered as item (item.id)}
						{@const action = getAction(item)}
						<div data-info-push={item.id}>
							{#if getImageUrl(item)}
								<ViewableImage src={getImageUrl(item)} alt={getName(item)} imageClass="w-full rounded" />
							{:else}
								<div class="text-sm font-medium">{getName(item)}</div>
							{/if}
							<div class="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
								<span title={item.endDate ? `Ends: ${formatDateFilter(item.endDate, 'long')}` : undefined}>{formatDateFilter(item.startDate ?? item.createdAt, 'long')}</span>
								{#if action && isActionable(action)}
									<Button size="sm" variant="outline" class="ml-auto" onclick={() => runLink(action)}>{linkLabel(action)}</Button>
								{/if}
							</div>
						</div>
					{/each}
				</div>
			{:else}
				{#each filtered as item (item.id)}
					{@const action = getAction(item)}
					{@const isOpen = expanded.includes(item.id)}
					<div data-info-push={item.id} class="border-b py-2 last:border-b-0">
						<div class="flex cursor-pointer gap-3" role="button" tabindex="0" onclick={() => toggle(item.id)} onkeydown={(e) => e.key === 'Enter' && toggle(item.id)}>
							{#if getImageUrl(item)}
								<ViewableImage src={getImageUrl(item)} class="flex-none" imageClass="h-14 w-24 rounded object-cover" />
							{/if}
							<div class="min-w-0 flex-1 text-sm">
								<div class="flex items-center gap-2">
									<ChevronRight class="size-4 flex-none text-muted-foreground transition-transform {isOpen ? 'rotate-90' : ''}" />
									<span class="truncate font-medium">{getName(item)}</span>
									{#if getStatus(item)}<Badge variant="destructive" class="flex-none">{STATUS_LABELS[getStatus(item)]}</Badge>{/if}
									<span class="ml-auto flex-none text-xs text-muted-foreground">{formatDateFilter(item.startDate ?? item.createdAt, 'long')}</span>
								</div>
								{#if getDescription(item)}
									<div class="text-xs text-muted-foreground {isOpen ? '' : 'line-clamp-2'}">{getDescription(item)}</div>
								{/if}
								<div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
									{#if item.endDate}<span>Ends: {formatDateFilter(item.endDate, 'long')}</span>{/if}
									{#each item.requireClientTags ?? [] as tag (tag)}
										<Badge variant="outline" class="text-xs">{tag}</Badge>
									{/each}
									{#if action}
										{#if isActionable(action)}
											<button
												type="button"
												class="cursor-pointer text-primary hover:underline"
												onclick={(e) => {
													e.stopPropagation();
													runLink(action);
												}}>{linkLabel(action)}</button
											>
										{:else}
											<span>{action.command}</span>
										{/if}
									{/if}
								</div>
							</div>
						</div>
						{#if isOpen}
							<div class="mt-3 space-y-3 pl-7 text-sm">
								{#if articleBlocks(item).length}
									<div class="space-y-2">
										{#each articleBlocks(item) as block, index (index)}
											{#if block.type === 'image'}
												<ViewableImage src={block.value} imageClass="w-full max-w-lg rounded" />
											{:else if block.type === 'video'}
												<Button size="sm" variant="outline" onclick={() => openExternal(block.value)}><Video class="size-4" />{m.dialog_info_push_open_video()}</Button>
											{:else}
												<p class="whitespace-pre-line {block.type === 'heading' ? 'text-base font-semibold' : ''}">{block.value}</p>
											{/if}
										{/each}
									</div>
								{/if}
								{#if item.data?.domainList?.length}
									<div class="flex flex-wrap items-center gap-1">
										<span class="text-xs text-muted-foreground">Domains:</span>
										{#each item.data.domainList as entry (entry.domain)}
											<Badge variant="outline">{entry.domain}</Badge>
										{/each}
									</div>
								{/if}
								{#if getLinks(item).length}
									<div class="flex flex-wrap gap-2">
										{#each getLinks(item) as link (`${link.command}:${link.target}`)}
											<Button size="sm" variant="outline" disabled={!isActionable(link)} onclick={() => runLink(link)}>{linkLabel(link)}</Button>
										{/each}
									</div>
								{/if}
							</div>
						{/if}
					</div>
				{/each}
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>

