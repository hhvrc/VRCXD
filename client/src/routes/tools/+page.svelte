<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import AutoChangeStatusDialog from '#lib/components/tools/auto-change-status-dialog.svelte';
	import CalendarDialog from '#lib/components/tools/calendar-dialog.svelte';
	import ExportDialogs from '#lib/components/tools/export-dialogs.svelte';
	import GroupListsDialog from '#lib/components/tools/group-lists-dialog.svelte';
	import InfoPushDialog from '#lib/components/tools/info-push-dialog.svelte';
	import InviteMessagesDialog from '#lib/components/tools/invite-messages-dialog.svelte';
	import ModerationReportsDialog from '#lib/components/tools/moderation-reports-dialog.svelte';
	import NoteExportDialog from '#lib/components/tools/note-export-dialog.svelte';
	import PurchaseHistoryDialog from '#lib/components/tools/purchase-history-dialog.svelte';
	import RegistryBackupDialog from '#lib/components/tools/registry-backup-dialog.svelte';
	import VrchatConfigDialog from '#lib/components/tools/vrchat-config-dialog.svelte';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import { openFolder } from '#lib/agent/agent.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { toast } from 'svelte-sonner';
	import { ApiError } from '#lib/api/client.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { readPinnedTools, toolCategories, tools, writePinnedTools, type Tool } from '#lib/nav/tools.js';
	import { pinnedTools } from '#lib/nav/pinned-tools.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Tools.vue: collapsible categories of tool cards, each pinnable to the nav.
	// Sidebar pins link here with ?open=<key>, which opens that tool's dialog or page.

	const COLLAPSED_KEY = 'vrcx.toolsCategoryCollapsed';

	let collapsed = $state<Record<string, boolean>>({});
	let exportTool = $state<'export-friend-list' | 'export-own-avatars' | 'discord-names' | null>(null);
	let groupMode = $state<'blocked-groups' | 'group-invites' | null>(null);
	let noteExportOpen = $state(false);
	let inviteMessagesOpen = $state(false);
	let reportsOpen = $state(false);
	let purchasesOpen = $state(false);
	let calendarOpen = $state(false);
	let infoPushOpen = $state(false);
	let autoStatusOpen = $state(false);
	let vrchatConfigOpen = $state(false);
	let registryBackupOpen = $state(false);
	let notice = $state('');

	onMount(() => {
		const stored = readSetting<unknown>(COLLAPSED_KEY, {});
		if (stored && typeof stored === 'object') collapsed = stored as typeof collapsed;
		pinnedTools.keys = readPinnedTools();
	});

	function toggleCategory(key: string) {
		collapsed[key] = !collapsed[key];
		storeSetting(COLLAPSED_KEY, collapsed);
	}

	function togglePin(tool: Tool) {
		pinnedTools.keys = pinnedTools.keys.includes(tool.key) ? pinnedTools.keys.filter((k) => k !== tool.key) : [...pinnedTools.keys, tool.key];
		writePinnedTools(pinnedTools.keys);
	}

	const usable = (tool: Tool) => tool.kind === 'dialog' || (tool.kind === 'agent' && !!tool.capability && agentCan(tool.capability));

	function trigger(tool: Tool) {
		notice = '';
		if (tool.kind === 'desktop') {
			notice = `${tool.title} works on your own PC's files, so it needs the desktop app.`;
			return;
		}
		if (tool.kind === 'pending') {
			notice = `${tool.title} is not in this client yet.`;
			return;
		}
		if (!vrchatAuth.ownerUserId) {
			notice = 'Log in to VRChat first.';
			return;
		}
		if (tool.kind === 'agent') {
			if (!tool.capability || !agentCan(tool.capability)) {
				notice = `${tool.title} works on your PC, so it needs the VRCX agent running there.`;
				return;
			}
			if (tool.folder) {
				// The original's app-api tool action: a toast either way.
				openFolder(tool.folder).then(
					() => toast.success(m.message_file_folder_opened()),
					(err) => (ApiError.from(err).status === 404 ? toast.error(m.message_file_folder_missing()) : toastError(err))
				);
				return;
			}
		}
		switch (tool.key) {
			case 'export-friend-list':
			case 'export-own-avatars':
			case 'discord-names':
				exportTool = tool.key;
				break;
			case 'blocked-groups':
			case 'group-invites':
				groupMode = tool.key;
				break;
			case 'export-notes':
				noteExportOpen = true;
				break;
			case 'edit-invite-message':
				inviteMessagesOpen = true;
				break;
			case 'moderation-reports':
				reportsOpen = true;
				break;
			case 'purchase-history':
				purchasesOpen = true;
				break;
			case 'group-calendar':
				calendarOpen = true;
				break;
			case 'info-push':
				infoPushOpen = true;
				break;
			case 'gallery':
				goto('/tools/gallery');
				break;
			case 'screenshot-metadata':
				goto('/tools/screenshot-metadata');
				break;
			case 'vrchat-store':
				goto('/tools/vrchat-store');
				break;
			case 'auto-change-status':
				autoStatusOpen = true;
				break;
			case 'vrchat-config':
				vrchatConfigOpen = true;
				break;
			case 'registry-backup':
				registryBackupOpen = true;
				break;
		}
	}

	$effect(() => {
		const key = page.url.searchParams.get('open');
		if (!key) return;
		if (key === 'gallery' || key === 'vrchat-store') {
			goto(`/tools/${key}`, { replaceState: true });
			return;
		}
		const tool = tools.find((t) => t.key === key);
		if (tool) trigger(tool);
		goto('/tools', { replaceState: true, reset: false });
	});
