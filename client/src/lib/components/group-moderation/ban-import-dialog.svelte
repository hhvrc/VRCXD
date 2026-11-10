<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { api } from '#lib/api/client.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupMemberModerationBanImportDialog: user ids from a CSV's userId column or
	// anywhere in the text, banned one a second, with a cancel.
	let { open = $bindable(false), groupId, onimported }: { open?: boolean; groupId: string; onimported: () => void } = $props();

	let csvInput = $state('');
	let importing = $state(false);
	let cancelled = false;
	let progressCurrent = $state(0);
	let progressTotal = $state(0);
	let errors = $state('');
	let resultMessage = $state('');

	const progressPercent = $derived(progressTotal ? Math.min(100, Math.round((progressCurrent / progressTotal) * 100)) : 0);

	function extractUserIds(input: string): string[] {
		const lines = input
			.split('\n')
			.map((l) => l.trim())
			.filter((l) => l.length > 0);
		if (lines.length === 0) return [];
		const headers = lines[0].split(',').map((h) => h.trim());
		const column = headers.findIndex((h) => h.toLowerCase() === 'userid');
		if (column !== -1 && lines.length > 1) {
			const ids = new Set<string>();
			for (const line of lines.slice(1)) {
				const cols = line.split(',');
				if (cols.length > column) {
					const value = cols[column].trim().replace(/^"|"$/g, '');
					if (value.startsWith('usr_')) ids.add(value);
				}
			}
			return [...ids];
		}
		return [...new Set(input.match(/usr_[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}/g) ?? [])];
	}

	async function parseAndImport() {
		const userIds = extractUserIds(csvInput);
		if (userIds.length === 0) {
			errors = 'No valid user IDs found (usr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)';
			return;
		}
		importing = true;
		cancelled = false;
		progressCurrent = 0;
		progressTotal = userIds.length;
		errors = '';
		resultMessage = '';
		let successCount = 0;
		for (let i = 0; i < userIds.length; i++) {
			if (cancelled) break;
			const userId = userIds[i];
			progressCurrent = i + 1;
			try {
				await api.post(ownPath(`/groups/${encodeURIComponent(groupId)}/bans/${encodeURIComponent(userId)}`));
				successCount++;
			} catch (err) {
				errors += `${userId}: ${err}\n`;
			}
			// Rate limit delay between requests.
			if (i < userIds.length - 1 && !cancelled) await new Promise((resolve) => setTimeout(resolve, 1000));
		}
		resultMessage = cancelled ? `Cancelled. Banned ${successCount}/${progressCurrent} users.` : `Done. Banned ${successCount}/${userIds.length} users.`;
		importing = false;
		progressCurrent = 0;
		progressTotal = 0;
		if (successCount > 0) onimported();
	}

	function close() {
		if (importing) cancelled = true;
		csvInput = '';
		errors = '';
		resultMessage = '';
		open = false;
	}
</script>

<Dialog.Root bind:open={() => open, (value) => { if (!value) close(); }}>
	<Dialog.Content class="bg-background sm:max-w-162.5">
		<Dialog.Header><Dialog.Title>{m.dialog_group_member_moderation_import_bans()}</Dialog.Title></Dialog.Header>
		<div class="mb-2 text-xs text-muted-foreground">
			<p>{m.dialog_group_member_moderation_import_bans_description()}</p>
		</div>
		<div role="alert" class="mb-2 flex items-start gap-2 rounded-lg border border-amber-500/50 px-4 py-3 text-sm text-amber-600 dark:text-amber-400">
			<TriangleAlert class="mt-0.5 size-4 shrink-0" />
			<span>{m.dialog_group_member_moderation_import_bans_warning()}</span>
		</div>
		<Textarea
			bind:value={csvInput}
			rows={10}
			disabled={importing}
			class="mb-2 resize-none"
			placeholder={'Paste CSV or user IDs here (one per line)\nusr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'}
		/>
		<div class="flex items-center gap-2">
			<Button size="sm" disabled={!csvInput.trim() || importing} onclick={parseAndImport}>Import &amp; Ban</Button>
			{#if importing}
				<Button size="sm" variant="destructive" onclick={() => (cancelled = true)}><Spinner /> {m.dialog_alertdialog_cancel()}</Button>
			{/if}
		</div>
		{#if importing}
			<div class="mt-2">
				<div class="mb-1 flex justify-between text-sm">
					<span>{m.dialog_note_export_progress()}</span>
					<strong>{progressCurrent} / {progressTotal}</strong>
				</div>
				<Progress value={progressPercent} class="h-3" />
			</div>
		{/if}
		{#if errors}
			<div>
				<Button size="sm" variant="secondary" onclick={() => (errors = '')}>{m.dialog_note_export_clear_errors()}</Button>
				<pre class="mt-1.5 text-xs whitespace-pre-wrap">{errors}</pre>
			</div>
		{/if}
		{#if resultMessage}
			<span class="text-sm">{resultMessage}</span>
		{/if}
	</Dialog.Content>
</Dialog.Root>
