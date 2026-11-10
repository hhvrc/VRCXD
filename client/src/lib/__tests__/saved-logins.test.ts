import { describe, expect, test } from 'vitest';
import { LoginNetworkHint } from '../api/login-network-hint.js';
import {
	AUTO_LOGIN_DELAY_PATTERN,
	autoLoginCountdown,
	autoLoginToasts,
	clampAutoLoginDelay,
	isPrimaryPassword,
	noteSavedLoginFailure,
	savedAccounts,
	savedLoginProblemMessage
} from '../api/saved-logins.js';
import type { Account } from '../api/types.js';

const account = (userId: string, sessionValid: boolean, hasSavedCredentials = false): Account => ({
	userId,
	displayName: userId,
	sessionValid,
	connected: sessionValid,
	loggedInAt: '2026-01-01T00:00:00Z',
	hasSavedCredentials
});

describe('auto-login delay', () => {
	test('is a whole number from 0 to 10, as the original clamps it', () => {
		expect(clampAutoLoginDelay('5')).toBe(5);
		expect(clampAutoLoginDelay('12')).toBe(10);
		expect(clampAutoLoginDelay('-3')).toBe(0);
		expect(clampAutoLoginDelay('abc')).toBe(0);
		expect(AUTO_LOGIN_DELAY_PATTERN.test('10')).toBe(true);
		expect(AUTO_LOGIN_DELAY_PATTERN.test('11')).toBe(false);
		expect(AUTO_LOGIN_DELAY_PATTERN.test('')).toBe(false);
	});

	test('counts down one toast per second', () => {
		expect(autoLoginCountdown(3)).toEqual(['Auto-login in 3s...', 'Auto-login in 2s...', 'Auto-login in 1s...']);
		expect(autoLoginCountdown(0)).toEqual([]);
	});
});

describe('saved accounts', () => {
	test('lists live sessions and accounts with a saved password', () => {
		const list = savedAccounts([account('usr_live', true), account('usr_saved', false, true), account('usr_gone', false)]);
		expect(list.map((a) => a.userId)).toEqual(['usr_live', 'usr_saved']);
	});

	test('the primary password takes 1 to 32 characters', () => {
		expect(isPrimaryPassword('')).toBe(false);
		expect(isPrimaryPassword('x'.repeat(32))).toBe(true);
		expect(isPrimaryPassword('x'.repeat(33))).toBe(false);
	});

	test('saved login problems show the original toasts', () => {
		expect(savedLoginProblemMessage('SavedLogin.Invalid')).toBe('Saved credentials are no longer valid.');
		expect(savedLoginProblemMessage('SavedLogin.IncorrectPrimaryPassword')).toBe('Incorrect primary password');
		expect(savedLoginProblemMessage('SavedLogin.PrimaryPasswordRequired')).toBe('Incorrect primary password');
		expect(savedLoginProblemMessage('Login.InvalidCredentials')).toBeUndefined();
	});
});

describe('auto login outcomes', () => {
	test('toast as the original auto login flow does', () => {
		expect(autoLoginToasts('success')).toEqual([{ kind: 'success', message: 'Automatically logged in.' }]);
		expect(autoLoginToasts('failed')).toEqual([{ kind: 'error', message: 'Failed to login automatically.' }]);
		expect(autoLoginToasts('invalid').map((t) => t.message)).toEqual(['Saved credentials are no longer valid.', 'Failed to login automatically.']);
		expect(autoLoginToasts('attempting')).toEqual([]);
		expect(autoLoginToasts('')).toEqual([]);
	});

	// The server logs in again by itself; the browser only gets the outcome, which never reaches the hint.
	test('auto login retries never count toward the network hint', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('tester@example.com', 'password');
		for (const state of ['attempting', 'failed', 'failed', 'failed']) autoLoginToasts(state);
		expect(hint.failed(0)).toBe(false);
		expect(hint.failed(1000)).toBe(false);
		expect(hint.failed(2000)).toBe(true);
	});

	test('saved account relogin failures count together with the form failures', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('tester@example.com', 'password');
		expect(LoginNetworkHint.counts(401, 'Unauthorized') && hint.failed(0)).toBe(false);
		expect(noteSavedLoginFailure(hint, 401, 'Unauthorized', 30_000)).toBe(false);
		expect(LoginNetworkHint.counts(401, 'Unauthorized') && hint.failed(60_000)).toBe(true);
	});

	test('a refused saved password does not count toward the hint', () => {
		const hint = new LoginNetworkHint();
		expect(noteSavedLoginFailure(hint, 401, 'Invalid Username/Email or Password', 0)).toBe(false);
		expect(noteSavedLoginFailure(hint, 401, 'Invalid Username/Email or Password', 1)).toBe(false);
		expect(noteSavedLoginFailure(hint, 401, 'Invalid Username/Email or Password', 2)).toBe(false);
	});
});
