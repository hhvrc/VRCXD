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
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { recentActions } from '#lib/api/recent-actions.svelte.js';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { storeCheckSettings } from '#lib/content/store-checks.svelte.js';
	import OpenSourceNoticeDialog from '#lib/components/settings/open-source-notice-dialog.svelte';
	import VrchatConfigDialog from '#lib/components/tools/vrchat-config-dialog.svelte';
	import FeedFiltersDialog from '#lib/components/settings/feed-filters-dialog.svelte';
	import NotificationPositionDialog from '#lib/components/settings/notification-position-dialog.svelte';
	import { PHOTON_EVENT_TYPES } from '#lib/photon/photon.svelte.js';
	import { wristFeedSettings } from '#lib/notifications/wrist-feed.svelte.js';
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
	import ActionDialogs, { type Confirming, type Prompting } from '#lib/components/dialogs/action-dialogs.svelte';
	import { agentCan, desktop } from '#lib/instances/instance-launch.svelte.js';
	import { cropAllPrints, deleteAllScreenshotMetadata, openFolder, pickPath } from '#lib/agent/agent.js';
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
		{ value: 'vr', get label() { return m.view_settings_category_vr(); } },
		{ value: 'media', get label() { return m.view_settings_category_media(); } },
		{ value: 'integrations', get label() { return m.view_settings_integrations_header(); } },
		{ value: 'advanced', get label() { return m.side_panel_settings_advanced(); } }
	];
	let activeTab = $state('system');
	let ossOpen = $state(false);
	let vrchatConfigOpen = $state(false);
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
	// The VR tab's Show VR Notifications choices (the original's overlayToast).
	const VR_CONDITIONS = [
		{ value: 'Never', get label() { return m.view_settings_notifications_notifications_conditions_never(); } },
		{ value: 'Game Running', get label() { return m.view_settings_notifications_notifications_conditions_inside_vrchat(); } },
		{ value: 'Game Closed', get label() { return m.view_settings_notifications_notifications_conditions_outside_vrchat(); } },
		{ value: 'Always', get label() { return m.view_settings_notifications_notifications_conditions_always(); } }
	];
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
	let notificationPositionOpen = $state(false);
	const hudFilterLabel = (filter: string) =>
		filter === 'VIP' ? m.view_settings_advanced_photon_event_hud_filter_favorites() : filter === 'Friends' ? m.view_settings_advanced_photon_event_hud_filter_friends() : m.view_settings_advanced_photon_event_hud_filter_everyone();
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
	let confirming = $state<Confirming | undefined>();

	// The original's askDeleteAllScreenshotMetadata: asked twice, then done on the PC.
	function askDeleteAllScreenshotMetadata() {
		confirming = {
			description: m.view_settings_advanced_advanced_delete_all_screenshot_metadata_ask(),
			confirmText: m.view_settings_advanced_advanced_delete_all_screenshot_metadata_confirm_yes(),
			cancelText: m.view_settings_advanced_advanced_delete_all_screenshot_metadata_confirm_no(),
			run: () => {
				confirming = {
					description: m.view_settings_advanced_advanced_delete_all_screenshot_metadata_confirm(),
					confirmText: m.view_settings_advanced_advanced_save_instance_prints_to_file_crop_convert_old_confirm(),
					cancelText: m.view_settings_advanced_advanced_save_instance_prints_to_file_crop_convert_old_cancel(),
					destructive: true,
					run: async () => {
						confirming = undefined;
						const progress = toast.warning('Batch metadata removal in progress...', { duration: Infinity, position: 'bottom-right' });
						try {
							await deleteAllScreenshotMetadata();
							toast.success('Batch metadata removal complete');
						} catch (err) {
							toast.error(`Batch metadata removal failed: ${ApiError.from(err).message}`);
						} finally {
							toast.dismiss(progress);
						}
					}
				};
			}
		};
	}

	// The original's setCropInstancePrints and cropPrintsChanged: turning it on offers to crop the prints saved already.
	async function setCropInstancePrints(on: boolean) {
		await saveSettings({ cropInstancePrints: on });
		if (!on || !agentCan('save_media')) return;
		confirming = {
			title: '',
			description: m.view_settings_advanced_advanced_save_instance_prints_to_file_crop_convert_old(),
			confirmText: m.view_settings_advanced_advanced_save_instance_prints_to_file_crop_convert_old_confirm(),
			cancelText: m.view_settings_advanced_advanced_save_instance_prints_to_file_crop_convert_old_cancel(),
			run: async () => {
				confirming = undefined;
				const progress = toast.warning('Batch print cropping in progress...', { duration: Infinity, position: 'bottom-right' });
				try {
					await cropAllPrints();
					toast.success('Batch print cropping complete');
				} catch (err) {
					toast.error(`Batch print cropping failed: ${ApiError.from(err).message}`);
				} finally {
					toast.dismiss(progress);
				}
			}
		};
	}

	// The original's openUGCFolderSelector: the PC's folder picker, kept as the account's override.
	async function setUgcFolder() {
		try {
			const path = await pickPath('ugc_folder');
			if (path) await saveSettings({ ugcFolderPath: path });
		} catch (err) {
			toastError(err);
		}
	}

	const openOnPc = (folder: Parameters<typeof openFolder>[0]) => openFolder(folder).catch(toastError);
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
<VrchatConfigDialog bind:open={vrchatConfigOpen} />
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
		<TabsPrimitive.Content value="vr" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				{#if !agentCan('notifications')}<p class={SUBTEXT_MUTED}>XSOverlay and OVR Toolkit notifications need the VRCXD agent on your PC.</p>{/if}
				<SettingsGroup title={m.view_settings_vr_vr_core_header()}>
					{#if !agentCan('openvr')}<p class={SUBTEXT_MUTED}>The SteamVR overlay needs the VRCXD agent on your PC.</p>{/if}
					{#if settings}
						<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_steamvr_overlay()} toggle>
							<Switch checked={settings.openVr} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_steamvr_overlay()} onCheckedChange={(v) => saveSettings({ openVr: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_start_overlay_with()}>
							<Select.Root type="single" value={settings.openVrAlways ? 'true' : 'false'} disabled={!settings.openVr} onValueChange={(v) => saveSettings({ openVrAlways: v === 'true' })}>
								<Select.Trigger size="sm">{settings.openVrAlways ? 'SteamVR' : 'VRChat'}</Select.Trigger>
								<Select.Content>
									<Select.Item value="false" label="VRChat" />
									<Select.Item value="true" label="SteamVR" />
								</Select.Content>
							</Select.Root>
						</SettingsItem>
					{/if}
				{#if desktop.os !== 'linux'}
						<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_xsoverlay_notifications()} toggle>
							<Switch checked={notySettings.xsNotifications} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_xsoverlay_notifications()} onCheckedChange={(v) => notySettings.setOverlay('xsNotifications', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_ovrtoolkit_hud_notifications()} toggle>
							<Switch checked={notySettings.ovrtHudNotifications} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_ovrtoolkit_hud_notifications()} onCheckedChange={(v) => notySettings.setOverlay('ovrtHudNotifications', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_ovrtoolkit_wrist_notifications()} toggle>
							<Switch checked={notySettings.ovrtWristNotifications} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_ovrtoolkit_wrist_notifications()} onCheckedChange={(v) => notySettings.setOverlay('ovrtWristNotifications', v)} />
						</SettingsItem>
				{/if}
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_vr_vr_notifications_header()}>
					<SettingsItem label={m.view_settings_notifications_notifications_desktop_notifications_when_to_display_vr()}>
						<Select.Root type="single" value={notySettings.overlayToast} disabled={(!notySettings.overlayNotifications || !settings?.openVr) && !notySettings.xsNotifications && !notySettings.ovrtHudNotifications && !notySettings.ovrtWristNotifications} onValueChange={(v) => notySettings.setOverlayToast(v)}>
							<Select.Trigger size="sm">{VR_CONDITIONS.find((c) => c.value === notySettings.overlayToast)?.label ?? notySettings.overlayToast}</Select.Trigger>
							<Select.Content>
								{#each VR_CONDITIONS as c (c.value)}<Select.Item value={c.value} label={c.label} />{/each}
							</Select.Content>
						</Select.Root>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_overlay_notifications()} toggle>
						<Switch checked={notySettings.overlayNotifications} disabled={!settings?.openVr} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_overlay_notifications()} onCheckedChange={(v) => notySettings.setOverlay('overlayNotifications', v)} />
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_notification_position()}>
						<Button size="sm" variant="outline" disabled={!notySettings.overlayNotifications || !settings?.openVr} onclick={() => (notificationPositionOpen = true)}>{m.view_settings_notifications_notifications_steamvr_notifications_notification_position()}</Button>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_notification_opacity()}>
						<div class="w-75 max-w-full pt-1">
							<Slider type="single" value={notySettings.notificationOpacity} min={0} max={100} step={1} onValueChange={(v) => notySettings.setNotificationOpacity(v)} />
						</div>
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_notification_timeout()}>
						<Input type="number" class="h-8 w-32" min={0} step={1} disabled={!notySettings.xsNotifications} value={notySettings.notificationTimeout / 1000} onchange={(e) => notySettings.setNotificationTimeout(Number(e.currentTarget.value) || 0)} />
					</SettingsItem>
					<SettingsItem label={m.view_settings_notifications_notifications_steamvr_notifications_user_images()} description={m.view_settings_notifications_notifications_steamvr_notifications_user_images_description()} toggle>
						<Switch checked={notySettings.imageNotifications} aria-label={m.view_settings_notifications_notifications_steamvr_notifications_user_images()} onCheckedChange={(v) => notySettings.setOverlay('imageNotifications', v)} />
					</SettingsItem>
				</SettingsGroup>
				<SettingsGroup title={m.view_settings_wrist_overlay_steamvr_wrist_overlay_header()}>
					<p class={SUBTEXT_MUTED}>{m.view_settings_wrist_overlay_steamvr_wrist_overlay_description()}</p>
					{#if settings}
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_wrist_feed_overlay()} toggle>
							<Switch checked={settings.overlayWrist} disabled={!settings.openVr} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_wrist_feed_overlay()} onCheckedChange={(v) => saveSettings({ overlayWrist: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_overlay_button()}>
							<Select.Root type="single" value={settings.overlayButton ? 'menu' : 'grip'} disabled={!settings.openVr || !settings.overlayWrist} onValueChange={(v) => saveSettings({ overlayButton: v === 'menu' })}>
								<Select.Trigger size="sm">{settings.overlayButton ? m.view_settings_wrist_overlay_steamvr_wrist_overlay_overlay_button_menu() : m.view_settings_wrist_overlay_steamvr_wrist_overlay_overlay_button_grip()}</Select.Trigger>
								<Select.Content>
									<Select.Item value="grip" label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_overlay_button_grip()} />
									<Select.Item value="menu" label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_overlay_button_menu()} />
								</Select.Content>
							</Select.Root>
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on()}>
							<Select.Root type="single" value={String(settings.overlayHand)} disabled={!settings.openVr || !settings.overlayWrist} onValueChange={(v) => saveSettings({ overlayHand: Number(v) })}>
								<Select.Trigger size="sm">{[m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_both(), m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_left(), m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_right()][settings.overlayHand] ?? ''}</Select.Trigger>
								<Select.Content>
									<Select.Item value="1" label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_left()} />
									<Select.Item value="2" label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_right()} />
									<Select.Item value="0" label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_display_overlay_on_both()} />
								</Select.Content>
							</Select.Root>
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_hide_private_worlds()} toggle>
							<Switch checked={wristFeedSettings.hidePrivateFromFeed} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_hide_private_worlds()} onCheckedChange={(v) => wristFeedSettings.set('hidePrivateFromFeed', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_grey_background()} toggle>
							<Switch checked={wristFeedSettings.vrBackgroundEnabled} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_grey_background()} onCheckedChange={(v) => wristFeedSettings.set('vrBackgroundEnabled', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_vr_devices()} toggle>
							<Switch checked={!wristFeedSettings.hideDevicesFromFeed} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_vr_devices()} onCheckedChange={(v) => wristFeedSettings.set('hideDevicesFromFeed', !v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_minimal_feed_icons()} toggle>
							<Switch checked={wristFeedSettings.minimalFeed} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_minimal_feed_icons()} onCheckedChange={(v) => wristFeedSettings.set('minimalFeed', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_cpu_usage()} toggle>
							<Switch checked={wristFeedSettings.vrOverlayCpuUsage} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_cpu_usage()} onCheckedChange={(v) => wristFeedSettings.set('vrOverlayCpuUsage', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_game_uptime()} toggle>
							<Switch checked={!wristFeedSettings.hideUptimeFromFeed} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_game_uptime()} onCheckedChange={(v) => wristFeedSettings.set('hideUptimeFromFeed', !v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_pc_uptime()} toggle>
							<Switch checked={wristFeedSettings.pcUptimeOnFeed} disabled={!settings.openVr || !settings.overlayWrist} aria-label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_show_pc_uptime()} onCheckedChange={(v) => wristFeedSettings.set('pcUptimeOnFeed', v)} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_wrist_overlay_steamvr_wrist_overlay_wrist_feed_filters()}>
							<Button size="sm" variant="outline" disabled={!settings.openVr || !settings.overlayWrist} onclick={() => (feedFiltersMode = 'wrist')}>{m.view_settings_wrist_overlay_steamvr_wrist_overlay_wrist_feed_filters()}</Button>
						</SettingsItem>
					{/if}
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
						<SettingsItem label={m.view_settings_advanced_advanced_delete_all_screenshot_metadata_button()}>
							<Button size="sm" variant="outline" disabled={!agentCan('screenshots')} title={agentCan('screenshots') ? undefined : 'Needs the agent on the PC'} onclick={askDeleteAllScreenshotMetadata}>{m.view_settings_advanced_advanced_delete_all_screenshot_metadata_button()}</Button>
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_pictures_pictures_auto_delete_old_prints()}>
						<SettingsItem label={m.view_settings_pictures_pictures_auto_delete_prints_from_vrc()} description="The server deletes the oldest prints that aren't favorites when the account connects and when a new print is made, keeping room for two." toggle>
							<Switch checked={settings.autoDeleteOldPrints} aria-label={m.view_settings_pictures_pictures_auto_delete_prints_from_vrc()} onCheckedChange={(v) => saveSettings({ autoDeleteOldPrints: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_advanced_user_generated_content_header()}>
						{#snippet description()}{m.view_settings_advanced_advanced_user_generated_content_description()}{/snippet}
						<div class="flex flex-wrap gap-2">
							<Button size="sm" variant="outline" disabled={!agentCan('folders')} onclick={() => openOnPc('ugc_photos')}>{m.view_settings_advanced_advanced_user_generated_content_folder()}</Button>
							<Button size="sm" variant="outline" disabled={!agentCan('pick_path')} onclick={setUgcFolder}>{m.view_settings_advanced_advanced_user_generated_content_set_folder()}</Button>
							{#if settings.ugcFolderPath}
								<Button size="sm" variant="outline" onclick={() => saveSettings({ ugcFolderPath: '' })}>{m.view_settings_advanced_advanced_user_generated_content_reset_override()}</Button>
							{/if}
						</div>
						{#if settings.ugcFolderPath}<p class={SUBTEXT_MUTED}>{settings.ugcFolderPath}</p>{/if}
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_advanced_save_instance_prints_to_file_header()}>
						{#snippet description()}{m.view_settings_advanced_advanced_save_instance_prints_to_file_header_tooltip()}{/snippet}
						<SettingsItem label={m.view_settings_advanced_advanced_save_instance_prints_to_file_description()} toggle>
							<Switch checked={settings.saveInstancePrints} aria-label={m.view_settings_advanced_advanced_save_instance_prints_to_file_description()} onCheckedChange={(v) => saveSettings({ saveInstancePrints: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_save_instance_prints_to_file_crop()} toggle>
							<Switch checked={settings.cropInstancePrints} aria-label={m.view_settings_advanced_advanced_save_instance_prints_to_file_crop()} onCheckedChange={setCropInstancePrints} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_advanced_save_instance_stickers_to_file_header()}>
						<SettingsItem label={m.view_settings_advanced_advanced_save_instance_stickers_to_file_description()} toggle>
							<Switch checked={settings.saveInstanceStickers} aria-label={m.view_settings_advanced_advanced_save_instance_stickers_to_file_description()} onCheckedChange={(v) => saveSettings({ saveInstanceStickers: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_advanced_save_instance_emoji_to_file_header()}>
						{#snippet description()}{m.view_settings_advanced_advanced_save_instance_prints_to_file_header_tooltip()}{/snippet}
						<SettingsItem label={m.view_settings_advanced_advanced_save_instance_emoji_to_file_description()} toggle>
							<Switch checked={settings.saveInstanceEmoji} aria-label={m.view_settings_advanced_advanced_save_instance_emoji_to_file_description()} onCheckedChange={(v) => saveSettings({ saveInstanceEmoji: v })} />
						</SettingsItem>
					</SettingsGroup>
				{/if}
			</div>
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="integrations" class="pt-2 outline-none">
			<div class="flex flex-col gap-10 py-2">
				{#if settings}
					<SettingsGroup title={m.view_settings_discord_presence_discord_presence_header()}>
						{#snippet description()}
							<p class="m-0">{m.view_settings_discord_presence_discord_presence_description()}</p>
							<button type="button" class="m-0 cursor-pointer text-left transition-colors hover:text-foreground" onclick={() => (vrchatConfigOpen = true)}>{m.view_settings_discord_presence_discord_presence_enable_tooltip()}</button>
						{/snippet}
						{#if !agentCan('discord')}
							<p class={SUBTEXT_MUTED}>Discord Rich Presence needs the VRCXD agent on your PC, with Discord running there.</p>
						{/if}
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_enable()} toggle>
								<Switch checked={settings.discordActive} disabled={false} aria-label={m.view_settings_discord_presence_discord_presence_enable()} onCheckedChange={(v) => saveSettings({ discordActive: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_world_integration()} description={m.view_settings_discord_presence_discord_presence_world_integration_tooltip()} toggle>
								<Switch checked={settings.discordWorldIntegration} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_world_integration()} onCheckedChange={(v) => saveSettings({ discordWorldIntegration: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_instance_type_player_count()} toggle>
								<Switch checked={settings.discordInstance} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_instance_type_player_count()} onCheckedChange={(v) => saveSettings({ discordInstance: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_show_current_platform()} toggle>
								<Switch checked={settings.discordShowPlatform} disabled={!settings.discordActive || !settings.discordInstance} aria-label={m.view_settings_discord_presence_discord_presence_show_current_platform()} onCheckedChange={(v) => saveSettings({ discordShowPlatform: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_show_details_in_private()} description={m.view_settings_discord_presence_discord_presence_show_details_in_private_tooltip()} toggle>
								<Switch checked={!settings.discordHideInvite} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_show_details_in_private()} onCheckedChange={(v) => saveSettings({ discordHideInvite: !v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_join_button()} toggle>
								<Switch checked={settings.discordJoinButton} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_join_button()} onCheckedChange={(v) => saveSettings({ discordJoinButton: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_show_images()} toggle>
								<Switch checked={!settings.discordHideImage} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_show_images()} onCheckedChange={(v) => saveSettings({ discordHideImage: !v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_show_media_images()} description={m.view_settings_discord_presence_discord_presence_show_media_images_tooltip()} toggle>
								<Switch checked={settings.discordMediaImage} disabled={!settings.discordActive || settings.discordHideImage} aria-label={m.view_settings_discord_presence_discord_presence_show_media_images()} onCheckedChange={(v) => saveSettings({ discordMediaImage: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_discord_presence_discord_presence_display_world_name_as_discord_status()} description={m.view_settings_discord_presence_discord_presence_display_world_name_as_discord_status_tooltip()} toggle>
								<Switch checked={settings.discordWorldNameAsDiscordStatus} disabled={!settings.discordActive} aria-label={m.view_settings_discord_presence_discord_presence_display_world_name_as_discord_status()} onCheckedChange={(v) => saveSettings({ discordWorldNameAsDiscordStatus: v })} />
							</SettingsItem>
					</SettingsGroup>
				{/if}
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
						<SettingsItem label={m.view_settings_advanced_advanced_relaunch_vrchat_header()} description={m.view_settings_advanced_advanced_relaunch_vrchat_description()} toggle>
							<Switch checked={settings.relaunchVrchatAfterCrash} aria-label={m.view_settings_advanced_advanced_relaunch_vrchat_header()} onCheckedChange={(v) => saveSettings({ relaunchVrchatAfterCrash: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_vrchat_quit_fix_header()} description={m.view_settings_advanced_advanced_vrchat_quit_fix_description()} toggle>
							<Switch checked={settings.vrcQuitFix} aria-label={m.view_settings_advanced_advanced_vrchat_quit_fix_header()} onCheckedChange={(v) => saveSettings({ vrcQuitFix: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_auto_cache_management_header()} description={m.view_settings_advanced_advanced_auto_cache_management_description()} toggle>
							<Switch checked={settings.autoSweepVrchatCache} aria-label={m.view_settings_advanced_advanced_auto_cache_management_header()} onCheckedChange={(v) => saveSettings({ autoSweepVrchatCache: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_self_invite_header()} description={m.view_settings_advanced_advanced_self_invite_description()} toggle>
							<Switch checked={settings.selfInviteOverride} aria-label={m.view_settings_advanced_advanced_self_invite_header()} onCheckedChange={(v) => saveSettings({ selfInviteOverride: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_general_logging_resource_load()} toggle>
							<Switch checked={settings.logResourceLoad} aria-label={m.view_settings_general_logging_resource_load()} onCheckedChange={(v) => saveSettings({ logResourceLoad: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_cache_debug_udon_exception_logging()} description="Written to the server's log" toggle>
							<Switch checked={settings.udonExceptionLogging} aria-label={m.view_settings_advanced_advanced_cache_debug_udon_exception_logging()} onCheckedChange={(v) => saveSettings({ udonExceptionLogging: v })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_photon_header()}>
						{#if !agentCan('openvr')}<p class={SUBTEXT_MUTED}>The Photon HUDs are drawn by the VRCXD agent's SteamVR overlay.</p>{/if}
						<SettingsItem label={m.view_settings_advanced_photon_event_hud_enable()} description={m.view_settings_advanced_photon_event_hud_enable_tooltip()} toggle>
							<Switch checked={settings.photonEventOverlay} disabled={!settings.openVr} aria-label={m.view_settings_advanced_photon_event_hud_enable()} onCheckedChange={(v) => saveSettings({ photonEventOverlay: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_event_hud_filter()}>
							<Select.Root type="single" value={settings.photonEventOverlayFilter} disabled={!settings.openVr || !settings.photonEventOverlay} onValueChange={(v) => saveSettings({ photonEventOverlayFilter: v })}>
								<Select.Trigger size="sm">{hudFilterLabel(settings.photonEventOverlayFilter)}</Select.Trigger>
								<Select.Content>
									<Select.Item value="VIP" label={m.view_settings_advanced_photon_event_hud_filter_favorites()} />
									<Select.Item value="Friends" label={m.view_settings_advanced_photon_event_hud_filter_friends()} />
									<Select.Item value="Everyone" label={m.view_settings_advanced_photon_event_hud_filter_everyone()} />
								</Select.Content>
							</Select.Root>
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_event_hud_message_timeout()}>
							<Input type="number" class="h-8 w-32" min={0} step={1} disabled={!settings.openVr} value={settings.photonOverlayMessageTimeout / 1000} onchange={(e) => saveSettings({ photonOverlayMessageTimeout: Math.max(0, Math.round(Number(e.currentTarget.value) * 1000)) || 6000 })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_event_hud_type_filter_placeholder()}>
							<MultiSelect class="w-55" values={settings.photonEventTypeOverlayFilter} options={PHOTON_EVENT_TYPES.map((type) => ({ value: type, label: type }))} placeholder={m.view_settings_advanced_photon_event_hud_type_filter_placeholder()} onchange={(types) => saveSettings({ photonEventTypeOverlayFilter: types })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_timeout_hud_enable()} description={m.view_settings_advanced_photon_timeout_hud_enable_tooltip()} toggle>
							<Switch checked={settings.timeoutHudOverlay} disabled={!settings.openVr} aria-label={m.view_settings_advanced_photon_timeout_hud_enable()} onCheckedChange={(v) => saveSettings({ timeoutHudOverlay: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_timeout_hud_filter()}>
							<Select.Root type="single" value={settings.timeoutHudOverlayFilter} disabled={!settings.openVr || !settings.timeoutHudOverlay} onValueChange={(v) => saveSettings({ timeoutHudOverlayFilter: v })}>
								<Select.Trigger size="sm">{hudFilterLabel(settings.timeoutHudOverlayFilter)}</Select.Trigger>
								<Select.Content>
									<Select.Item value="VIP" label={m.view_settings_advanced_photon_event_hud_filter_favorites()} />
									<Select.Item value="Friends" label={m.view_settings_advanced_photon_event_hud_filter_friends()} />
									<Select.Item value="Everyone" label={m.view_settings_advanced_photon_event_hud_filter_everyone()} />
								</Select.Content>
							</Select.Root>
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_photon_timeout_hud_timeout_threshold()}>
							<Input type="number" class="h-8 w-32" min={1} step={1} disabled={!settings.openVr} value={settings.photonLobbyTimeoutThreshold / 1000} onchange={(e) => saveSettings({ photonLobbyTimeoutThreshold: Math.max(1000, Math.round(Number(e.currentTarget.value) * 1000)) || 6000 })} />
						</SettingsItem>
					</SettingsGroup>
					<SettingsGroup title={m.view_settings_advanced_advanced_video_progress_pie_header()}>
						<SettingsItem label={m.view_settings_advanced_advanced_video_progress_pie_enable()} description={m.view_settings_advanced_advanced_video_progress_pie_enable_tooltip()} toggle>
							<Switch checked={settings.progressPie} disabled={!settings.openVr} aria-label={m.view_settings_advanced_advanced_video_progress_pie_enable()} onCheckedChange={(v) => saveSettings({ progressPie: v })} />
						</SettingsItem>
						<SettingsItem label={m.view_settings_advanced_advanced_video_progress_pie_dance_world_only()} toggle>
							<Switch checked={settings.progressPieFilter} disabled={!settings.openVr || !settings.progressPie} aria-label={m.view_settings_advanced_advanced_video_progress_pie_dance_world_only()} onCheckedChange={(v) => saveSettings({ progressPieFilter: v })} />
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
					{#if desktop.os !== 'linux'}
						<SettingsGroup title={m.view_settings_advanced_advanced_app_launcher_header()}>
							<SettingsItem label={m.view_settings_advanced_advanced_app_launcher_folder()}>
								<Button size="sm" variant="outline" disabled={!agentCan('folders')} onclick={() => openOnPc('shortcuts')}>{m.view_settings_advanced_advanced_app_launcher_folder()}</Button>
							</SettingsItem>
							<SettingsItem label={m.view_settings_advanced_advanced_remote_database_enable()} description={m.view_settings_advanced_advanced_app_launcher_folder_tooltip()} toggle>
								<Switch checked={settings.enableAppLauncher} aria-label={m.view_settings_advanced_advanced_remote_database_enable()} onCheckedChange={(v) => saveSettings({ enableAppLauncher: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_advanced_advanced_app_launcher_auto_close()} toggle>
								<Switch checked={settings.enableAppLauncherAutoClose} aria-label={m.view_settings_advanced_advanced_app_launcher_auto_close()} onCheckedChange={(v) => saveSettings({ enableAppLauncherAutoClose: v })} />
							</SettingsItem>
							<SettingsItem label={m.view_settings_advanced_advanced_app_launcher_run_process_once()} toggle>
								<Switch checked={settings.enableAppLauncherRunProcessOnce} aria-label={m.view_settings_advanced_advanced_app_launcher_run_process_once()} onCheckedChange={(v) => saveSettings({ enableAppLauncherRunProcessOnce: v })} />
							</SettingsItem>
						</SettingsGroup>
					{/if}
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

<NotificationPositionDialog bind:open={notificationPositionOpen} />
<FeedFiltersDialog bind:mode={feedFiltersMode} />
<ActionDialogs bind:prompting bind:confirming />
<TranslationApiDialog bind:open={translationOpen} />
