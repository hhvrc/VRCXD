<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as InputGroup from '#lib/components/ui/input-group/index.js';

	// The original's WorldAllowedDomainsDialog: the world's urlList, one domain per row.
	let { open = $bindable(false), path, urlList = [], onsaved }: { open?: boolean; path: string; urlList?: string[]; onsaved: () => void } = $props();

	let domains = $state<string[]>([]);

	$effect(() => {
		if (open) domains = untrack(() => [...urlList]);
	});

	function save() {
		const list = [...domains];
		// As the original, the dialog closes at once and the save reports when it lands.
		open = false;
		api.put(path, { urlList: list }).then(
			() => {
				toast.success('Allowed Video Player Domains updated');
				onsaved();
			},
			(err) => toastError(err)
		);
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-150">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_allowed_video_player_domains_header()}</Dialog.Title>
		</Dialog.Header>

		<div>
			{#each domains as _, index (index)}
				<InputGroup.Root class="mt-1.5 h-8">
					<InputGroup.Input bind:value={domains[index]} class="h-8" />
					<InputGroup.Addon align="inline-end">
						<Button variant="ghost" size="icon-sm" aria-label={m.dialog_world_actions_delete()} onclick={() => domains.splice(index, 1)}><Trash2 /></Button>
					</InputGroup.Addon>
				</InputGroup.Root>
			{/each}
			<Button size="sm" variant="outline" style="margin-top: 6px" onclick={() => domains.push('')}>{m.dialog_allowed_video_player_domains_add_domain()}</Button>
		</div>

		<Dialog.Footer>
			<Button onclick={save}>{m.dialog_config_json_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
