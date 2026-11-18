<script lang="ts">
	import { onMount } from 'svelte';
	import { m } from '#lib/paraglide/messages.js';
	import SettingsGroup from '#lib/components/settings/settings-group.svelte';
	import SettingsItem from '#lib/components/settings/settings-item.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import { ApiError } from '#lib/api/client.js';
	import {
		canRun,
		formatBytes,
		formatElapsed,
		isRunning,
		legacyImportApi,
		reportRows,
		reportTotal,
		uploadInParts,
		type LegacyImportStatus
	} from '#lib/settings/legacy-import.js';
	import { legacyImportTopic } from '#lib/live/topics/legacy-import.svelte.js';

	// Importing an original VRCX database, which the original has no counterpart for: it is that
	// database. The work is the server's (POST /1/legacy-import and on); this page uploads the file
	// and shows the server's status from the "legacy_import" topic, which follows the upload, each
	// step of a check or an import, and its end, also when another tab started it.

	let status = $state<LegacyImportStatus | undefined>();
	// In the browser only: the page is prerendered, and the topic opens the live socket.
	onMount(() => {
		const topic = legacyImportTopic();
		const stopListening = topic.listen((value) => (status = value));
		return () => {
			stopListening();
			topic.release();
		};
	});

	let file = $state<File | undefined>();
	let uploading = $state<{ received: number; size: number; abort: AbortController } | undefined>();
	let error = $state('');
	let busy = $state(false);

	const running = $derived(!!status && isRunning(status.state));
	const rows = $derived(status?.report ? reportRows(status.report) : []);
	const percent = (received: number, size: number) => (size > 0 ? Math.floor((received / size) * 100) : 0);

	async function upload() {
		if (!file) return;
		error = '';
		const abort = new AbortController();
		uploading = { received: 0, size: file.size, abort };
		try {
			await uploadInParts(file, legacyImportApi, {
				signal: abort.signal,
				onProgress: (received, size) => uploading && (uploading = { ...uploading, received, size })
			});
		} catch (err) {
			if (!abort.signal.aborted) error = ApiError.from(err).message;
		} finally {
			uploading = undefined;
		}
	}

	async function act(run: () => Promise<unknown>) {
		busy = true;
		error = '';
		try {
			await run();
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			busy = false;
		}
	}

	async function cancelUpload() {
		uploading?.abort.abort();
		await act(() => legacyImportApi.discard());
	}
</script>

