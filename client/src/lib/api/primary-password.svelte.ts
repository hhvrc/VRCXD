import { toast } from 'svelte-sonner';
import { m } from '#lib/paraglide/messages.js';
import { api, ApiError } from '#lib/api/client.js';
import { toastError } from '#lib/helpers/feedback.js';

// The original's primary password (stores/auth.js): saved passwords are encrypted with it, the
// server never keeps it, and it is asked for each time one of them is used. GET/PUT
// /1/accounts/primary-password hold whether it is on.

export const primaryPassword = $state({ enabled: false });

/** The original's "Primary Password Required" prompt. */
export const primaryPrompt = $state({ open: false, value: '' });
/** The original's PrimaryPasswordDialog, for a new one. */
export const primarySetDialog = $state({ open: false, password: '', rePassword: '' });

let answer: ((value: string | null) => void) | undefined;

export async function loadPrimaryPassword(): Promise<void> {
	try {
		primaryPassword.enabled = (await api.get<{ enabled: boolean }>('/1/accounts/primary-password')).enabled;
	} catch {
		// The token gate handles a dead server.
	}
}

/** Asks for the primary password; null when cancelled. */
export function askPrimaryPassword(): Promise<string | null> {
	answer?.(null);
	primaryPrompt.value = '';
	primaryPrompt.open = true;
	return new Promise((resolve) => (answer = resolve));
}

export function answerPrimaryPassword(value: string | null): void {
	primaryPrompt.open = false;
	primaryPrompt.value = '';
	const resolve = answer;
	answer = undefined;
	resolve?.(value);
}

/** The original's enablePrimaryPasswordChange: on opens the new password dialog, off asks for it first. */
export async function togglePrimaryPassword(on: boolean): Promise<void> {
	if (on) {
		primarySetDialog.password = '';
		primarySetDialog.rePassword = '';
		primarySetDialog.open = true;
		return;
	}
	const value = await askPrimaryPassword();
	if (value === null) return;
	await putPrimaryPassword(false, value);
}

/** The original's setPrimaryPassword: every saved password gets encrypted with it. */
export async function setPrimaryPassword(): Promise<void> {
	const password = primarySetDialog.password;
	primarySetDialog.open = false;
	await putPrimaryPassword(true, password);
}

async function putPrimaryPassword(enabled: boolean, password: string) {
	try {
		primaryPassword.enabled = (await api.put<{ enabled: boolean }>('/1/accounts/primary-password', { enabled, password })).enabled;
	} catch (err) {
		if (ApiError.from(err).type === 'SavedLogin.IncorrectPrimaryPassword') toast.error(m.message_auth_incorrect_primary_password());
		else toastError(err);
	}
}
