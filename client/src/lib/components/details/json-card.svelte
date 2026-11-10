<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Download from '@lucide/svelte/icons/download';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import { Button } from '#lib/components/ui/button/index.js';
	import { downloadJson } from '#lib/groups/group-dialog.svelte.js';
	import JsonTree from './json-tree.svelte';

	// The original's DialogJsonTab: Refresh, Download as <id>.json, and the data as a folding tree,
	// with the bundle analysis under it when there is one.
	let {
		value,
		fileName,
		download,
		extra,
		onrefresh
	}: { value: unknown; fileName: string; download?: unknown; extra?: unknown; onrefresh?: () => void } = $props();
</script>

<div class="mt-2 flex min-h-0 flex-col rounded-xl bg-(--profile-card) p-2">
	<div class="shrink-0">
		{#if onrefresh}
			<Button class="mr-2 rounded-full" size="icon-sm" variant="ghost" aria-label={m.dialog_note_export_refresh()} onclick={onrefresh}><RefreshCw /></Button>
		{/if}
		<Button class="rounded-full" size="icon-sm" variant="ghost" aria-label={m.dialog_vrcx_updater_download()} onclick={() => downloadJson(fileName, download ?? value)}><Download /></Button>
	</div>
	<div class="min-h-0 flex-1 overflow-auto">
		<JsonTree {value} />
		{#if extra && typeof extra === 'object' && Object.keys(extra).length}
			<br />
			<JsonTree value={extra} />
		{/if}
	</div>
</div>
