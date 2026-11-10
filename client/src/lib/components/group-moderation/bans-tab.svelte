<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import MemberTable from '#lib/components/group-moderation/member-table.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { groupModeration as M } from '#lib/groups/group-moderation.svelte.js';

	// The original's GroupModerationBansTab: the search narrows the loaded bans by name.
	let { onexport, onimport }: { onexport: () => void; onimport: () => void } = $props();

	const shown = $derived.by(() => {
		const q = M.bansSearch.trim().toLowerCase();
		return q ? M.bans.filter((r) => (r.user?.displayName ?? '').toLowerCase().includes(q)) : M.bans;
	});
</script>

<div class="flex h-full min-h-0 flex-col gap-2">
	<div class="flex justify-between">
		<div class="flex items-center gap-2">
			<Button class="rounded-full" variant="outline" size="icon-sm" aria-label={m.dialog_note_export_refresh()} disabled={M.loading} onclick={() => M.getAllGroupBans()}>
				{#if M.loading}<Spinner />{:else}<RefreshCw />{/if}
			</Button>
			<Button size="sm" variant="outline" onclick={() => M.selectAll(M.bans)}>{m.view_favorite_select_all()}</Button>
			<span class="mx-1.5 text-sm">{M.bans.length}</span>
		</div>
		<div class="flex items-center gap-2">
			<Button variant="outline" size="sm" disabled={!M.bans.length} onclick={onexport}>{m.dialog_group_member_moderation_export_bans()}</Button>
			<Button variant="outline" size="sm" disabled={!M.can('group-bans-manage')} onclick={onimport}>{m.dialog_group_member_moderation_import_bans()}</Button>
			<ClearableInput bind:value={M.bansSearch} class="w-80" placeholder={m.view_favorite_worlds_search()} />
		</div>
	</div>
	<MemberTable rows={shown} kind="bans" persistKey="group-moderation:bans" loading={M.loading} />
</div>
