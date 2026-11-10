import { describe, expect, test } from 'vitest';
import { LoginNetworkHint } from '../api/login-network-hint.js';

describe('LoginNetworkHint', () => {
	test('a wrong password never counts, other 401s do', () => {
		expect(LoginNetworkHint.counts(401, 'Invalid Username/Email or Password')).toBe(false);
		expect(LoginNetworkHint.counts(401, 'Missing Credentials')).toBe(true);
		expect(LoginNetworkHint.counts(500, 'boom')).toBe(false);
	});

	test('hints once on the third failure within 90 seconds', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('a', 'b');
		expect(hint.failed(0)).toBe(false);
		expect(hint.failed(1000)).toBe(false);
		expect(hint.failed(2000)).toBe(true);
		expect(hint.failed(3000)).toBe(false);
	});

	test('old failures fall out of the window and new credentials start over', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('a', 'b');
		hint.failed(0);
		hint.failed(1000);
		expect(hint.failed(100_000)).toBe(false);
		hint.attempt('a', 'c');
		expect(hint.failed(100_001)).toBe(false);
		expect(hint.failed(100_002)).toBe(false);
		expect(hint.failed(100_003)).toBe(true);
	});
});

describe('LoginNetworkHint, as the original auth store counts', () => {
	// The login form only calls failed() for counted errors.
	const fail = (hint: LoginNetworkHint, message: string, now: number) => LoginNetworkHint.counts(401, message) && hint.failed(now);

	test('a wrong password in between does not make the third failure', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('tester@example.com', 'password');
		expect(fail(hint, 'Unauthorized', 0)).toBe(false);
		expect(fail(hint, 'Unauthorized', 20_000)).toBe(false);
		expect(fail(hint, 'Invalid Username/Email or Password', 40_000)).toBe(false);
	});

	test('a successful login starts the window again', () => {
		const hint = new LoginNetworkHint();
		hint.attempt('tester@example.com', 'password');
		hint.failed(0);
		hint.failed(30_000);
		expect(hint.failed(60_000)).toBe(true);
		hint.reset();
		hint.attempt('tester@example.com', 'password');
		expect(hint.failed(70_000)).toBe(false);
		expect(hint.failed(90_000)).toBe(false);
	});

	test('changed credentials say so, so a hint still showing can be dismissed', () => {
		const hint = new LoginNetworkHint();
		expect(hint.attempt('tester@example.com', 'password')).toBe(false);
		hint.failed(0);
		hint.failed(30_000);
		expect(hint.failed(60_000)).toBe(true);
		expect(hint.attempt('tester@example.com', 'password')).toBe(false);
		expect(hint.attempt('tester@example.com', 'new-password')).toBe(true);
		expect(hint.hinted).toBe(false);
		expect(hint.failed(70_000)).toBe(false);
	});
});
