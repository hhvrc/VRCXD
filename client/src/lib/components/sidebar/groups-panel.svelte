<script lang="ts">
	import { PERSON_NAME } from '#lib/details.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import BackToTop from '#lib/components/common/back-to-top.svelte';
	import { goto } from '$app/navigation';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import Users from '@lucide/svelte/icons/users';
	import Location from '#lib/components/instance/location.svelte';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { ApiError } from '#lib/api/client.js';
	import { groupHref, iconSrc } from '#lib/api/links.js';
	import { canInviteSelf, instanceDialogs, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { sidebarData, type GroupInstance } from '#lib/people/sidebar-data.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupsSidebar: open instances grouped by group, each group folding.
	const ageGatedVisible = $derived(appearance.isAgeGatedInstancesVisible.value);
	let collapsed = $state<string[]>([]);
	let notice = $state('');

	const byGroup = $derived.by(() => {
		const groups = new Map<string, GroupInstance[]>();
		for (const instance of sidebarData.groupInstances) {
			const list = groups.get(instance.groupId);
			if (list) list.push(instance);
			else groups.set(instance.groupId, [instance]);
		}
		return [...groups.values()];
	});

	async function invite(location: string) {
		try {
			await selfInvite(location);
			notice = 'Self invite sent';
		} catch (err) {
			notice = ApiError.from(err).message;
		}
		setTimeout(() => (notice = ''), 3000);
	}
	// The original's BackToTop for this list.
	let scroller = $state<HTMLElement | undefined>();
</script>

<div class="relative h-full">
	<div bind:this={scroller} class="h-full w-full overflow-auto">
		<div class="px-1.5 py-2.5">
			{#each byGroup as group, index (group[0].groupId)}
				{@const groupId = group[0].groupId}
				{@const isCollapsed = collapsed.includes(groupId)}
				<div class="cursor-pointer pt-4 pb-1.5 text-xs" style="padding-top: {index === 0 ? '0px' : '10px'}">
					<div
						class="flex items-center"
						role="button"
						tabindex="0"
						onclick={() => (collapsed = isCollapsed ? collapsed.filter((id) => id !== groupId) : [...collapsed, groupId])}
						onkeydown={(e) => e.key === 'Enter' && e.currentTarget.click()}
					>
						<ChevronDown class={['size-4 transition-transform duration-200 ease-in-out', isCollapsed && '-rotate-90']} />
						<span class="ml-1.5">{group[0].groupName} &ndash; {group.length}</span>
					</div>
				</div>
				{#if !isCollapsed}
					{#each group as row (row.location)}
						<ContextMenu.Root>
							<ContextMenu.Trigger>
								{#snippet child({ props })}
									<div {...props} class="box-border flex cursor-pointer items-center p-1.5 text-compact hover:rounded-lg hover:bg-muted/50" role="button" tabindex="0" onclick={() => goto(groupHref(row.groupId))} onkeydown={(e) => e.key === 'Enter' && goto(groupHref(row.groupId))}>
										{#if ageGatedVisible || !row.ageGate}
											<PersonAvatar src={iconSrc(row.groupIconUrl)} fallback={Users} />
											<div class="flex-1 overflow-hidden">
												<span class={PERSON_NAME}>
													<span>{row.groupName}</span>
													<span class="ml-1.5 font-normal">({row.userCount}/{row.capacity})</span>
												</span>
												<Location class="text-xs" location={row.location} link={false} />
											</div>
										{/if}
									</div>
								{/snippet}
							</ContextMenu.Trigger>
							<ContextMenu.Content>
								<ContextMenu.Item disabled={!canInviteSelf(row.location, vrchatAuth.ownerUserId)} onSelect={() => (instanceDialogs.launch = { location: row.location })}>{m.dialog_user_info_launch_invite_tooltip()}</ContextMenu.Item>
								<ContextMenu.Item disabled={!canInviteSelf(row.location, vrchatAuth.ownerUserId)} onSelect={() => invite(row.location)}>{m.dialog_user_info_self_invite_tooltip()}</ContextMenu.Item>
							</ContextMenu.Content>
						</ContextMenu.Root>
					{/each}
				{/if}
			{/each}
		</div>
	</div>
	{#if notice}<div class="absolute right-2 bottom-2 left-2 rounded-md border bg-card px-3 py-2 text-xs shadow">{notice}</div>{/if}
</div>

<BackToTop target={scroller} />