</script>

<svelte:head><title>Tools · VRCXD</title></svelte:head>

<div class="h-full overflow-y-auto p-4">
	<div class="text-lg font-semibold">{m.view_tools_header()}</div>
	{#if notice}<p class="mt-2 text-xs text-muted-foreground">{notice}</p>{/if}
	<div class="mt-5 px-5">
		{#each toolCategories as category (category.key)}
			<div class="mb-6">
				<button type="button" class="mb-3 flex w-full cursor-pointer items-center rounded-lg p-2 px-3 text-left transition-all duration-200 ease-in-out" onclick={() => toggleCategory(category.key)}>
					<i class="ri-arrow-down-s-line mr-2 text-sm transition-transform duration-300 {collapsed[category.key] ? '-rotate-90' : ''}" aria-hidden="true"></i>
					<span class="ml-1.5 text-base font-semibold">{category.label}</span>
				</button>
				{#if !collapsed[category.key]}
					<div class="ml-4 grid grid-cols-2 gap-4">
						{#each tools.filter((t) => t.category === category.key) as tool (tool.key)}
							{@const pinned = pinnedTools.keys.includes(tool.key)}
							<div
								role="button"
								tabindex="0"
								class="group flex cursor-pointer items-center gap-3 rounded-lg border p-3 hover:bg-accent/50 {usable(tool) ? '' : 'opacity-60'}"
								onclick={() => trigger(tool)}
								onkeydown={(e) => e.key === 'Enter' && trigger(tool)}
							>
								<i class="{tool.icon} inline-flex items-center justify-center text-2xl" aria-hidden="true"></i>
								<div class="min-w-0 flex-1">
									<div class="flex items-start gap-2">
										<div class="flex-1 text-sm font-medium">{tool.title}</div>
										{#if tool.kind === 'dialog' || tool.kind === 'agent'}
											<Button
												size="icon-xs"
												variant={pinned ? 'secondary' : 'ghost'}
												class="opacity-0 transition-opacity group-hover:opacity-100"
												title={pinned ? 'Unpin from nav' : 'Pin to nav'}
												aria-label={pinned ? 'Unpin from nav' : 'Pin to nav'}
												onclick={(e: MouseEvent) => {
													e.stopPropagation();
													togglePin(tool);
												}}
											>
												<span class="relative inline-flex size-4">
													<i class="ri-side-bar-line inline-flex size-4 items-center justify-center text-base" aria-hidden="true"></i>
													<span class="absolute -top-1 -right-1 grid size-2.5 place-items-center rounded-full bg-background shadow-sm">
														<i class="{pinned ? 'ri-subtract-line' : 'ri-add-line'} inline-flex size-2 items-center justify-center text-3xs" aria-hidden="true"></i>
													</span>
												</span>
											</Button>
										{/if}
									</div>
									<div class="text-sm text-muted-foreground">{tool.description}</div>
								</div>
							</div>
						{/each}
					</div>
				{/if}
			</div>
		{/each}
	</div>
</div>

<ExportDialogs bind:tool={exportTool} />
<GroupListsDialog bind:mode={groupMode} />
<NoteExportDialog bind:open={noteExportOpen} />
<InviteMessagesDialog bind:open={inviteMessagesOpen} />
<ModerationReportsDialog bind:open={reportsOpen} />
<PurchaseHistoryDialog bind:open={purchasesOpen} />
<CalendarDialog bind:open={calendarOpen} />
<InfoPushDialog bind:open={infoPushOpen} />
<AutoChangeStatusDialog bind:open={autoStatusOpen} />
<VrchatConfigDialog bind:open={vrchatConfigOpen} />
<RegistryBackupDialog bind:open={registryBackupOpen} />
