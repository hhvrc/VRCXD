<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Copy from '@lucide/svelte/icons/copy';
	import Share2 from '@lucide/svelte/icons/share-2';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import type { DetailActions } from '#lib/helpers/detail-actions.svelte.js';

	// Share, then Copy URL, Copy Name, Copy ID.
	let { kind, url, name, id, actions }: { kind: string; url: string; name: string; id: string; actions: DetailActions } = $props();

	// Only the original's world dialog names what it copied; the others say "Copied successfully!".
	const said = $derived(kind === 'world' ? { url: m.message_world_url_copied(), name: m.message_world_name_copied(), id: m.message_world_id_copied() } : undefined);
</script>

<DropdownMenu.Sub>
	<DropdownMenu.SubTrigger><Share2 class="size-4" /> {m.dialog_user_actions_share()}</DropdownMenu.SubTrigger>
	<DropdownMenu.SubContent class="w-auto whitespace-nowrap">
		<DropdownMenu.Item onSelect={() => actions.copy(url, said?.url)}><Copy class="size-4" /> {m.dialog_user_info_copy_url()}</DropdownMenu.Item>
		<DropdownMenu.Item onSelect={() => actions.copy(name, said?.name)}><Copy class="size-4" /> {m.dialog_world_info_copy_name()}</DropdownMenu.Item>
		<DropdownMenu.Item onSelect={() => actions.copy(id, said?.id)}><Copy class="size-4" /> {m.dialog_user_info_copy_id()}</DropdownMenu.Item>
	</DropdownMenu.SubContent>
</DropdownMenu.Sub>
