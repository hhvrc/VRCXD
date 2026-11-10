<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';

	// The original's OpenSourceSoftwareNoticeDialog: the third-party packages the client bundles and
	// the server ships with, from the manifest scripts/generate-third-party-licenses.js writes at
	// build time (so a dev server has none), searchable, with each package's license text.
	type Entry = {
		id: string;
		name: string;
		version?: string;
		license: string;
		sourceLabel: string;
		projects?: string[];
		projectUrl?: string;
		licenseUrl?: string;
		noticeText?: string;
		needsReview?: boolean;
	};

	let { open = $bindable(false) }: { open?: boolean } = $props();

	let loading = $state(false);
	let loadError = $state(false);
	let entries = $state<Entry[]>([]);
	let noticePath = $state('licenses/THIRD_PARTY_NOTICES.txt');
	let query = $state('');
	let selectedId = $state('');
	let loaded = false;

	const filtered = $derived.by(() => {
		const q = query.trim().toLowerCase();
		if (!q) return entries;
		return entries.filter((e) => [e.name, e.version, e.license, e.sourceLabel, ...(e.projects ?? [])].filter(Boolean).join(' ').toLowerCase().includes(q));
	});
	const selected = $derived(filtered.find((e) => e.id === selectedId) ?? filtered[0] ?? null);

	$effect(() => {
		if (open && !loaded) load();
	});

	async function load() {
		loading = true;
		loadError = false;
		try {
			const response = await fetch(new URL('/licenses/third-party-licenses.json', window.location.href), { cache: 'no-store' });
			if (!response.ok) throw new Error(`Failed to load third-party license manifest: ${response.status}`);
			const manifest = await response.json();
			entries = Array.isArray(manifest.entries) ? manifest.entries : [];
			if (typeof manifest.noticePath === 'string' && manifest.noticePath) noticePath = manifest.noticePath;
			selectedId = entries[0]?.id ?? '';
			loaded = true;
		} catch (err) {
			console.error(err);
			loadError = true;
		} finally {
			loading = false;
		}
	}

	const licenseLabel = (license: string) => (license.startsWith('Public Domain') ? 'Public Domain' : license);
	const openLink = (url: string) => window.open(url, '_blank', 'noopener,noreferrer');
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-5xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_open_source_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="space-y-4">
			<Card.Root>
				<Card.Header>
					<Card.Title class="text-base">{m.dialog_open_source_description()}</Card.Title>
					<Card.Description>
						{m.dialog_open_source_notice_location_prefix()}
						<code class="rounded bg-muted px-1.5 py-0.5 text-xs">{noticePath}</code>
					</Card.Description>
				</Card.Header>
				<Card.Content>
					<Input bind:value={query} placeholder={m.dialog_open_source_search_placeholder()} />
				</Card.Content>
			</Card.Root>

			{#if loading}
				<div class="rounded-md border border-dashed p-6 text-sm text-muted-foreground">{m.dialog_open_source_loading()}</div>
			{:else if loadError}
				<div class="rounded-md border border-dashed p-6 text-sm text-muted-foreground">{m.dialog_open_source_unavailable()}</div>
			{:else}
				<div class="grid gap-4 md:grid-cols-[minmax(0,18rem)_minmax(0,1fr)]">
					<div class="h-[28rem] space-y-3 overflow-y-auto pr-1">
						{#each filtered as entry (entry.id)}
							<button type="button" class="block w-full text-left" onclick={() => (selectedId = entry.id)}>
								<Card.Root class={['cursor-pointer transition-colors', entry.id === selected?.id ? 'border-primary bg-accent' : 'hover:bg-accent/40']}>
									<Card.Header class="gap-2">
										<Card.Title class="truncate text-sm" title={entry.name}>{entry.name}</Card.Title>
										<Card.Description>{entry.version || 'No version metadata'}</Card.Description>
									</Card.Header>
									<Card.Content class="flex min-w-0 flex-wrap items-center gap-2">
										<Badge variant="secondary" class="max-w-full min-w-0 shrink truncate" title={entry.license}>{licenseLabel(entry.license)}</Badge>
										<span class="min-w-0 text-xs text-muted-foreground">{entry.sourceLabel}</span>
									</Card.Content>
								</Card.Root>
							</button>
						{:else}
							<div class="rounded-md border border-dashed p-6 text-sm text-muted-foreground">{m.dialog_open_source_no_results()}</div>
						{/each}
					</div>
					{#if selected}
						<Card.Root class="min-w-0">
							<Card.Header class="gap-3">
								<div class="flex flex-wrap items-start justify-between gap-3">
									<div class="min-w-0">
										<Card.Title class="truncate text-base">{selected.name}</Card.Title>
										<Card.Description>{selected.version || 'No version metadata'}</Card.Description>
									</div>
									<div class="flex flex-wrap gap-2">
										{#if selected.projectUrl}<Button size="sm" variant="outline" onclick={() => openLink(selected.projectUrl!)}>{m.dialog_open_source_open_project()}</Button>{/if}
										{#if selected.licenseUrl}<Button size="sm" variant="outline" onclick={() => openLink(selected.licenseUrl!)}>{m.dialog_open_source_open_license_url()}</Button>{/if}
									</div>
								</div>
								<div class="flex flex-wrap items-center gap-2">
									<Badge variant="secondary" title={selected.license}>{licenseLabel(selected.license)}</Badge>
									{#if selected.needsReview}<Badge variant="destructive">{m.dialog_open_source_review_required()}</Badge>{/if}
									<span class="text-xs text-muted-foreground">{selected.sourceLabel}</span>
									{#if selected.projects?.length}<span class="text-xs text-muted-foreground">Used by: {selected.projects.join(', ')}</span>{/if}
								</div>
							</Card.Header>
							<Card.Content>
								<pre class="h-[22rem] overflow-auto rounded-md bg-muted p-3 text-xs whitespace-pre-wrap">{selected.noticeText || 'No local license text was generated for this entry. Open the full notice file and review this component before release.'}</pre>
							</Card.Content>
						</Card.Root>
					{:else}
						<Card.Root>
							<Card.Content class="p-6 text-sm text-muted-foreground">{m.dialog_open_source_select_package()}</Card.Content>
						</Card.Root>
					{/if}
				</div>
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>
