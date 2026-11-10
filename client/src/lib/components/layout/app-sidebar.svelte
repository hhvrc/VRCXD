<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Plus from '@lucide/svelte/icons/plus';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { THEME_COLORS, THEME_CONFIG, THEME_MODES } from '#lib/settings/themes.js';
	import { dashboards, type Dashboard } from '#lib/nav/dashboards.svelte.js';
	import * as Sidebar from '#lib/components/ui/sidebar/index.js';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import CustomNavDialog from '#lib/components/dialogs/custom-nav-dialog.svelte';
	import { type NavItem } from '#lib/nav/nav-items.js';
	import { pinnedTools } from '#lib/nav/pinned-tools.svelte.js';
	import { writePinnedTools } from '#lib/nav/tools.js';
	import { navLayout } from '#lib/nav/nav-layout.svelte.js';
	import { DASHBOARD_NAV_KEY_PREFIX, TOOL_NAV_KEY_PREFIX, activeNavKey, isDashboardNavKey, routeNavKeys, isToolNavKey, type NavDefinition, type NavFolderEntry } from '#lib/nav/nav-layout.js';
	import { api } from '#lib/api/client.js';
	import { directAccess } from '#lib/nav/direct-access.svelte.js';
	import { setOwnerUserId, vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { navNotify } from '#lib/nav/nav-notify.svelte.js';

	// The active entry comes from the path; a tool page lights its pinned tool, or else Tools.
	const activeKey = $derived(activeNavKey(navLayout.entries, routeNavKeys(page.url.pathname, navLayout.definitions)));
	function isActive(def: NavDefinition) {
		return def.key === activeKey;
	}

	// Direct Access needs a VRChat session.
	const visible = (def: NavDefinition) => def.action !== 'direct-access' || !!vrchatAuth.ownerUserId;

	const sidebar = Sidebar.useSidebar();
	/** Folders opened or closed by hand; otherwise a folder is open while one of its pages is. */
	let openFolders = $state<Record<string, boolean>>({});

	/** unpinToolFromNav. */
	function unpinTool(key: string) {
		const tool = key.slice(TOOL_NAV_KEY_PREFIX.length);
		pinnedTools.keys = pinnedTools.keys.filter((k) => k !== tool);
		writePinnedTools(pinnedTools.keys);
	}

	// Settings is reached from the footer's Manage menu rather than the nav lists, like the Vue app's
	// NavMenuFooter, which also holds the theme mode and colour.
	const settingsItem: NavItem = { key: 'settings', icon: 'ri-settings-3-line', get label() { return m.nav_tooltip_manage(); } };

	let confirming = $state<Confirming | undefined>();
	const SUPPORT_LINKS = { wiki: 'https://github.com/vrcx-team/VRCX/wiki', github: 'https://github.com/vrcx-team/VRCX', discord: 'https://vrcx.app/discord' };

	// New notifications and friendships light their page's entry until it is opened.
	$effect(() =>
		onLiveEvent((event) => {
			const account = vrchatAuth.ownerUserId;
			if (event.event.case === 'notification' && event.event.value.ownerUserId === account) navNotify.notify('notification', page.url.pathname);
			else if (event.event.case === 'friendship' && event.event.value.ownerUserId === account) navNotify.notify('friend-log', page.url.pathname);
		})
	);
	$effect(() => navNotify.remove(page.url.pathname.slice(1)));
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);

	// The original's nav menu: New Dashboard on top, each dashboard an entry with Edit and Delete.
	async function createDashboard() {
		const dashboard = dashboards.create('Dashboard');
		await goto(`/dashboard/${dashboard.id}`);
		dashboards.editingId = dashboard.id;
	}

	async function editDashboard(dashboard: Dashboard) {
		if (page.url.pathname !== `/dashboard/${dashboard.id}`) await goto(`/dashboard/${dashboard.id}`);
		dashboards.editingId = dashboard.id;
	}

	function deleteDashboard(dashboard: Dashboard) {
		confirming = {
			get title() { return m.nav_menu_delete_dashboard(); },
			get description() { return m.dashboard_confirmations_delete_description(); },
			destructive: true,
			run: async () => {
				dashboards.remove(dashboard.id);
				if (page.url.pathname === `/dashboard/${dashboard.id}`) await goto('/feed', { replaceState: true });
			}
		};
	}

	// Best-effort: failing to reach the server shouldn't block clearing the remembered account
	// and reopening the login gate.
	async function logOutOfVrchat() {
		const ownerUserId = vrchatAuth.ownerUserId;
		setOwnerUserId('');
		vrchatAuth.needsLogin = true;
		try {
			await api.delete(`/1/accounts/${encodeURIComponent(ownerUserId)}/session`);
		} catch {
			// Already logged out locally regardless.
		}
	}
