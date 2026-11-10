import { m } from '#lib/paraglide/messages.js';
import type { Account } from '#lib/api/types.js';
import { LoginNetworkHint } from '#lib/api/login-network-hint.js';

// The original's saved credentials (stores/auth.js) and auto-login delay (stores/settings/general.js).
// The server keeps the passwords, encrypted; the browser only learns whether one is saved.

/** The original's prompt pattern for the auto-login delay: a whole number from 0 to 10. */
export const AUTO_LOGIN_DELAY_PATTERN = /^(10|[0-9])$/;

/** The original's setAutoLoginDelaySeconds: not a number is 0, and it stays within 0 to 10. */
export function clampAutoLoginDelay(value: unknown): number {
	const parsed = parseInt(String(value), 10);
	return Number.isNaN(parsed) ? 0 : Math.min(10, Math.max(0, parsed));
}

/** The original's primary password prompt takes 1 to 32 characters. */
export const PRIMARY_PASSWORD_MAX_LENGTH = 32;
export const isPrimaryPassword = (value: string): boolean => value.length >= 1 && value.length <= PRIMARY_PASSWORD_MAX_LENGTH;

/** The login page's Saved Accounts: a live session, or a saved password to log in with. */
export const savedAccounts = (accounts: Account[]): Account[] => accounts.filter((a) => a.sessionValid || a.hasSavedCredentials);

/** What the server's auto login said, as the original's toasts. */
export type AutoLoginToast = { kind: 'success' | 'error'; message: string };

export function autoLoginToasts(state: string): AutoLoginToast[] {
	switch (state) {
		case 'success':
			return [{ kind: 'success', message: m.message_auth_auto_login_success() }];
		case 'failed':
			return [{ kind: 'error', message: m.message_auth_auto_login_failed() }];
		case 'invalid':
			// relogin says the saved password is gone, then the auto login flow says it failed.
			return [
				{ kind: 'error', message: m.message_auth_saved_credentials_invalid() },
				{ kind: 'error', message: m.message_auth_auto_login_failed() }
			];
		default:
			return [];
	}
}

/** The original's countdown toast texts, one per second, for the server's delay before it logs in again. */
export const autoLoginCountdown = (seconds: number): string[] =>
	Array.from({ length: Math.max(0, seconds) }, (_, i) => m.message_auto_login_delay_countdown({ seconds: seconds - i }));

/** The toast for a saved login problem the server answered, or undefined to show its own message. */
export function savedLoginProblemMessage(type: string): string | undefined {
	switch (type) {
		case 'SavedLogin.Invalid':
			return m.message_auth_saved_credentials_invalid();
		case 'SavedLogin.IncorrectPrimaryPassword':
		case 'SavedLogin.PrimaryPasswordRequired':
			return m.message_auth_incorrect_primary_password();
		default:
			return undefined;
	}
}

/**
 * A failed saved account login counts toward the network hint with the form's failures, as the
 * original's relogin does. The browser never sees the saved password, so it does not change the
 * credentials the form's attempts are compared with.
 */
export function noteSavedLoginFailure(hint: LoginNetworkHint, status: number, message: string | undefined, now = Date.now()): boolean {
	return LoginNetworkHint.counts(status, message) && hint.failed(now);
}
