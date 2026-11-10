<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { onMount, untrack } from 'svelte';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import InterfaceTab from '#lib/components/settings/interface-tab.svelte';
	import { TAB_TRIGGER, SUBTEXT_MUTED } from '#lib/details.js';
	import SettingsGroup from '#lib/components/settings/settings-group.svelte';
	import SettingsItem from '#lib/components/settings/settings-item.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { recentActions } from '#lib/api/recent-actions.svelte.js';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { storeCheckSettings } from '#lib/content/store-checks.svelte.js';
	import OpenSourceNoticeDialog from '#lib/components/settings/open-source-notice-dialog.svelte';
	import FeedFiltersDialog from '#lib/components/settings/feed-filters-dialog.svelte';
	import TranslationApiDialog from '#lib/components/settings/translation-api-dialog.svelte';
	import DatabaseCleanup from '#lib/components/settings/database-cleanup.svelte';
	import { errorReporting } from '#lib/settings/error-reporting.svelte.js';
	import Languages from '@lucide/svelte/icons/languages';
	import { translation } from '#lib/settings/translation.svelte.js';
	import Play from '@lucide/svelte/icons/play';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { DESKTOP_CONDITIONS, TTS_CONDITIONS } from '#lib/notifications/noty.js';
	import { notySettings, testNotification } from '#lib/notifications/noty.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { SWITCH_AVATAR_CONFIRM_KEY } from '#lib/instances/launch-command.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { accountPath, api, ApiError, setServerToken } from '#lib/api/client.js';
	import { formatDate } from '#lib/helpers/format.js';
	import type { Account, AccountSettings, Favorites } from '#lib/api/types.js';
	import { live } from '#lib/api/live.svelte.js';
	import { serverAuth } from '#lib/api/server-auth.svelte.js';
	import { setOwnerUserId, vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import ActionDialogs, { type Prompting } from '#lib/components/dialogs/action-dialogs.svelte';
	import { loadPrimaryPassword, primaryPassword, togglePrimaryPassword } from '#lib/api/primary-password.svelte.js';
	import { AUTO_LOGIN_DELAY_PATTERN, clampAutoLoginDelay } from '#lib/api/saved-logins.js';

	// The server's accounts (GET /1/accounts) with their sync state, a "pull everything now"
	// button per account, the original's app settings for this browser's account
	// (GET/PUT /1/accounts/{id}/settings: screenshot helper, YouTube API, quit fix, resource
	// loads), and the browser's own connection settings.

	// The original's Settings tabs, in its order; VR is desktop only, and so is the Notifications
	// tab's SteamVR group.
	const SETTINGS_TABS = [
		{ value: 'system', get label() { return m.view_settings_category_system(); } },
		{ value: 'interface', get label() { return m.view_settings_category_interface(); } },
		{ value: 'social', get label() { return m.view_settings_category_social(); } },
		{ value: 'notifications', get label() { return m.view_settings_category_notifications(); } },
		{ value: 'media', get label() { return m.view_settings_category_media(); } },
		{ value: 'integrations', get label() { return m.view_settings_integrations_header(); } },
		{ value: 'advanced', get label() { return m.side_panel_settings_advanced(); } }
	];
	let activeTab = $state('system');
	let ossOpen = $state(false);
	// The Notifications tab: the filters dialog, the TTS test line, and whether the browser lets notifications show.
	let feedFiltersMode = $state<'' | 'noty' | 'wrist'>('');
	let translationOpen = $state(false);
	// The Advanced tab's Database group: the original's table sizes, from the server, on Refresh.
	const TABLE_SIZE_ROWS = [
		['gps', 'GPS:'],
		['status', 'Status:'],
		['bio', 'Bio:'],
		['avatar', 'Avatar:'],
		['onlineOffline', 'Online/Offline:'],
		['friendLogHistory', 'Friend Log History:'],
		['notification', 'Notifications:'],
		['location', 'Location:'],
		['joinLeave', 'Join/Leave:'],
		['portalSpawn', 'Portal Spawn:'],
		['videoPlay', 'Video Play:'],
		['event', 'Event:']
	] as const;
	let tableSizes = $state<Record<string, number> | null>(null);
	let tableSizesLoading = $state(false);
	async function loadTableSizes() {
		tableSizesLoading = true;
		try {
			tableSizes = await api.get<Record<string, number>>('/1/database/table-sizes');
		} catch (err) {
			toastError(err);
		} finally {
			tableSizesLoading = false;
		}
	}
	let ttsTestVisible = $state(false);
	let ttsTest = $state('');
	let notificationPermission = $state(typeof Notification === 'undefined' ? 'default' : Notification.permission);
	const readPermission = () => {
		if (typeof Notification !== 'undefined') notificationPermission = Notification.permission;
	};
	const conditionLabel = (value: string) => DESKTOP_CONDITIONS.find((c) => c.value === value)?.label ?? value;
	// The original's showConfirmationOnSwitchAvatar, per browser.
	let switchAvatarConfirm = $state(readSetting(SWITCH_AVATAR_CONFIRM_KEY, false));

	let accounts = $state<Account[]>([]);
	let loadError = $state('');
	let busy = $state('');
	let actionError = $state('');

	async function load() {
		try {
			accounts = await api.get<Account[]>('/1/accounts');
			loadError = '';
		} catch (err) {
			loadError = ApiError.from(err).message;
		}
	}

	let settings = $state<AccountSettings | null>(null);
	// The original's Online Users row (upstream issue 122): VRChat's online user and active instance counts.
	let onlineUsers = $state<{ users: number; instances?: number | null } | null>(null);
	let onlineUsersError = $state('');

	async function refreshOnlineUsers() {
		if (!vrchatAuth.ownerUserId) return;
		onlineUsersError = '';
		try {
			onlineUsers = await api.get<{ users: number; instances?: number | null }>(ownPath('/online-users'));
		} catch (err) {
			onlineUsersError = ApiError.from(err).message;
		}
	}
	let settingsError = $state('');
	let youtubeKeyDraft = $state('');
	let testingYoutubeKey = $state(false);

	/** The original's YouTubeApiDialog: a blank key is removed, any other is saved only when YouTube accepts it (upstream PR 1450). */
	async function testYouTubeKey() {
		if (!settings || !vrchatAuth.ownerUserId) return;
		const key = youtubeKeyDraft.trim();
		if (!key) {
			await saveSettings({ youtubeApiKey: '' });
			toast.success('YouTube API key removed');
			return;
		}
		testingYoutubeKey = true;
		try {
			const result = await api.post<{ valid: boolean }>(ownPath('/settings/youtube-key-test'), { apiKey: key });
			if (result.valid) {
				await saveSettings({ youtubeApiKey: key });
				toast.success('YouTube API key valid!');
			} else {
				youtubeKeyDraft = settings.youtubeApiKey;
				toast.error('Invalid YouTube API key');
			}
		} catch (err) {
			toastError(err);
		} finally {
			testingYoutubeKey = false;
		}
	}

	// The Social tab's favorite groups picker: VRChat's friend groups, then the local ones.
	let friendGroupOptions = $state<{ value: string; label: string }[]>([]);
	async function loadFriendGroups(owner: string) {
		try {
			const [remote, local] = await Promise.all([
				api.get<Favorites>(accountPath(owner, '/favorites'), { type: 'Friend' }),
				api.get<{ name: string }[]>(accountPath(owner, '/favorites/local/groups'), { type: 'Friend' })
			]);
			friendGroupOptions = [
				...remote.groups.filter((g) => g.type === 'friend').map((g) => ({ value: `friend:${g.name}`, label: g.displayName || g.name })),
				...local.map((g) => ({ value: `local:${g.name}`, label: g.name }))
			];
		} catch {
			friendGroupOptions = [];
		}
	}

	async function loadSettings() {
		if (!vrchatAuth.ownerUserId) return;
		void loadFriendGroups(vrchatAuth.ownerUserId);
		try {
			settings = await api.get<AccountSettings>(ownPath('/settings'));
			youtubeKeyDraft = settings.youtubeApiKey;
			settingsError = '';
		} catch (err) {
			settingsError = ApiError.from(err).message;
		}
	}

	async function saveSettings(patch: Partial<AccountSettings>) {
		if (!vrchatAuth.ownerUserId || !settings) return;
		const next = { ...settings, ...patch };
		settings = next;
		try {
			settings = await api.put<AccountSettings>(ownPath('/settings'), next);
			settingsError = '';
		} catch (err) {
			settingsError = ApiError.from(err).message;
		}
	}

	// The original's promptAutoLoginDelaySeconds.
	let prompting = $state<Prompting | undefined>();
	function promptAutoLoginDelaySeconds() {
		if (!settings) return;
		prompting = {
			title: m.prompt_auto_login_delay_header(),
			description: m.prompt_auto_login_delay_description(),
			value: String(settings.autoLoginDelaySeconds),
			numeric: true,
			allowEmpty: true,
			errorMessage: m.prompt_auto_login_delay_input_error(),
			run: async (value) => {
				if (!AUTO_LOGIN_DELAY_PATTERN.test(value)) return void toast.error(m.prompt_auto_login_delay_input_error());
				prompting = undefined;
				await saveSettings({ autoLoginDelaySeconds: clampAutoLoginDelay(value) });
			}
		};
	}

	onMount(load);
	onMount(loadPrimaryPassword);
	$effect(() => {
		void vrchatAuth.ownerUserId;
		untrack(() => loadSettings());
	});

	async function sync(account: Account) {
		busy = account.userId;
		actionError = '';
		try {
			await api.post(accountPath(account.userId, '/sync'));
			await load();
		} catch (err) {
			actionError = ApiError.from(err).message;
		} finally {
			busy = '';
		}
	}

	async function logout(account: Account) {
		busy = account.userId;
		actionError = '';
		try {
			await api.delete(accountPath(account.userId, '/session'));
			if (vrchatAuth.ownerUserId === account.userId) {
				setOwnerUserId('');
				vrchatAuth.needsLogin = true;
			}
			await load();
		} catch (err) {
			actionError = ApiError.from(err).message;
		} finally {
			busy = '';
		}
	}

	function forgetToken() {
		setServerToken('');
		serverAuth.needsToken = true;
	}
</script>

<svelte:head>
	<title>Settings · VRCXD</title>
</svelte:head>

<div class="mx-auto flex w-full max-w-2xl flex-col">
	<div class="shrink-0 p-1.5">
		<span class="text-lg font-semibold text-foreground">{m.view_login_settings()}</span>
	</div>

	<TabsPrimitive.Root bind:value={activeTab} class="flex min-h-0 w-full min-w-0 flex-col">
		<div class="relative flex w-full min-w-0 shrink-0 items-center border-b border-border">
			<TabsPrimitive.List class="relative flex w-full items-center gap-1 overflow-x-auto">
				{#each SETTINGS_TABS as item (item.value)}
					<TabsPrimitive.Trigger value={item.value} class={TAB_TRIGGER}>{item.label}</TabsPrimitive.Trigger>
				{/each}
			</TabsPrimitive.List>
		</div>
		<TabsPrimitive.Content value="system" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				<SettingsGroup title="VRChat accounts">
					{#snippet description()}
						Accounts the server is logged into. Friend lists sync every 5 minutes, everything else hourly.
					{/snippet}
					{#if loadError}
						<p class="text-sm text-destructive">{loadError}</p>
					{:else if accounts.length === 0}
						<p class="text-sm text-muted-foreground">No accounts yet. Log in from the dialog or the sidebar.</p>
					{:else}
						{#if actionError}
							<p class="text-sm text-destructive">{actionError}</p>
						{/if}
						<div class="flex flex-col gap-2">
							{#each accounts as account (account.userId)}
								<div class="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border p-2">
									<div>
										<div class="flex items-center gap-2 text-sm font-medium text-foreground">
											{account.displayName}
											{#if account.userId === vrchatAuth.ownerUserId}
												<Badge variant="default">This browser</Badge>
											{:else}
												<Button variant="link" size="sm" class="h-auto p-0" onclick={() => setOwnerUserId(account.userId)}>Use here</Button>
											{/if}
											<Badge variant={account.sessionValid ? 'outline' : 'destructive'}>
												{account.sessionValid ? 'Session OK' : 'Logged out'}
											</Badge>
										</div>
										<div class="text-xs text-muted-foreground">
											Friends synced {account.lastFriendSyncAt ? formatDate(account.lastFriendSyncAt) : 'never'} · full sync
											{account.lastFullSyncAt ? formatDate(account.lastFullSyncAt) : 'never'}
										</div>
									</div>
									<div class="flex gap-2">
										<Button variant="outline" size="sm" disabled={busy === account.userId || !account.sessionValid} onclick={() => sync(account)}>
											{busy === account.userId ? 'Working…' : 'Sync now'}
										</Button>
										<Button variant="ghost" size="sm" disabled={busy === account.userId || !account.sessionValid} onclick={() => logout(account)}>
											Log out
										</Button>
									</div>
								</div>
							{/each}
						</div>
					{/if}
				</SettingsGroup>
				<!-- The original's System tab: General (version, repository, support), Contributors and
				     Legal Notice. Its updater and application (startup, tray, GPU, proxy) settings are the
				     desktop app's. -->
				<SettingsGroup title={m.view_settings_category_general()}>
					<div class="flex flex-col gap-0.5 px-1 py-1">
						<span class="block truncate text-sm leading-4.5 font-medium">{m.dialog_world_info_version()}</span>
						<span class={SUBTEXT_MUTED}>{live.serverVersion ? `VRCXD server ${live.serverVersion}` : 'Not connected'}</span>
					</div>
					{#each [{ get label() { return m.view_settings_general_general_repository_url(); }, url: 'https://github.com/vrcx-team/VRCX' }, { get label() { return m.view_settings_general_general_support(); }, url: 'https://vrcx.app/discord' }] as link (link.url)}
						<a class="flex flex-col gap-0.5 px-1 py-1" href={link.url} target="_blank" rel="noopener noreferrer">
							<span class="block truncate text-sm leading-4.5 font-medium">{link.label}</span>
							<span class={SUBTEXT_MUTED}>{link.url}</span>
						</a>
					{/each}
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_general_contributors_header()}>
					<a href="https://github.com/vrcx-team/VRCX/graphs/contributors" target="_blank" rel="noopener noreferrer">
						<img src="https://contrib.rocks/image?repo=vrcx-team/VRCX" alt="Contributors" />
					</a>
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_general_legal_notice_header()}>
					<div class="mb-2 flex flex-col gap-2 text-sm text-muted-foreground">
						<p class="m-0">
							&copy; 2019-2026
							<a href="https://github.com/pypy-vrc" target="_blank" rel="noopener noreferrer">pypy</a> &amp;
							<a href="https://github.com/Natsumi-sama" target="_blank" rel="noopener noreferrer">Natsumi</a> &amp;
							<a href="https://github.com/Map1en" target="_blank" rel="noopener noreferrer">Map1en</a>
						</p>
						<p class="m-0">{m.view_settings_general_legal_notice_info()}</p>
						<p class="m-0">{m.view_settings_general_legal_notice_disclaimer1()}</p>
						<p class="m-0">{m.view_settings_general_legal_notice_disclaimer2()}</p>
					</div>
					<SettingsItem label={m.dialog_open_source_header()}>
						<Button size="sm" variant="outline" onclick={() => (ossOpen = true)}>{m.dialog_open_source_header()}</Button>
					</SettingsItem>
				</SettingsGroup>
<OpenSourceNoticeDialog bind:open={ossOpen} />
				<SettingsGroup title="This browser">
					<SettingsItem label="Live connection" description={live.connected ? `Connected to server ${live.serverVersion}` : 'Not connected'}>
						<span class="inline-block size-2 rounded-full {live.connected ? 'bg-status-online' : 'bg-status-offline'}"></span>
					</SettingsItem>
					<SettingsItem label="Server API token" description="Forget the stored token and enter a new one.">
						<Button variant="outline" size="sm" onclick={forgetToken}>{m.prompt_change_favorite_group_name_change()}</Button>
					</SettingsItem>
				</SettingsGroup>
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="interface" class="pt-2 outline-none">
			<InterfaceTab />
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="social" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				{#if settings}
					<SettingsGroup title={m.view_settings_social_interaction_header()}>
						<SettingsItem label={m.view_settings_appearance_user_dialog_recent_action_cooldown()} description={m.view_settings_appearance_user_dialog_recent_action_cooldown_description()} toggle>
							<Switch checked={recentActions.enabled} aria-label={m.view_settings_appearance_user_dialog_recent_action_cooldown()} onCheckedChange={(v) => (recentActions.enabled = v)} />
						</SettingsItem>
						{#if recentActions.enabled}
							<SettingsItem label={m.view_settings_appearance_user_dialog_recent_action_cooldown_minutes()}>
								<Input type="number" min="1" max="1440" step="1" class="w-32" value={recentActions.minutes} onchange={(e) => (recentActions.minutes = Number(e.currentTarget.value))} />
							</SettingsItem>
						{/if}
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_social_friend_requests_header()}>
						{#if settingsError}
							<p class="text-sm text-destructive">{settingsError}</p>
						{/if}
						<SettingsItem label={m.view_settings_general_friend_requests_header()} description={m.view_settings_general_friend_requests_header_tooltip()} toggle>
							<Switch checked={settings.autoDeclineFriendRequests} aria-label={m.view_settings_general_friend_requests_header()} onCheckedChange={(v) => saveSettings({ autoDeclineFriendRequests: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.dialog_world_info_favorites()}>
						<SettingsItem label={m.view_settings_general_favorites_header()} description={m.view_settings_general_favorites_header_tooltip()}>
							<MultiSelect class="w-48" values={settings.favoriteFriendGroups} options={friendGroupOptions} placeholder={m.view_settings_general_favorites_group_placeholder()} onchange={(v) => saveSettings({ favoriteFriendGroups: v })} />
						</SettingsItem>
					</SettingsGroup>
				{/if}
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="notifications" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				<SettingsGroup title={m.view_settings_category_notifications()}>
					{#snippet description()}<span class={sidebarData.me?.status === 'busy' ? 'text-destructive' : ''}>{m.view_settings_notifications_notifications_busy_warning()}</span>{/snippet}
					<SettingsItem label={m.dialog_shared_feed_filters_notification()}>
						<Button size="sm" variant="outline" onclick={() => (feedFiltersMode = 'noty')}>{m.dialog_shared_feed_filters_notification()}</Button>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_test_notification()}>
						<Button size="sm" variant="outline" onclick={testNotification}><Play />{m.view_settings_notifications_notifications_test_notification()}</Button>
					</SettingsItem>
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_notifications_notifications_desktop_notifications_header()}>
					{#if notificationPermission === 'denied'}<p class="text-sm text-destructive">This browser blocks notifications from VRCXD; allow them in the site settings.</p>{/if}
					<SettingsItem label={m.view_settings_notifications_notifications_desktop_notifications_when_to_display()}>
						<Select.Root type="single" value={notySettings.desktopToast} onValueChange={(v) => { notySettings.setDesktopToast(v); setTimeout(readPermission, 1500); }}>
							<Select.Trigger size="sm">{conditionLabel(notySettings.desktopToast)}</Select.Trigger>
							<Select.Content>
								{#each DESKTOP_CONDITIONS as c (c.value)}<Select.Item value={c.value} label={c.label} />{/each}
							</Select.Content>
						</Select.Root>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_desktop_notifications_desktop_notification_while_afk()} toggle>
						<Switch checked={notySettings.afkDesktopToast} aria-label={m.view_settings_notifications_notifications_desktop_notifications_desktop_notification_while_afk()} onCheckedChange={(v) => notySettings.setAfkDesktopToast(v)} />
					</SettingsItem>
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_notifications_notifications_text_to_speech_header()}>
					<SettingsItem label={m.view_settings_notifications_notifications_text_to_speech_when_to_play()}>
						<Select.Root type="single" value={notySettings.notificationTTS} onValueChange={(v) => notySettings.setNotificationTTS(v)}>
							<Select.Trigger size="sm">{conditionLabel(notySettings.notificationTTS)}</Select.Trigger>
							<Select.Content>
								{#each TTS_CONDITIONS as c (c.value)}<Select.Item value={c.value} label={c.label} />{/each}
							</Select.Content>
						</Select.Root>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_text_to_speech_tts_voice()}>
						<Select.Root type="single" value={String(notySettings.ttsVoice)} disabled={notySettings.notificationTTS === 'Never'} onValueChange={(v) => notySettings.setVoice(Number(v))}>
							<Select.Trigger size="sm" class="max-w-64"><span class="truncate">{notySettings.voices[notySettings.ttsVoice]?.name ?? notySettings.voices[0]?.name ?? ''}</span></Select.Trigger>
							<Select.Content>
								{#each notySettings.voices as voice, i (i)}<Select.Item value={String(i)} label={voice.name} />{/each}
							</Select.Content>
						</Select.Root>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_text_to_speech_use_memo_nicknames()} toggle>
						<Switch checked={notySettings.notificationTTSNickName} disabled={notySettings.notificationTTS === 'Never'} aria-label={m.view_settings_notifications_notifications_text_to_speech_use_memo_nicknames()} onCheckedChange={(v) => notySettings.setNickName(v)} />
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_text_to_speech_tts_test_placeholder()} toggle>
						<Switch checked={ttsTestVisible} aria-label={m.view_settings_notifications_notifications_text_to_speech_tts_test_placeholder()} onCheckedChange={(v) => (ttsTestVisible = v)} />
					</SettingsItem>
					{#if ttsTestVisible}
						<div class="mt-1 flex items-center gap-2">
							<Textarea bind:value={ttsTest} placeholder={m.view_settings_notifications_notifications_text_to_speech_tts_test_placeholder()} rows={1} class="min-h-0 w-44 resize-none" />
							<Button size="sm" variant="outline" onclick={() => { speechSynthesis.cancel(); notySettings.speak(ttsTest); }}>{m.view_settings_notifications_notifications_text_to_speech_play()}</Button>
						</div>
					{/if}
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_notifications_notifications_inventory_notifications_header()}>
					<SettingsItem label={m.view_settings_notifications_notifications_inventory_notifications_inventory_bundle_notifications()} description={m.view_settings_notifications_notifications_inventory_notifications_inventory_bundle_notifications_description()} toggle>
						<Switch checked={storeCheckSettings.inventoryBundles} aria-label={m.view_settings_notifications_notifications_inventory_notifications_inventory_bundle_notifications()} onCheckedChange={(v) => storeCheckSettings.setInventoryBundles(v)} />
					</SettingsItem>
					<SettingsItem label={m.dialog_free_store_item_dialog_header()} description={m.view_settings_notifications_notifications_inventory_notifications_free_store_item_notifications_description()} toggle>
						<Switch checked={storeCheckSettings.freeStoreItems} aria-label={m.dialog_free_store_item_dialog_header()} onCheckedChange={(v) => storeCheckSettings.setFreeStoreItems(v)} />
					</SettingsItem>
				</SettingsGroup>
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="media" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				{#if settings}
					<SettingsGroup title={m.view_settings_advanced_advanced_screenshot_helper_header()}>
						{#snippet description()}Embed world and instance data into VRChat screenshots as PNG metadata.{/snippet}
						{#if settingsError}
							<p class="text-sm text-destructive">{settingsError}</p>
						{/if}
						<SettingsItem label={m.view_settings_advanced_advanced_screenshot_helper_enable()} description={m.view_settings_advanced_advanced_screenshot_helper_description_tooltip()} toggle>
							<Switch checked={settings.screenshotHelper} aria-label={m.view_settings_advanced_advanced_screenshot_helper_enable()} onCheckedChange={(v) => saveSettings({ screenshotHelper: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_screenshot_helper_modify_filename()} description={m.view_settings_advanced_advanced_screenshot_helper_modify_filename_tooltip()} toggle>
							<Switch checked={settings.screenshotHelperModifyFilename} disabled={!settings.screenshotHelper} aria-label={m.view_settings_advanced_advanced_screenshot_helper_modify_filename()} onCheckedChange={(v) => saveSettings({ screenshotHelperModifyFilename: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_screenshot_helper_copy_to_clipboard()} toggle>
							<Switch checked={settings.screenshotHelperCopyToClipboard} aria-label={m.view_settings_advanced_advanced_screenshot_helper_copy_to_clipboard()} onCheckedChange={(v) => saveSettings({ screenshotHelperCopyToClipboard: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_pictures_pictures_auto_delete_old_prints()}>
						<SettingsItem label={m.view_settings_pictures_pictures_auto_delete_prints_from_vrc()} description="The server deletes the oldest prints that aren't favorites when the account connects and when a new print is made, keeping room for two." toggle>
							<Switch checked={settings.autoDeleteOldPrints} aria-label={m.view_settings_pictures_pictures_auto_delete_prints_from_vrc()} onCheckedChange={(v) => saveSettings({ autoDeleteOldPrints: v })} />
						</SettingsItem>
					</SettingsGroup>
				{/if}
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="integrations" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				<SettingsGroup title={m.view_settings_advanced_advanced_translation_api_header()}>
					<SettingsItem label={m.view_settings_advanced_photon_event_hud_enable()} description={m.view_settings_advanced_advanced_translation_api_enable_tooltip()} toggle>
						<Switch checked={translation.enabled} aria-label={m.view_settings_advanced_photon_event_hud_enable()} onCheckedChange={(v) => translation.setEnabled(v)} />
					</SettingsItem>
					<SettingsItem label={m.view_settings_advanced_advanced_translation_api_translation_api_key()}>
						<Button size="sm" variant="outline" onclick={() => (translationOpen = true)}><Languages class="mr-1.5 h-4 w-4" />{m.view_settings_advanced_advanced_translation_api_translation_api_key()}</Button>
					</SettingsItem>
				</SettingsGroup>
				{#if settings}
					<SettingsGroup title={m.dialog_youtube_api_header()}>
						<SettingsItem label={m.view_settings_advanced_photon_event_hud_enable()} description={m.view_settings_advanced_advanced_youtube_api_enable_tooltip()} toggle>
							<Switch checked={settings.youtubeApi} aria-label={m.view_settings_advanced_photon_event_hud_enable()} onCheckedChange={(v) => saveSettings({ youtubeApi: v })} />
						</SettingsItem>
						<SettingsItem label={m.dialog_youtube_api_placeholder()} description={m.dialog_youtube_api_description()}>
							<div class="flex items-center gap-2">
								<Button size="sm" variant="ghost" href="https://smashballoon.com/doc/youtube-api-key/" target="_blank" rel="noopener noreferrer">{m.dialog_youtube_api_guide()}</Button>
								<Input type="password" class="h-8 w-64" maxlength={39} bind:value={youtubeKeyDraft} placeholder={m.dialog_youtube_api_placeholder()} onkeydown={(e) => e.key === 'Enter' && testYouTubeKey()} />
								<Button size="sm" variant="outline" disabled={testingYoutubeKey} onclick={testYouTubeKey}>{testingYoutubeKey ? 'Testing...' : 'Test & Save'}</Button>
							</div>
						</SettingsItem>
					</SettingsGroup>
				{/if}
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="advanced" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				{#if settings}
					<SettingsGroup title={m.view_settings_category_advanced()}>
						<SettingsItem label={m.view_settings_advanced_advanced_vrchat_quit_fix_header()} description={m.view_settings_advanced_advanced_vrchat_quit_fix_description()} toggle>
							<Switch checked={settings.vrcQuitFix} aria-label={m.view_settings_advanced_advanced_vrchat_quit_fix_header()} onCheckedChange={(v) => saveSettings({ vrcQuitFix: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_general_logging_resource_load()} toggle>
							<Switch checked={settings.logResourceLoad} aria-label={m.view_settings_general_logging_resource_load()} onCheckedChange={(v) => saveSettings({ logResourceLoad: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_cache_debug_udon_exception_logging()} description="Written to the server's log" toggle>
							<Switch checked={settings.udonExceptionLogging} aria-label={m.view_settings_advanced_advanced_cache_debug_udon_exception_logging()} onCheckedChange={(v) => saveSettings({ udonExceptionLogging: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_groups_security_header()}>
						<SettingsItem label={m.view_settings_advanced_advanced_primary_password_header()} description={m.view_settings_advanced_advanced_primary_password_description()} toggle>
							<Switch checked={primaryPassword.enabled} aria-label={m.view_settings_advanced_advanced_primary_password_header()} onCheckedChange={(v) => togglePrimaryPassword(v)} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_general_logging_header()}>
						<SettingsItem label={m.view_settings_general_logging_auto_login_delay()} toggle>
							<Switch checked={settings.autoLoginDelayEnabled} aria-label={m.view_settings_general_logging_auto_login_delay()} onCheckedChange={(v) => saveSettings({ autoLoginDelayEnabled: v })} />
						</SettingsItem>
						{#if settings.autoLoginDelayEnabled}
							<SettingsItem label={m.view_settings_general_logging_auto_login_delay_button()}>
								<Button size="sm" variant="outline" onclick={promptAutoLoginDelaySeconds}>{m.view_settings_general_logging_auto_login_delay_button()}</Button>
							</SettingsItem>
						{/if}
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_groups_database_header()}>
						{#snippet description()}Rows the server holds for every account, counted as the original's SQLite Table Size.{/snippet}
						<SettingsItem label={m.dialog_note_export_refresh()}>
							<Button size="sm" variant="outline" disabled={tableSizesLoading} onclick={loadTableSizes}>{m.dialog_note_export_refresh()}</Button>
						</SettingsItem>
						<div class="flex flex-col gap-1 text-sm">
							{#each TABLE_SIZE_ROWS as [key, label] (key)}
								<span>{label} <span>{(tableSizes?.[key] ?? 0).toLocaleString()}</span></span>
							{/each}
						</div>
					</SettingsGroup>
					<DatabaseCleanup value={settings.avatarAutoCleanup ?? 'Off'} onchange={(v) => saveSettings({ avatarAutoCleanup: v })} />
					<SettingsGroup title={m.view_settings_advanced_advanced_launch_commands_header()}>
						<SettingsItem label={m.view_settings_advanced_advanced_launch_commands_show_confirmation_on_switch_avatar_enable()} description={m.view_settings_advanced_advanced_launch_commands_show_confirmation_on_switch_avatar_tooltip()} toggle>
							<Switch checked={switchAvatarConfirm} aria-label={m.view_settings_advanced_advanced_launch_commands_show_confirmation_on_switch_avatar_enable()} onCheckedChange={(v) => { switchAvatarConfirm = v; storeSetting(SWITCH_AVATAR_CONFIRM_KEY, v); }} />
						</SettingsItem>
						<div class="flex flex-wrap gap-2 px-1">
							<Button size="sm" variant="outline" href="https://github.com/vrcx-team/VRCX/wiki/Launch-parameters-&-VRCX.json" target="_blank" rel="noopener noreferrer">{m.view_settings_advanced_advanced_launch_commands_docs()}</Button>
							<Button size="sm" variant="outline" href="https://github.com/Myrkie/open-in-vrcx" target="_blank" rel="noopener noreferrer">{m.view_settings_advanced_advanced_launch_commands_website_userscript()}</Button>
						</div>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_groups_diagnostics_header()}>
						<SettingsItem label={m.view_profile_game_info_online_users()}>
							<div class="flex items-center gap-2">
								{#if onlineUsers}
									<span class="text-sm text-muted-foreground">{onlineUsers.users} users online.</span>
									{#if onlineUsers.instances != null}<span class="text-sm text-muted-foreground">{onlineUsers.instances} active instances.</span>{/if}
								{/if}
								{#if onlineUsersError}<span class="text-sm text-destructive">{onlineUsersError}</span>{/if}
								<Button size="sm" variant="outline" disabled={!vrchatAuth.ownerUserId} onclick={refreshOnlineUsers}>{m.dialog_note_export_refresh()}</Button>
							</div>
						</SettingsItem>
					</SettingsGroup>
				{/if}
				{#if errorReporting.available}
					<SettingsGroup title={m.view_settings_advanced_groups_nightly_header()}>
						<SettingsItem label={m.view_settings_advanced_advanced_anonymous_error_reporting_header()} description={m.view_settings_advanced_advanced_anonymous_error_reporting_description()} toggle>
							<Switch bind:checked={() => errorReporting.enabled, () => void errorReporting.toggle()} aria-label={m.view_settings_advanced_advanced_anonymous_error_reporting_header()} />
						</SettingsItem>
					</SettingsGroup>
				{/if}
			</div>
		</TabsPrimitive.Content>
	</TabsPrimitive.Root>
</div>

<FeedFiltersDialog bind:mode={feedFiltersMode} />
<ActionDialogs bind:prompting />
<TranslationApiDialog bind:open={translationOpen} />
