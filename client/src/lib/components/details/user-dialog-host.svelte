<script lang="ts">
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import { afterNavigate } from '$app/navigation';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import AvatarDetails from '#lib/components/details/avatar-details.svelte';
	import GroupDetails from '#lib/components/details/group-details.svelte';
	import GroupMemberModeration from '#lib/components/group-moderation/group-member-moderation.svelte';
	import GroupRoles from '#lib/components/group/group-roles.svelte';
	import UserDetails from '#lib/components/details/user-details.svelte';
	import WorldDetails from '#lib/components/details/world-details.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { profileBackgroundStyle } from '#lib/settings/profile-theme.js';

	// The one pop-up, MainDialogContainer.vue's shell. Links to users, worlds, avatars and groups are
	// plain URLs (see userHref in api.ts), so they can still open in a new tab; a normal click lands here.
	const OPENERS: Record<string, (id: string) => void> = {
		'/user': (id) => userDialog.open(id),
		'/world': (id) => userDialog.openWorld(id),
		'/avatar': (id) => userDialog.openAvatar(id),
		'/group': (id) => userDialog.openGroup(id)
	};
	const TITLES = { user: 'User details', world: 'World details', avatar: 'Avatar details', group: 'Group details', 'group-moderation': 'Group Member Moderation', 'group-roles': 'Group Roles' };

	function onClick(event: MouseEvent) {
		if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
		const link = (event.target as Element | null)?.closest?.('a[href]') as HTMLAnchorElement | null;
		if (!link || link.target && link.target !== '_self') return;
		const url = new URL(link.href, location.href);
		const id = url.searchParams.get('id');
		const opener = OPENERS[url.pathname];
		if (url.origin !== location.origin || !opener || !id) return;
		event.preventDefault();
		event.stopPropagation();
		opener(id);
	}

	afterNavigate(({ shallow, type }) => {
		if (shallow && type === 'goto') return;

		return userDialog.close();
	});

	const crumbs = $derived(userDialog.crumbs);
	// VRChat Profile Backgrounds: the user's gradient or texture behind the user pop-up.
	const dialogStyle = $derived(
		userDialog.kind === 'user' && appearance.displayVRCProfileBackgrounds.value
			? profileBackgroundStyle(userDialog.profileOf(userDialog.id), appearance.isDarkMode, appearance.profileBackgroundOpacity.value)
			: undefined
	);
</script>

<svelte:window onclickcapture={onClick}></svelte:window>

<Dialog.Root bind:open={() => userDialog.isOpen, (open) => { if (!open) userDialog.close(); }}>
	<Dialog.Content
		showCloseButton={false}
		style={dialogStyle}
		class="top-[10vh] flex max-h-[85vh] translate-y-0 flex-col gap-4 overflow-hidden rounded-lg border bg-background p-6 shadow-lg ring-0 duration-200 sm:max-w-270"
	>
		<Dialog.Title class="sr-only">{TITLES[userDialog.kind]}</Dialog.Title>
		{#if crumbs.length > 1}
			<nav class="mb-2 flex w-fit flex-shrink-0 items-center gap-1 rounded-xl bg-(--profile-card) pr-4 text-sm">
				<Button variant="ghost" size="icon-sm" aria-label="Back" onclick={() => userDialog.goTo(crumbs.length - 2)}><ArrowLeft /></Button>
				{#each crumbs as crumb, index (`${crumb.id}:${index}`)}
					{#if index > 0}<ChevronRight class="size-3.5 text-muted-foreground" />{/if}
					{#if index < crumbs.length - 1}
						<Button variant="ghost" size="sm" class="max-w-40 justify-start text-left" onclick={() => userDialog.goTo(index)}>
							<span class="truncate">{crumb.label}</span>
						</Button>
					{:else}
						<span class="max-w-40 truncate text-foreground">{crumb.label}</span>
					{/if}
				{/each}
			</nav>
		{/if}
		{#if userDialog.kind === 'world'}
			<WorldDetails worldId={userDialog.id} location={userDialog.location} shortName={userDialog.shortName} />
		{:else if userDialog.kind === 'group'}
			<GroupDetails groupId={userDialog.id} />
		{:else if userDialog.kind === 'group-moderation'}
			{#key userDialog.id}<GroupMemberModeration groupId={userDialog.id} />{/key}
		{:else if userDialog.kind === 'group-roles'}
			{#key userDialog.id}<GroupRoles groupId={userDialog.id} />{/key}
		{:else if userDialog.kind === 'avatar'}
			<AvatarDetails avatarId={userDialog.id} />
		{:else}
			<UserDetails userId={userDialog.id} />
		{/if}
	</Dialog.Content>
</Dialog.Root>
