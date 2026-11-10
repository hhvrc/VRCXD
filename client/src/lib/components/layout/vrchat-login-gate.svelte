<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { SUBTEXT_MUTED } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { onMount, untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import type { Account, LoginResponse } from '#lib/api/types.js';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import UserIcon from '@lucide/svelte/icons/user';
	import { serverAuth } from '#lib/api/server-auth.svelte.js';
	import { vrchatStatus } from '#lib/api/vrchat-status.svelte.js';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import QrCodeIcon from '@lucide/svelte/icons/qr-code';
	import QRCode from 'qrcode';
	import { setOwnerUserId, vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { LoginNetworkHint, TROUBLESHOOTING_URL } from '#lib/api/login-network-hint.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import SettingsIcon from '@lucide/svelte/icons/settings';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { askPrimaryPassword, loadPrimaryPassword, primaryPassword } from '#lib/api/primary-password.svelte.js';
	import { autoLoginCountdown, autoLoginToasts, noteSavedLoginFailure, savedAccounts, savedLoginProblemMessage } from '#lib/api/saved-logins.js';

	// Picks (or creates) the VRChat account this browser works as. On load, the remembered id
	// is checked against GET /1/accounts: still valid means no dialog; otherwise the dialog
	// offers the server's other valid accounts and a login form (POST /1/accounts/login, then
	// /login/2fa when VRChat asks for a code). As in the original, it can also log in with a QR
	// code another signed-in device approves, ask VRChat for a password recovery mail, and links to
	// VRChat's registration page. As the original's Save Credentials, the password can be kept
	// (encrypted on the server) so the server logs in again by itself when the session expires; an
	// account with a saved password stays in Saved Accounts after its session ends.

	let accounts = $state<Account[]>([]);
	// The original's Login Settings: a custom development endpoint for the next password login.
	let loginSettingsOpen = $state(false);
	let enableCustomEndpoint = $state(readSetting<boolean>('VRCX_enableCustomEndpoint', false) === true);
	let endpoint = $state('');
	let websocket = $state('');
	const loginBody = () => ({ username, password, ...(enableCustomEndpoint && (endpoint.trim() || websocket.trim()) ? { endpoint: endpoint.trim(), websocket: websocket.trim() } : {}) });
	function toggleCustomEndpoint(on: boolean) {
		enableCustomEndpoint = on;
		storeSetting('VRCX_enableCustomEndpoint', on);
		endpoint = '';
		websocket = '';
	}
	let username = $state('');
	let password = $state('');
	let code = $state('');
	let twoFactor = $state<{ pendingId: string; methods: string[]; method: string } | null>(null);
	let submitting = $state(false);
	let error = $state('');
	// The original's loginForm.saveCredentials, on by default.
	let saveCredentials = $state(true);
	// What the last form login asked to save, so resending the email code keeps it.
	let lastSave: { saveCredentials: boolean; primaryPassword?: string } = { saveCredentials: false };

	async function loadAccounts(): Promise<boolean> {
		try {
			accounts = await api.get<Account[]>('/1/accounts');
			return true;
		} catch {
			return false; // The token gate handles a dead server or a bad token.
		}
	}

	onMount(async () => {
		if (serverAuth.needsToken) return;
		if (!(await loadAccounts())) return;
		void loadPrimaryPassword();
		const valid = accounts.filter((a) => a.sessionValid);
		if (valid.some((a) => a.userId === vrchatAuth.ownerUserId)) return;
		if (valid.length === 1) {
			setOwnerUserId(valid[0].userId);
			return;
		}
		vrchatAuth.needsLogin = true;
	});

	// The original's QrLogin: VRChat's device code shown as a QR code, polled at VRChat's interval
	// until another device approves it.
	let qr = $state<{ pendingId: string; image: string } | null>(null);
	let qrSession = 0;

	async function startQr() {
		const session = ++qrSession;
		error = '';
		try {
			const device = await api.post<{ pendingId: string; verificationUrl: string; interval: number }>('/1/accounts/login/device');
			if (session !== qrSession) return void api.delete(`/1/accounts/login/device/${device.pendingId}`).catch(() => {});
			qr = { pendingId: device.pendingId, image: await QRCode.toDataURL(device.verificationUrl, { width: 184 }) };
			while (session === qrSession) {
				await new Promise((resolve) => setTimeout(resolve, (device.interval || 5) * 1000));
				if (session !== qrSession) return;
				const response = await api.post<LoginResponse>(`/1/accounts/login/device/${device.pendingId}`);
				if (session !== qrSession) return;
				if (response.status === 'loggedIn') {
					qrSession++;
					qr = null;
					applyResponse(response);
					return;
				}
			}
		} catch {
			if (session !== qrSession) return;
			qrSession++;
			qr = null;
			toast.error(m.view_login_qr_failed());
		}
	}

	// A login that finished another way, or a closed gate, ends the QR poll.
	$effect(() => {
		if (!vrchatAuth.needsLogin) untrack(stopQr);
		return () => untrack(stopQr);
	});

	function stopQr() {
		qrSession++;
		if (qr) api.delete(`/1/accounts/login/device/${qr.pendingId}`).catch(() => {});
		qr = null;
	}

	// The original's ResetPasswordDialog: prefilled when the username is an email address.
	let reset = $state({ open: false, email: '', sending: false });

	function openReset() {
		reset = { open: true, email: username.includes('@') ? username : '', sending: false };
	}

	async function sendReset(event: SubmitEvent) {
		event.preventDefault();
		if (!reset.email.trim() || reset.sending) return;
		reset.sending = true;
		try {
			await api.post('/1/accounts/login/password-reset', { email: reset.email.trim() });
			reset.open = false;
		} catch (err) {
			toastError(err);
		} finally {
			reset.sending = false;
		}
	}

	// The original's deleteSavedLogin: forgets the saved password and the session (its history stays).
	async function removeAccount(account: Account) {
		try {
			await api.delete(accountPath(account.userId, '/saved-login'));
			toast.success(m.message_auth_account_removed());
		} catch (err) {
			toastError(err);
		}
		await loadAccounts();
		void loadPrimaryPassword();
	}

	function pick(account: Account) {
		if (!account.sessionValid) return void relogin(account);
		setOwnerUserId(account.userId);
		vrchatAuth.needsLogin = false;
	}

	// The original's relogin: logs in with the saved password, asking for the primary password first
	// when it is encrypted with it.
	let reloginFrom: Account | null = null;

	async function relogin(account: Account) {
		reloginFrom = account;
		let primary: string | null = null;
		if (account.savedCredentialsNeedPrimaryPassword) {
			primary = await askPrimaryPassword();
			if (primary === null) return void toast.error(m.message_auth_incorrect_primary_password());
		}
		submitting = true;
		error = '';
		try {
			applyResponse(await api.post<LoginResponse>(accountPath(account.userId, '/relogin'), primary ? { primaryPassword: primary } : {}));
			if (vrchatAuth.needsLogin === false) resetNetworkHint();
		} catch (err) {
			const apiError = ApiError.from(err);
			const message = savedLoginProblemMessage(apiError.type);
			if (message) toast.error(message);
			else error = apiError.message;
			if (noteSavedLoginFailure(networkHint, apiError.status, apiError.problem?.detail ?? apiError.message)) showNetworkHint();
		} finally {
			submitting = false;
			await loadAccounts();
		}
	}

	// The server's auto login after a session expired (the original's runHandleAutoLoginFlow): its
	// countdown and outcome toasts, and the gate when the session is gone for good.
	let countdownToast: string | number | undefined;
	let countdownTimer: ReturnType<typeof setInterval> | undefined;

	function stopCountdown() {
		if (countdownTimer !== undefined) clearInterval(countdownTimer);
		countdownTimer = undefined;
		if (countdownToast !== undefined) toast.dismiss(countdownToast);
		countdownToast = undefined;
	}

	function startCountdown(seconds: number) {
		stopCountdown();
		const texts = autoLoginCountdown(seconds);
		let next = 0;
		const show = () => {
			if (next >= texts.length) return stopCountdown();
			countdownToast = toast.info(texts[next++], { id: countdownToast, duration: Infinity });
		};
		show();
		countdownTimer = setInterval(show, 1000);
	}

	onMount(() => {
		const unsubscribe = onLiveEvent((event) => {
			if (event.event.case !== 'account') return;
			const change = event.event.value;
			if (!change.accountId || change.accountId !== vrchatAuth.ownerUserId) return;
			if (change.autoLogin === 'attempting') {
				if (change.autoLoginDelay > 0) startCountdown(change.autoLoginDelay);
				return;
			}
			if (change.autoLogin) stopCountdown();
			for (const t of autoLoginToasts(change.autoLogin)) toast[t.kind](t.message);
			if (change.sessionValid) {
				if (change.autoLogin === 'success') vrchatAuth.needsLogin = false;
			} else {
				vrchatAuth.needsLogin = true;
			}
			void loadAccounts();
		});
		return () => {
			unsubscribe();
			stopCountdown();
		};
	});

	function applyResponse(response: LoginResponse) {
		if (response.status === 'loggedIn' && response.userId) {
			setOwnerUserId(response.userId);
			vrchatAuth.needsLogin = false;
			twoFactor = null;
			password = '';
			code = '';
		} else if (response.status === 'needsTwoFactor' && response.pendingId) {
			const methods = response.methods ?? ['totp'];
			twoFactor = { pendingId: response.pendingId, methods, method: methods.includes('emailOtp') ? 'emailOtp' : 'totp' };
			error = '';
		}
	}

	const networkHint = new LoginNetworkHint();
	let networkHintToast: string | number | undefined;

	function noteLoginFailure(err: ApiError) {
		if (LoginNetworkHint.counts(err.status, err.problem?.detail ?? err.message) && networkHint.failed()) showNetworkHint();
	}

	function showNetworkHint() {
		networkHintToast = toast.warning(m.message_auth_login_network_issue_hint_title(), {
			get description() { return m.message_auth_login_network_issue_hint_description(); },
			duration: Infinity,
			action: { get label() { return m.common_actions_open(); }, onClick: () => window.open(TROUBLESHOOTING_URL, '_blank', 'noopener,noreferrer') }
		});
	}

	function resetNetworkHint() {
		if (networkHintToast !== undefined) toast.dismiss(networkHintToast);
		networkHintToast = undefined;
		networkHint.reset();
	}

	async function submitLogin(event: SubmitEvent) {
		event.preventDefault();
		// As the original: saving with the primary password on encrypts the password with it.
		reloginFrom = null;
		lastSave = { saveCredentials };
		if (saveCredentials && primaryPassword.enabled) {
			const primary = await askPrimaryPassword();
			if (primary === null) return;
			lastSave.primaryPassword = primary;
		}
		submitting = true;
		error = '';
		// New credentials start the count again and drop a hint still showing, as the original.
		if (networkHint.attempt(username, password)) resetNetworkHint();
		try {
			applyResponse(await api.post<LoginResponse>('/1/accounts/login', { ...loginBody(), ...lastSave }));
			if (vrchatAuth.needsLogin === false) {
				resetNetworkHint();
				await loadAccounts();
			}
		} catch (err) {
			const apiError = ApiError.from(err);
			const message = savedLoginProblemMessage(apiError.type);
			if (message) toast.error(message);
			else error = apiError.message;
			noteLoginFailure(apiError);
		} finally {
			submitting = false;
		}
	}

	async function submitTwoFactor(event: SubmitEvent) {
		event.preventDefault();
		if (!twoFactor) return;
		submitting = true;
		error = '';
		try {
			applyResponse(
				await api.post<LoginResponse>('/1/accounts/login/2fa', {
					pendingId: twoFactor.pendingId,
					method: twoFactor.method,
					code: code.trim()
				})
			);
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			submitting = false;
		}
	}

	// The original's resendEmail2fa: logging in again from a fresh session makes VRChat send a new code.
	async function resendEmail() {
		// A saved account's login asks again with the saved password, as the original's resendEmail2fa.
		if (reloginFrom) {
			code = '';
			await relogin(reloginFrom);
			if (twoFactor) toast.success(m.message_auth_email_2fa_resent());
			return;
		}
		if (!password) {
			error = m.message_auth_email_2fa_no_credentials();
			twoFactor = null;
			return;
		}
		submitting = true;
		error = '';
		try {
			applyResponse(await api.post<LoginResponse>('/1/accounts/login', { ...loginBody(), ...lastSave }));
			code = '';
			toast.success('Email sent');
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			submitting = false;
		}
	}

	function useBackupCode() {
		if (!twoFactor) return;
		twoFactor = { ...twoFactor, method: 'otp' };
		code = '';
		error = '';
	}

	const validAccounts = $derived(savedAccounts(accounts));

	onMount(() => vrchatStatus.start());
</script>

<Dialog.Root open={vrchatAuth.needsLogin}>
	<Dialog.Content showCloseButton={false} interactOutsideBehavior="ignore" escapeKeydownBehavior="ignore">
		{#if !twoFactor}
			<!-- The original's login page warns about VRChat server issues (upstream issue 1396). -->
			{#if vrchatStatus.hasIssue}
				<button
					type="button"
					class="flex w-full cursor-pointer items-start gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-opacity hover:opacity-80 {vrchatStatus.isMajor ? 'border-destructive/50 text-destructive' : 'border-amber-500/50 text-amber-600 dark:text-amber-400'}"
					onclick={() => vrchatStatus.openPage()}
				>
					<TriangleAlert class="mt-0.5 size-4 shrink-0" />
					<span class="min-w-0">
						<span class="block truncate font-medium">{m.status_bar_servers_issue()}</span>
						<span class="block truncate text-xs opacity-90">{vrchatStatus.text}</span>
					</span>
				</button>
			{/if}
			<form class="flex flex-col gap-4" onsubmit={submitLogin}>
				<Dialog.Header>
					<Dialog.Title>Log in to VRChat</Dialog.Title>
					<Dialog.Description>
						The server keeps its own VRChat login to sync friends, notifications and presence.
					</Dialog.Description>
				</Dialog.Header>
				{#if validAccounts.length > 0}
					<div class="flex flex-col gap-2">
						<Label>{m.view_login_savedAccounts()}</Label>
						<div class="flex max-h-60 flex-col overflow-y-auto">
							{#each validAccounts as account (account.userId)}
								<div class="flex items-center gap-3 rounded-md p-2 hover:bg-muted">
									<button type="button" class="flex min-w-0 flex-1 cursor-pointer items-center gap-3 text-left" onclick={() => pick(account)}>
										<span class="relative size-10 shrink-0">
											<Avatar.Root class="size-full">
												<Avatar.Image src={imageSrc(account.thumbnailImageUrl)} loading="lazy" />
												<Avatar.Fallback><UserIcon class="size-5 text-muted-foreground" /></Avatar.Fallback>
											</Avatar.Root>
											<IconFrame iconFrame={account.iconFrame} />
										</span>
										<span class="min-w-0">
											<span class="block truncate text-sm font-medium">{account.displayName}</span>
											{#if account.username}<span class={SUBTEXT_MUTED}>{account.username}</span>{/if}
											{#if account.apiEndpoint}<span class={SUBTEXT_MUTED}>{account.apiEndpoint}</span>{/if}
										</span>
									</button>
									<Button type="button" size="icon-sm" variant="ghost" class="rounded-full" aria-label="Remove account" onclick={() => removeAccount(account)}><Trash2 /></Button>
								</div>
							{/each}
						</div>
					</div>
				{/if}
				{#if qr}
					<div class="flex flex-col items-center gap-2 text-center">
						<img src={qr.image} width="184" height="184" alt="QR code" />
						<p class="text-xs text-muted-foreground">{m.view_login_qr_hint()}</p>
					</div>
				{:else}
				<div class="flex flex-col gap-2">
					<Label for="vrchat-username">Username or email</Label>
					<Input id="vrchat-username" autocomplete="username" bind:value={username} required />
				</div>
				<div class="flex flex-col gap-2">
					<Label for="vrchat-password">{m.view_login_field_password()}</Label>
					<Input
						id="vrchat-password"
						type="password"
						autocomplete="current-password"
						bind:value={password}
						required
					/>
					{#if error}
						<p class="text-sm text-destructive">{error}</p>
					{/if}
				</div>
				{/if}
				<div class="flex items-center gap-2 text-sm">
					<label class="flex w-fit items-center gap-2">
						<Checkbox checked={saveCredentials} onCheckedChange={(v) => (saveCredentials = v === true)} />
						<span>{m.view_login_field_saveCredentials()}</span>
					</label>
				</div>
				<div class="flex items-center justify-between text-sm">
					<Button type="button" size="icon-sm" variant="ghost" class="rounded-full" title={m.view_login_settings()} aria-label={m.view_login_settings()} onclick={() => (loginSettingsOpen = true)}><SettingsIcon /></Button>
					<button type="button" class="cursor-pointer text-muted-foreground hover:text-foreground hover:underline" onclick={openReset}>{m.view_login_forgotPassword()}</button>
				</div>
				<div class="flex flex-col gap-2">
					{#if qr}
						<Button type="button" size="lg" variant="outline" class="w-full" onclick={stopQr}>{m.view_login_qr_cancel()}</Button>
					{:else}
						<Button type="submit" size="lg" class="w-full" disabled={submitting || !username.trim() || !password}>
							{submitting ? 'Logging in…' : 'Log in'}
						</Button>
						<Button type="button" variant="secondary" size="lg" class="w-full" onclick={startQr}><QrCodeIcon />{m.view_login_qr_button()}</Button>
					{/if}
					<Button type="button" variant="outline" size="lg" class="w-full" href="https://vrchat.com/home/register" target="_blank" rel="noopener noreferrer">{m.view_login_register()}</Button>
				</div>
			</form>
		{:else}
			<form class="flex flex-col gap-4" onsubmit={submitTwoFactor}>
				<Dialog.Header>
					<Dialog.Title>Two-factor authentication</Dialog.Title>
					<Dialog.Description>
						{#if twoFactor.method === 'emailOtp'}
							Enter the code VRChat emailed you.
						{:else if twoFactor.method === 'otp'}
							Enter one of your backup codes.
						{:else}
							Enter the code from your authenticator app.
						{/if}
					</Dialog.Description>
				</Dialog.Header>
				<div class="flex flex-col gap-2">
					<Label for="vrchat-2fa-code">Code</Label>
					<Input id="vrchat-2fa-code" autocomplete="one-time-code" bind:value={code} required />
					{#if error}
						<p class="text-sm text-destructive">{error}</p>
					{/if}
				</div>
				<Dialog.Footer class="flex items-center justify-between sm:justify-between">
					{#if twoFactor.method === 'totp'}
						<Button type="button" variant="link" class="px-0" onclick={useBackupCode}>
							Use a backup code
						</Button>
					{:else if twoFactor.method === 'emailOtp'}
						<Button type="button" variant="link" class="px-0" disabled={submitting} onclick={resendEmail}>
							{m.prompt_email_otp_resend()}
						</Button>
					{:else}
						<span></span>
					{/if}
					<Button type="submit" disabled={submitting || !code.trim()}>
						{submitting ? 'Verifying…' : 'Verify'}
					</Button>
				</Dialog.Footer>
			</form>
		{/if}
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={reset.open}>
	<Dialog.Content class="max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.view_login_reset_password_title()}</Dialog.Title>
			<Dialog.Description>{m.view_login_reset_password_description()}</Dialog.Description>
		</Dialog.Header>
		<form id="reset-password-form" class="flex flex-col gap-2" onsubmit={sendReset}>
			<Label for="reset-password-email">{m.view_login_reset_password_email()}</Label>
			<Input id="reset-password-email" type="email" autocomplete="email" name="email" placeholder={m.view_login_reset_password_email()} bind:value={reset.email} />
		</form>
		<Dialog.Footer>
			<Button variant="outline" onclick={() => (reset.open = false)}>{m.view_login_qr_cancel()}</Button>
			<Button type="submit" form="reset-password-form" disabled={!reset.email.trim() || reset.sending}>{m.view_login_reset_password_send()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={loginSettingsOpen}>
	<Dialog.Content class="max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.view_login_settings()}</Dialog.Title>
		</Dialog.Header>
		<div class="flex flex-col gap-3">
			<label class="inline-flex items-center gap-2 text-sm">
				<Checkbox checked={enableCustomEndpoint} onCheckedChange={(v) => toggleCustomEndpoint(v === true)} />
				<span>{m.view_login_field_devEndpoint()}</span>
			</label>
			{#if enableCustomEndpoint}
				<div class="flex flex-col gap-2">
					<Label for="login-settings-endpoint">{m.view_login_field_endpoint()}</Label>
					<Input id="login-settings-endpoint" autocomplete="off" name="endpoint" placeholder="https://api.vrchat.cloud/api/1" bind:value={endpoint} />
				</div>
				<div class="flex flex-col gap-2">
					<Label for="login-settings-websocket">{m.view_login_field_websocket()}</Label>
					<Input id="login-settings-websocket" autocomplete="off" name="websocket" placeholder="wss://pipeline.vrchat.cloud" bind:value={websocket} />
				</div>
			{/if}
		</div>
		<Dialog.Footer>
			<Button onclick={() => (loginSettingsOpen = false)}>{m.prompt_proxy_settings_close()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