</script>

{#snippet entryMenu(key?: string)}
	<ContextMenu.Content>
		{#if navNotify.menus.length}
			<ContextMenu.Item onclick={() => navNotify.clear()}>Mark All Read</ContextMenu.Item>
			<ContextMenu.Separator />
		{/if}
		{#if key && isDashboardNavKey(key)}
			{@const dashboard = dashboards.get(key.slice(DASHBOARD_NAV_KEY_PREFIX.length))}
			{#if dashboard}
				<ContextMenu.Item onclick={() => editDashboard(dashboard)}>{m.nav_menu_edit_dashboard()}</ContextMenu.Item>
				<ContextMenu.Item variant="destructive" onclick={() => deleteDashboard(dashboard)}>{m.nav_menu_delete_dashboard()}</ContextMenu.Item>
				<ContextMenu.Separator />
			{/if}
		{:else if key && isToolNavKey(key)}
			<ContextMenu.Item onclick={() => unpinTool(key)}>Unpin from Nav</ContextMenu.Item>
			<ContextMenu.Separator />
		{:else if !key}
			<ContextMenu.Item onclick={createDashboard}>{m.dashboard_new_dashboard()}</ContextMenu.Item>
		{/if}
		<ContextMenu.Item onclick={() => (navLayout.editing = true)}>Custom Nav</ContextMenu.Item>
	</ContextMenu.Content>
{/snippet}

{#snippet entryIcon(icon: string, notified: boolean)}
	<i class={['relative', icon]} aria-hidden="true">
		{#if notified}<span class="absolute -top-0.5 -right-1 size-1.5 rounded-full bg-red-500"></span>{/if}
	</i>
{/snippet}

{#snippet navEntry(def: NavDefinition)}
	<Sidebar.MenuItem>
		<ContextMenu.Root>
			<ContextMenu.Trigger>
				{#snippet child({ props: menuProps })}
					{#if def.action === 'direct-access'}
						<!-- The original's Direct Access entry, always last, with its shortcut hint. -->
						<Sidebar.MenuButton {...menuProps} tooltipContent={def.label} onclick={() => directAccess.paste()}>
							<i class={def.icon} aria-hidden="true"></i>
							<span>{def.label}</span>
							<span class="ml-auto inline-flex items-center gap-1 group-data-[collapsible=icon]:hidden">
								{#each [isMac ? '⌘' : 'Ctrl', 'D'] as key (key)}
									<kbd class="pointer-events-none inline-flex h-5 min-w-5 items-center justify-center rounded-sm bg-muted px-1 font-sans text-xs font-medium text-muted-foreground">{key}</kbd>
								{/each}
							</span>
						</Sidebar.MenuButton>
					{:else}
						<Sidebar.MenuButton {...menuProps} isActive={isActive(def)} tooltipContent={def.label}>
							{#snippet child({ props })}
								<a href={def.href} {...props}>
									{@render entryIcon(def.icon, navNotify.has(def.key))}
									<span>{def.label}</span>
								</a>
							{/snippet}
						</Sidebar.MenuButton>
					{/if}
				{/snippet}
			</ContextMenu.Trigger>
			{@render entryMenu(def.key)}
		</ContextMenu.Root>
	</Sidebar.MenuItem>
{/snippet}

{#snippet navFolder(folder: NavFolderEntry)}
	{@const children = folder.items.map((key) => navLayout.byKey.get(key)).filter((d): d is NavDefinition => !!d && visible(d))}
	{@const active = children.some(isActive)}
	{@const notified = children.some((d) => navNotify.has(d.key))}
	{@const open = openFolders[folder.id] ?? active}
	{#if children.length}
		<Sidebar.MenuItem>
			{#if sidebar.state === 'collapsed' && !sidebar.isMobile}
				<!-- The original's collapsed folder: its entries in a menu beside the icon. -->
				<DropdownMenu.Root>
					<DropdownMenu.Trigger>
						{#snippet child({ props })}
							<Sidebar.MenuButton {...props} isActive={active} tooltipContent={folder.name}>
								{@render entryIcon(folder.icon, notified)}
								<span>{folder.name}</span>
							</Sidebar.MenuButton>
						{/snippet}
					</DropdownMenu.Trigger>
					<DropdownMenu.Content side="right" align="start" class="w-56">
						{#each children as def (def.key)}
							<DropdownMenu.Item onSelect={() => def.href && goto(def.href)}>
								{@render entryIcon(def.icon, navNotify.has(def.key))}
								<span>{def.label}</span>
							</DropdownMenu.Item>
						{/each}
					</DropdownMenu.Content>
				</DropdownMenu.Root>
			{:else}
				<ContextMenu.Root>
					<ContextMenu.Trigger>
						{#snippet child({ props })}
							<Sidebar.MenuButton {...props} isActive={active} tooltipContent={folder.name} onclick={() => (openFolders[folder.id] = !open)}>
								{@render entryIcon(folder.icon, notified)}
								<span>{folder.name}</span>
								<ChevronRight class={['ml-auto transition-transform', open && 'rotate-90']} />
							</Sidebar.MenuButton>
						{/snippet}
					</ContextMenu.Trigger>
					{@render entryMenu()}
				</ContextMenu.Root>
				{#if open}
					<Sidebar.MenuSub>
						{#each children as def (def.key)}
							<Sidebar.MenuSubItem>
								<ContextMenu.Root>
									<ContextMenu.Trigger>
										{#snippet child({ props: menuProps })}
											<Sidebar.MenuSubButton {...menuProps} isActive={isActive(def)}>
												{#snippet child({ props })}
													<a href={def.href} {...props}>
														{@render entryIcon(def.icon, navNotify.has(def.key))}
														<span>{def.label}</span>
													</a>
												{/snippet}
											</Sidebar.MenuSubButton>
										{/snippet}
									</ContextMenu.Trigger>
									{@render entryMenu(def.key)}
								</ContextMenu.Root>
							</Sidebar.MenuSubItem>
						{/each}
					</Sidebar.MenuSub>
				{/if}
			{/if}
		</Sidebar.MenuItem>
	{/if}
{/snippet}

<!-- Stops above the status bar, which spans the full width as in the original. -->
<Sidebar.Root collapsible="icon" class="bottom-[22px] h-auto">
	{#if dashboards.showNewButton}
		<Sidebar.Header class="px-2 py-2">
			<Sidebar.Menu>
				<Sidebar.MenuItem>
					<Sidebar.MenuButton tooltipContent="New Dashboard" class="border border-dashed border-primary/40 text-primary hover:bg-primary/10" onclick={createDashboard}>
						<Plus class="size-4" />
						<span>{m.dashboard_new_dashboard()}</span>
					</Sidebar.MenuButton>
				</Sidebar.MenuItem>
			</Sidebar.Menu>
		</Sidebar.Header>
	{/if}
	<Sidebar.Content class="pt-2">
		<!-- The original's nav background menu: Mark All Read, New Dashboard, Custom Nav. -->
		<ContextMenu.Root>
			<ContextMenu.Trigger class="flex min-h-full flex-col">
				<Sidebar.Group>
					<Sidebar.GroupContent>
						<Sidebar.Menu>
							{#each navLayout.entries as entry (entry.type === 'folder' ? entry.id : entry.key)}
								{#if entry.type === 'folder'}
									{@render navFolder(entry)}
								{:else}
									{@const def = navLayout.byKey.get(entry.key)}
									{#if def && visible(def)}{@render navEntry(def)}{/if}
								{/if}
							{/each}
						</Sidebar.Menu>
					</Sidebar.GroupContent>
				</Sidebar.Group>
				<div class="flex-1"></div>
			</ContextMenu.Trigger>
			{@render entryMenu()}
		</ContextMenu.Root>
	</Sidebar.Content>

	<Sidebar.Footer>
		<Sidebar.Menu>
			<!-- The original's Help & Support menu: the VRCX project's wiki, GitHub and Discord. -->
			<Sidebar.MenuItem>
				<DropdownMenu.Root>
					<DropdownMenu.Trigger>
						{#snippet child({ props })}
							<Sidebar.MenuButton {...props} tooltipContent="Help & Support">
								<i class="ri-question-line" aria-hidden="true"></i>
								<span>{m.nav_tooltip_help_support()}</span>
							</Sidebar.MenuButton>
						{/snippet}
					</DropdownMenu.Trigger>
					<DropdownMenu.Content side="right" align="start" class="w-56">
						<DropdownMenu.Label>{m.nav_menu_resources()}</DropdownMenu.Label>
						<DropdownMenu.Item>{#snippet child({ props })}<a {...props} href={SUPPORT_LINKS.wiki} target="_blank" rel="noopener noreferrer">{m.nav_menu_wiki()}</a>{/snippet}</DropdownMenu.Item>
						<DropdownMenu.Separator />
						<DropdownMenu.Label>{m.nav_menu_get_help()}</DropdownMenu.Label>
						<DropdownMenu.Item>{#snippet child({ props })}<a {...props} href={SUPPORT_LINKS.github} target="_blank" rel="noopener noreferrer">{m.dialog_change_log_github()}</a>{/snippet}</DropdownMenu.Item>
						<DropdownMenu.Item>{#snippet child({ props })}<a {...props} href={SUPPORT_LINKS.discord} target="_blank" rel="noopener noreferrer">{m.dialog_user_tags_discord()}</a>{/snippet}</DropdownMenu.Item>
					</DropdownMenu.Content>
				</DropdownMenu.Root>
			</Sidebar.MenuItem>
			<Sidebar.MenuItem>
				<DropdownMenu.Root>
					<DropdownMenu.Trigger>
						{#snippet child({ props })}
							<Sidebar.MenuButton {...props} isActive={page.url.pathname === '/settings'} tooltipContent={settingsItem.label}>
								<i class={settingsItem.icon} aria-hidden="true"></i>
								<span>{settingsItem.label}</span>
							</Sidebar.MenuButton>
						{/snippet}
					</DropdownMenu.Trigger>
					<DropdownMenu.Content side="right" align="start" class="w-54">
						<DropdownMenu.Item onSelect={() => goto('/settings')}>{m.view_login_settings()}</DropdownMenu.Item>
						<DropdownMenu.Sub>
							<DropdownMenu.SubTrigger>{m.view_settings_appearance_appearance_theme_mode()}</DropdownMenu.SubTrigger>
							<DropdownMenu.SubContent class="w-54">
								{#each THEME_MODES as mode (mode)}
									<DropdownMenu.CheckboxItem checked={appearance.themeMode.value === mode} onSelect={() => appearance.setThemeMode(mode)}>
										{THEME_CONFIG[mode].name}
									</DropdownMenu.CheckboxItem>
								{/each}
								<DropdownMenu.Separator />
								<DropdownMenu.Label class="px-2 py-2 font-normal">
									<div class="flex items-center justify-around">
										{#each THEME_COLORS as color (color.key)}
											<Tooltip.Root delayDuration={600}>
												<Tooltip.Trigger>
													{#snippet child({ props })}
														<button
															{...props}
															type="button"
															aria-pressed={appearance.themeColor.value === color.key}
															aria-label={color.label}
															class="h-3.5 w-3.5 shrink-0 cursor-pointer rounded-sm transition-transform hover:scale-125 {appearance.themeColor.value === color.key ? 'ring-1 ring-ring ring-offset-1 ring-offset-background' : ''}"
															style="background-color: {color.swatch}"
															onclick={() => (appearance.themeColor.value = color.key)}
														></button>
													{/snippet}
												</Tooltip.Trigger>
												<Tooltip.Content side="top">{color.label}</Tooltip.Content>
											</Tooltip.Root>
										{/each}
									</div>
								</DropdownMenu.Label>
							</DropdownMenu.SubContent>
						</DropdownMenu.Sub>
						<DropdownMenu.Sub>
							<DropdownMenu.SubTrigger>{m.view_settings_appearance_appearance_table_density()}</DropdownMenu.SubTrigger>
							<DropdownMenu.SubContent side="right" align="start" class="w-54">
								<!-- The original's labels: standard spacing reads as Comfortable, the tighter one as Standard. -->
								{#each [['standard', 'Comfortable'], ['comfortable', 'Standard'], ['compact', 'Compact']] as const as [density, label] (density)}
									<DropdownMenu.CheckboxItem checked={appearance.tableDensity.value === density} onSelect={() => (appearance.tableDensity.value = density)}>{label}</DropdownMenu.CheckboxItem>
								{/each}
							</DropdownMenu.SubContent>
						</DropdownMenu.Sub>
						<DropdownMenu.Item onSelect={() => (navLayout.editing = true)}>Custom Nav</DropdownMenu.Item>
						{#if vrchatAuth.ownerUserId}
							<DropdownMenu.Separator />
							<DropdownMenu.Item variant="destructive" onSelect={logOutOfVrchat}>{m.dialog_user_actions_logout()}</DropdownMenu.Item>
						{/if}
					</DropdownMenu.Content>
				</DropdownMenu.Root>
			</Sidebar.MenuItem>
		</Sidebar.Menu>
	</Sidebar.Footer>
</Sidebar.Root>

<ActionDialogs bind:confirming />
<CustomNavDialog />
