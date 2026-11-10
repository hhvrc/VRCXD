<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { imageSrc, userHref } from '#lib/api/links.js';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { untrack } from 'svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import type { FriendExportRow } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's NoteExportDialog: friends whose memo differs from their VRChat note, editable
	// before sending, one note every five seconds because VRChat's rate limit is strict.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	type Row = FriendExportRow & { edited: string };
	const SEND_DELAY_MS = 5000;

	let rows = $state<Row[]>([]);
	let progress = $state(0);
	let progressTotal = $state(0);
	let loading = $state(false);
	let errors = $state('');

	$effect(() => {
		if (open) {
			untrack(() => {
				rows = [];
				progress = 0;
				progressTotal = 0;
				loading = false;
				errors = '';
			});
		} else {
			loading = false;
		}
	});

	async function refresh() {
		try {
			const friends = await api.get<FriendExportRow[]>(ownPath('/tools/friends'));
			rows = friends.flatMap((friend) => {
				const memo = (friend.memo ?? '').replace(/[\r\n]/g, ' ');
				return memo && (friend.note ?? '') !== memo.slice(0, 256) ? [{ ...friend, edited: memo }] : [];
			});
		} catch (err) {
			errors = ApiError.from(err).message;
		}
	}

	async function exportNotes() {
		loading = true;
		const queue = [...rows];
		progressTotal = queue.length;
		let current: Row | undefined;
		try {
			for (const row of queue) {
				if (!loading) break;
				current = row;
				await api.put(ownPath(`/user-notes/${encodeURIComponent(row.userId)}`), { note: row.edited.slice(0, 256) });
				rows = rows.filter((r) => r.userId !== row.userId);
				progress++;
				await new Promise((resolve) => setTimeout(resolve, SEND_DELAY_MS));
			}
		} catch (err) {
			errors = `Name: ${current?.displayName}\n${ApiError.from(err).message}\n\n`;
		} finally {
			progress = 0;
			progressTotal = 0;
			loading = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-5xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_note_export_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="text-xs">
			{m.dialog_note_export_description1()}<br />
			{m.dialog_note_export_description2()}<br />
			{m.dialog_note_export_description3()}<br />
			{m.dialog_note_export_description4()}<br />
			{m.dialog_note_export_description5()}<br />
			{m.dialog_note_export_description6()}<br />
			{m.dialog_note_export_description7()}<br />
			{m.dialog_note_export_description8()}<br />
		</div>
		<div class="flex flex-wrap items-center gap-2">
			<Button size="sm" variant="outline" disabled={loading} onclick={refresh}>{m.dialog_note_export_refresh()}</Button>
			<Button size="sm" variant="outline" disabled={loading || rows.length === 0} onclick={exportNotes}>{m.dialog_note_export_export()}</Button>
			{#if loading}
				<Button size="sm" variant="outline" onclick={() => (loading = false)}>{m.dialog_note_export_cancel()}</Button>
				<span class="flex items-center gap-2 text-xs"><Spinner />Progress: {progress}/{progressTotal}</span>
			{/if}
		</div>
		{#if errors}
			<div>
				<Button size="sm" variant="outline" onclick={() => (errors = '')}>{m.dialog_note_export_clear_errors()}</Button>
				<h2 class="mt-2 font-bold">{m.dialog_note_export_errors()}</h2>
				<pre class="text-xs whitespace-pre-wrap">{errors}</pre>
			</div>
		{/if}
		<div class="max-h-125 overflow-y-auto rounded-md border">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head class="w-14">{m.dialog_group_member_moderation_avatar()}</Table.Head>
						<Table.Head class="w-48">{m.dialog_group_member_moderation_display_name()}</Table.Head>
						<Table.Head>{m.dialog_user_info_note()}</Table.Head>
						<Table.Head class="w-14"></Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each rows as row (row.userId)}
						<Table.Row>
							<Table.Cell>
								<Avatar.Root class="size-8">
									<Avatar.Image src={imageSrc(row.thumbnailImageUrl) ?? undefined} class="object-cover" />
									<Avatar.Fallback>{row.displayName.slice(0, 1)}</Avatar.Fallback>
								</Avatar.Root>
							</Table.Cell>
							<Table.Cell class="truncate"><a class="hover:underline" href={userHref(row.userId)}>{row.displayName}</a></Table.Cell>
							<Table.Cell>
								<textarea bind:value={row.edited} maxlength="256" rows="2" class="w-full resize-none rounded-md border border-input bg-transparent p-1 text-xs outline-none"></textarea>
							</Table.Cell>
							<Table.Cell>
								<Button size="icon-sm" variant="ghost" aria-label={m.common_actions_remove()} onclick={() => (rows = rows.filter((r) => r.userId !== row.userId))}><Trash2 class="size-4" /></Button>
							</Table.Cell>
						</Table.Row>
					{:else}
						<Table.Row><Table.Cell colspan={4} class="{EMPTY_STATE} text-xs">{m.common_no_data()}</Table.Cell></Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	</Dialog.Content>
</Dialog.Root>
