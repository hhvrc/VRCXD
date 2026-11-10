<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { timeToText } from '#lib/helpers/format.js';
	import type { InviteMessage } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's EditInviteMessagesDialog and EditInviteMessageDialog: four tabs of message
	// slots, each with VRChat's one hour cool down. A row on cool down cannot be opened.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const COOL_DOWN_MS = 60 * 60 * 1000;
	const tabs = [
		{ value: 'message', get label() { return m.dialog_edit_invite_messages_invite_message_tab(); } },
		{ value: 'request', get label() { return m.dialog_edit_invite_messages_invite_request_tab(); } },
		{ value: 'requestResponse', get label() { return m.dialog_edit_invite_messages_invite_request_response_tab(); } },
		{ value: 'response', get label() { return m.dialog_edit_invite_messages_invite_response_tab(); } }
	];

	let messages = $state<Record<string, InviteMessage[]>>({});
	let activeTab = $state('message');
	const now = $derived(clock(5000).now);
	let editing = $state<InviteMessage | null>(null);
	let draft = $state('');
	let notice = $state('');
	let error = $state('');

	const rows = $derived([...(messages[activeTab] ?? [])].sort((a, b) => a.slot - b.slot));
	const coolDownLeft = (row: InviteMessage) => (row.updatedAt ? new Date(row.updatedAt).getTime() + COOL_DOWN_MS - now : -1);
	const path = (rest: string) => ownPath(`/invite-messages/${rest}`);

	async function load() {
		error = '';
		try {
			const lists = await Promise.all(tabs.map((tab) => api.get<InviteMessage[]>(path(tab.value))));
			messages = Object.fromEntries(tabs.map((tab, i) => [tab.value, lists[i]]));
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	$effect(() => {
		if (!open) return;
		untrack(() => {
			notice = '';
			load();
		});
	});

	function openRow(row: InviteMessage) {
		if (coolDownLeft(row) > 0) {
			notice = 'This invite message is on cooldown and cannot be edited yet.';
			return;
		}
		notice = '';
		draft = row.message;
		editing = row;
	}

	async function save() {
		const row = editing;
		editing = null;
		if (!row || row.message === draft) return;
		try {
			const updated = await api.put<InviteMessage[]>(path(`${activeTab}/${row.slot}`), { message: draft });
			if (updated.find((item) => item.slot === row.slot)?.message === row.message) {
				error = 'Invite message update failed';
				return;
			}
			messages[activeTab] = updated;
			notice = 'Invite message updated';
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-5xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_edit_invite_messages_header()}</Dialog.Title>
		</Dialog.Header>
		<Tabs.Root bind:value={activeTab}>
			<Tabs.List>
				{#each tabs as tab (tab.value)}
					<Tabs.Trigger value={tab.value}>{tab.label}</Tabs.Trigger>
				{/each}
			</Tabs.List>
		</Tabs.Root>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		{#if notice}<p class="text-xs text-muted-foreground">{notice}</p>{/if}
		<div class="max-h-[60vh] overflow-y-auto rounded-md border">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head class="w-18">{m.table_profile_invite_messages_slot()}</Table.Head>
						<Table.Head>{m.dialog_group_post_edit_message()}</Table.Head>
						<Table.Head class="w-28 text-right">{m.table_profile_invite_messages_cool_down()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each rows as row (row.slot)}
						{@const left = coolDownLeft(row)}
						<Table.Row class="cursor-pointer" onclick={() => openRow(row)}>
							<Table.Cell>{row.slot}</Table.Cell>
							<Table.Cell class="whitespace-normal">{row.message}</Table.Cell>
							<Table.Cell class="text-right">{left >= 0 ? timeToText(left) : '-'}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root open={editing !== null} onOpenChange={(o) => !o && (editing = null)}>
	<Dialog.Content class="sm:max-w-sm">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_edit_invite_message_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="text-xs">
			<span>{m.dialog_edit_invite_message_description()}</span>
			<textarea bind:value={draft} maxlength="64" rows="2" class="mt-2.5 w-full resize-none rounded-lg border border-input bg-transparent p-2 text-sm outline-none"></textarea>
			<div class="text-right text-muted-foreground">{draft.length}/64</div>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (editing = null)}>{m.dialog_invite_message_cancel()}</Button>
			<Button onclick={save}>{m.dialog_config_json_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
