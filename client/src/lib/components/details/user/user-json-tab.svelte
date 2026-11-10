<script lang="ts">
	import JsonCard from '#lib/components/details/json-card.svelte';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import type { User } from '#lib/api/types.js';

	let { user, userId, isSelf, onrefresh }: { user: User; userId: string; isSelf: boolean; onrefresh: () => void } = $props();
</script>

<!-- The original's tree: the account's own object too on its own page, then the user and their profile. -->
<JsonCard value={isSelf ? { currentUser: sidebarData.me, user, profile: userDialog.profileOf(userId) } : { user, profile: userDialog.profileOf(userId) }} fileName={userId} download={user} {onrefresh} />
