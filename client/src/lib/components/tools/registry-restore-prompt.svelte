<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import RegistryBackupDialog from '#lib/components/tools/registry-backup-dialog.svelte';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import { checkRegistryBackups } from '#lib/agent/agent.js';

	// The original's checkAutoBackupRestoreVrcRegistry at start: once per session with the PC's
	// agent connected, the server backs up VRChat's registry when due, or says it is gone, and then
	// the backups are offered.

	let checkedFor = '';
	let open = $state(false);
	let confirming = $state<Confirming | undefined>();

	$effect(() => {
		const owner = vrchatAuth.ownerUserId;
		if (!owner || checkedFor === owner || !agentCan('registry')) return;
		checkedFor = owner;
		checkRegistryBackups()
			.then((ask) => {
				if (!ask) return;
				open = true;
				confirming = { title: m.dialog_registry_backup_header(), description: m.dialog_registry_backup_restore_prompt(), run: () => (confirming = undefined) };
			})
			.catch(() => {});
	});
</script>

<RegistryBackupDialog bind:open />
<ActionDialogs bind:confirming />
