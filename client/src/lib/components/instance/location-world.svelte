<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Lock from '@lucide/svelte/icons/lock';
	import LockOpen from '@lucide/svelte/icons/lock-open';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { closedInstances, instanceDialogs } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation, resolveRegion } from '#lib/instances/location.js';
	import { groupNames } from '#lib/people/names.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { cn } from '#lib/utils.js';

	// The original's LocationWorld pill: region flag, "Access ⋅ #name" that opens the launch dialog,
	// the group (opens it), the closed-at warning and the strict lock. Unlocked when the instance is
	// the account's own or its short name is the one the world page was opened with.
	let {
		location,
		shortName = '',
		worldDialogShortName = '',
		currentUserId = '',
		groupHint = '',
		displayName = '',
		bold = false,
		class: className = ''
	}: {
		location: string;
		shortName?: string | null;
		worldDialogShortName?: string | null;
		currentUserId?: string | null;
		groupHint?: string;
		/** The instance's own display name, when VRChat gave one. */
		displayName?: string | null;
		bold?: boolean;
		class?: string;
	} = $props();

	const ACCESS_NAMES: Record<string, () => string> = {
		public: m.dialog_new_instance_access_type_public,
		group: m.dialog_new_instance_access_type_group,
		'friends+': m.dialog_new_instance_access_type_friend_plus,
		friends: m.dialog_new_instance_access_type_friend,
		'invite+': m.dialog_new_instance_access_type_invite_plus,
		invite: m.dialog_new_instance_access_type_invite,
		groupPublic: m.dialog_new_instance_group_access_type_public,
		groupPlus: m.dialog_new_instance_group_access_type_plus,
		groupMembers: m.dialog_new_instance_group_access_type_members
	};

	/** The original's translateAccessType: group kinds read "Group Public" and the like. */
	function accessName(raw: string): string {
		const name = ACCESS_NAMES[raw];
		if (!name) return raw;
		if (raw === 'groupPublic' || raw === 'groupPlus') return `${m.dialog_new_instance_access_type_group()} ${name()}`;
		return name();
	}

	const L = $derived(parseLocation(location));
	const region = $derived(resolveRegion(L) || 'us');
	const access = $derived(accessName(L.accessTypeName));
	const name = $derived(displayName || L.instanceName);
	const unlocked = $derived(Boolean((worldDialogShortName && shortName && worldDialogShortName === shortName) || (currentUserId && currentUserId === L.userId)));
	const groupName = $derived(L.isRealInstance && L.groupId ? groupHint || groupNames.get(L.groupId) || L.groupId : '');
	const closedAt = $derived(L.isRealInstance ? closedInstances.get(location) : undefined);
</script>

<div class={cn('flex min-w-0 items-center gap-2 overflow-hidden rounded-full border border-muted-foreground/10 px-2 py-0.5 text-muted-foreground', className)}>
	<span class={cn('flags inline-block shrink-0', region)}></span>
	<button
		type="button"
		class={cn('min-w-0 flex-1 cursor-pointer truncate text-left text-muted-foreground', bold && 'font-bold')}
		title={`${access} ⋅ #${name}`}
		onclick={() => (instanceDialogs.launch = { location, shortName })}
	>
		{#if unlocked}<LockOpen class="mr-1.25 inline-block size-4" />{/if}<span>{access} ⋅ #{name}</span>
	</button>
	{#if groupName}
		<button type="button" class="min-w-0 shrink cursor-pointer truncate" title={groupName} onclick={() => L.groupId && userDialog.openGroup(L.groupId)}>({groupName})</button>
	{/if}
	{#if closedAt}
		<Tooltip.Root delayDuration={300}>
			<Tooltip.Trigger><TriangleAlert class="my-auto size-4 text-orange-500" /></Tooltip.Trigger>
			<Tooltip.Content side="top">{m.dialog_user_info_instance_closed_at()}: {formatDateFilter(closedAt, 'long')}</Tooltip.Content>
		</Tooltip.Root>
	{/if}
	{#if L.strict}<Lock class="size-4 shrink-0 text-muted-foreground" />{/if}
</div>
