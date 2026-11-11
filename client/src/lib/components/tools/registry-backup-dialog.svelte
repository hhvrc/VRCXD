<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Download from '@lucide/svelte/icons/download';
	import RotateCcw from '@lucide/svelte/icons/rotate-ccw';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import ActionDialogs, { type Confirming, type Prompting } from '#lib/components/dialogs/action-dialogs.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import type { AccountSettings } from '#lib/api/types.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { downloadJson } from '#lib/groups/group-dialog.svelte.js';
	import {
		createRegistryBackup,
		deleteRegistryBackup,
		deleteVrchatRegistry,
		isRegistryData,
		listRegistryBackups,
		restoreRegistry,
		restoreRegistryBackup,
		type RegistryBackup
	} from '#lib/agent/agent.js';

	// The original's RegistryBackupDialog: backups of VRChat's registry, kept by the server, restored
	// on the PC by its agent.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	let backups = $state<RegistryBackup[]>([]);
	let settings = $state<AccountSettings | undefined>();
	let confirming = $state<Confirming | undefined>();
	let prompting = $state<Prompting | undefined>();
	let fileInput = $state<HTMLInputElement | undefined>();

	$effect(() => {
		if (!open) return;
		untrack(refresh);
	});

	async function refresh() {
		try {
			[backups, settings] = await Promise.all([listRegistryBackups(), api.get<AccountSettings>(ownPath('/settings'))]);
		} catch (err) {
			toastError(err);
		}
	}

	async function saveSetting(patch: Partial<AccountSettings>) {
		if (!settings) return;
		try {
			settings = await api.put<AccountSettings>(ownPath('/settings'), { ...settings, ...patch });
		} catch (err) {
			toastError(err);
		}
	}

	const restoreFailed = (err: unknown) => toast.error(m.message_registry_restore_failed({ error: ApiError.from(err).message }));

	function restore(backup: RegistryBackup) {
		confirming = {
			title: m.confirm_title(),
			description: m.confirm_restore_backup(),
			run: async () => {
				confirming = undefined;
				try {
					await restoreRegistryBackup(backup.id);
					toast.success(m.message_registry_restored());
				} catch (err) {
					restoreFailed(err);
				}
			}
		};
	}

	async function forget(backup: RegistryBackup) {
		try {
			await deleteRegistryBackup(backup.id);
		} catch (err) {
			toastError(err);
		}
		await refresh();
	}

	function reset() {
		confirming = {
			title: m.confirm_title(),
			description: m.confirm_delete_vrc_registry(),
			destructive: true,
			run: async () => {
				confirming = undefined;
				try {
					await deleteVrchatRegistry();
					toast.success(m.message_registry_deleted());
				} catch (err) {
					toastError(err);
				}
			}
		};
	}

	function promptName() {
		prompting = {
			title: m.prompt_backup_name_header(),
			description: m.prompt_backup_name_description(),
			value: 'Backup',
			errorMessage: m.prompt_backup_name_input_error(),
			run: async (name) => {
				try {
					await createRegistryBackup(name);
				} catch (err) {
					toastError(err);
				}
				await refresh();
			}
		};
	}

	// restoreVrcRegistryFromFile: a .json backup read here and written on the PC.
	async function restoreFromFile(event: Event & { currentTarget: HTMLInputElement }) {
		const file = event.currentTarget.files?.[0];
		event.currentTarget.value = '';
		if (!file) return;
		let data: unknown;
		try {
			data = JSON.parse(await file.text());
		} catch {
			data = undefined;
		}
		if (!isRegistryData(data)) {
			toast.error(m.message_registry_invalid_json());
			return;
		}
		try {
			await restoreRegistry(data);
			toast.success(m.message_registry_restored());
		} catch (err) {
			restoreFailed(err);
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_registry_backup_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="mt-2">
			<div class="flex items-center justify-between text-xs">
				<span class="mr-6">{m.dialog_registry_backup_auto_backup()}</span>
				<Switch checked={settings?.vrcRegistryAutoBackup ?? true} disabled={!settings} onCheckedChange={(v) => saveSetting({ vrcRegistryAutoBackup: v })} />
			</div>
			<div class="mt-1.5 flex items-center justify-between text-xs">
				<span class="mr-6">{m.dialog_registry_backup_ask_to_restore()}</span>
				<Switch checked={settings?.vrcRegistryAskRestore ?? true} disabled={!settings} onCheckedChange={(v) => saveSetting({ vrcRegistryAskRestore: v })} />
			</div>
			<div class="mt-2 max-h-80 overflow-y-auto rounded-md border">
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.dialog_registry_backup_name()}</Table.Head>
							<Table.Head>{m.dialog_registry_backup_date()}</Table.Head>
							<Table.Head class="text-right">{m.dialog_registry_backup_action()}</Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each backups as backup (backup.id)}
							<Table.Row>
								<Table.Cell>{backup.name}</Table.Cell>
								<Table.Cell>{formatDateFilter(backup.date, 'long')}</Table.Cell>
								<Table.Cell class="text-right">
									<Button variant="ghost" size="icon-sm" title={m.dialog_registry_backup_restore()} aria-label={m.dialog_registry_backup_restore()} onclick={() => restore(backup)}><RotateCcw /></Button>
									<Button variant="ghost" size="icon-sm" title={m.dialog_registry_backup_save_to_file()} aria-label={m.dialog_registry_backup_save_to_file()} onclick={() => downloadJson(backup.name, backup.data)}><Download /></Button>
									<Button variant="ghost" size="icon-sm" title={m.dialog_registry_backup_delete()} aria-label={m.dialog_registry_backup_delete()} onclick={() => forget(backup)}><Trash2 /></Button>
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			</div>
			<div class="mt-2 flex items-center justify-between">
				<Button size="sm" variant="destructive" onclick={reset}>{m.dialog_registry_backup_reset()}</Button>
				<div class="flex gap-2">
					<Button size="sm" variant="outline" onclick={promptName}>{m.dialog_registry_backup_backup()}</Button>
					<Button size="sm" variant="outline" onclick={() => fileInput?.click()}>{m.dialog_registry_backup_restore_from_file()}</Button>
					<input bind:this={fileInput} type="file" accept=".json" class="hidden" onchange={restoreFromFile} />
				</div>
			</div>
		</div>
	</Dialog.Content>
</Dialog.Root>

<ActionDialogs bind:confirming bind:prompting />