<SettingsGroup title={m.view_settings_advanced_legacy_import_header()}>
	{#snippet description()}{m.view_settings_advanced_legacy_import_description()}{/snippet}
	{#if !status}
		<p class="text-sm text-muted-foreground">{m.view_settings_advanced_legacy_import_waiting()}</p>
	{:else if uploading}
		<div class="flex flex-col gap-2 py-1">
			<span class="text-sm">
				{m.view_settings_advanced_legacy_import_uploading({ received: formatBytes(uploading.received), size: formatBytes(uploading.size), percent: percent(uploading.received, uploading.size) })}
			</span>
			<Progress value={uploading.received} max={uploading.size || 1} />
			<div><Button size="sm" variant="outline" onclick={cancelUpload}>{m.view_settings_advanced_legacy_import_cancel()}</Button></div>
		</div>
	{:else if running}
		<div class="flex flex-col gap-2 py-1">
			<span class="text-sm">
				{#if status.progress}
					{(status.state === 'checking' ? m.view_settings_advanced_legacy_import_checking : m.view_settings_advanced_legacy_import_importing)(status.progress)}
				{:else}
					{m.view_settings_advanced_legacy_import_starting()}
				{/if}
			</span>
			<Progress value={status.progress?.step ?? 0} max={status.progress?.steps ?? 1} />
		</div>
	{:else}
		{#if canRun(status)}
			<SettingsItem label={m.view_settings_advanced_legacy_import_uploaded({ size: formatBytes(status.size) })}>
				<div class="flex gap-2">
					<Button size="sm" variant="outline" disabled={busy} onclick={() => act(() => legacyImportApi.dryRun(status!.uploadId!))}>{m.view_settings_advanced_legacy_import_check()}</Button>
					<Button size="sm" disabled={busy || status.state !== 'checked'} onclick={() => act(() => legacyImportApi.import(status!.uploadId!))}>{m.view_settings_advanced_legacy_import_import()}</Button>
					<Button size="sm" variant="ghost" disabled={busy} onclick={() => act(() => legacyImportApi.discard())}>{m.view_settings_advanced_legacy_import_discard()}</Button>
				</div>
			</SettingsItem>
		{:else}
			<SettingsItem label={m.view_settings_advanced_legacy_import_file()}>
				<div class="flex items-center gap-2">
					<input
						type="file"
						accept=".sqlite3,.sqlite,.db"
						class="max-w-64 text-sm file:mr-2 file:rounded-md file:border file:border-input file:bg-background file:px-2 file:py-1 file:text-sm"
						onchange={(event) => (file = event.currentTarget.files?.[0])}
					/>
					<Button size="sm" variant="outline" disabled={!file || busy} onclick={upload}>{m.view_settings_advanced_legacy_import_upload()}</Button>
				</div>
			</SettingsItem>
		{/if}
		{#if status.state === 'failed' && status.error}
			<p class="text-sm text-destructive">{m.view_settings_advanced_legacy_import_failed({ error: status.error })}</p>
		{/if}
		{#if status.report && (status.state === 'checked' || status.state === 'imported')}
			<p class="text-sm">
				{#if status.report.dryRun}
					{m.view_settings_advanced_legacy_import_checked({ count: reportTotal(status.report).toLocaleString() })}
				{:else}
					{m.view_settings_advanced_legacy_import_imported({ count: reportTotal(status.report).toLocaleString(), elapsed: formatElapsed(status.report.elapsedSeconds) })}
				{/if}
			</p>
			{#if status.state === 'imported'}
				<!-- The import writes past the live topics, so a reload is what brings the new rows in. -->
				<div><Button size="sm" variant="outline" onclick={() => location.reload()}>{m.view_settings_advanced_legacy_import_reload()}</Button></div>
			{/if}
			<Table.Root class="text-xs">
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.view_settings_advanced_legacy_import_table_source()}</Table.Head>
						<Table.Head>{m.view_settings_advanced_legacy_import_table_target()}</Table.Head>
						<Table.Head class="text-right">{m.view_settings_advanced_legacy_import_table_read()}</Table.Head>
						<Table.Head class="text-right">{status.report.dryRun ? m.view_settings_advanced_legacy_import_table_would_add() : m.view_settings_advanced_legacy_import_table_added()}</Table.Head>
						{#if !status.report.dryRun}<Table.Head class="text-right">{m.view_settings_advanced_legacy_import_table_already()}</Table.Head>{/if}
						<Table.Head class="text-right">{m.view_settings_advanced_legacy_import_table_skipped()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each rows as row (`${row.source}\u0000${row.target}`)}
						<Table.Row>
							<Table.Cell>{row.source}</Table.Cell>
							<Table.Cell>{row.target}</Table.Cell>
							<Table.Cell class="text-right">{row.read.toLocaleString()}</Table.Cell>
							<Table.Cell class="text-right">{row.count.toLocaleString()}</Table.Cell>
							{#if !status.report.dryRun}<Table.Cell class="text-right">{(row.already ?? 0).toLocaleString()}</Table.Cell>{/if}
							<Table.Cell class="text-right">{row.skipped ? row.skipped.toLocaleString() : ''}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
			{#each status.report.notes as note (note)}
				<p class="text-xs text-muted-foreground">{note}</p>
			{/each}
		{/if}
	{/if}
	{#if error}
		<p class="text-sm text-destructive">{error}</p>
	{/if}
</SettingsGroup>
