import { beforeEach, describe, expect, test, vi } from 'vitest';
import { beforeSend, isOptedIn, shouldAskConsent, shouldReportMemory } from '../settings/error-reporting.js';

const api = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('#lib/api/client.js', () => ({ api }));
const sentry = vi.hoisted(() => ({
	init: vi.fn(),
	replayIntegration: vi.fn(() => ({ name: 'Replay' })),
	browserTracingIntegration: vi.fn(() => ({ name: 'BrowserTracing' })),
	withScope: vi.fn(),
	captureMessage: vi.fn()
}));
vi.mock('@sentry/browser', () => sentry);

const { errorReporting, initSentry } = await import('../settings/error-reporting.svelte.js');

// The original's anonymous error reporting: off without a server address, unset until answered,
// sent only once opted in.
describe('error reporting', () => {
	beforeEach(() => {
		api.get.mockReset();
		sentry.init.mockReset();
	});

	test('reports only when the server has an address and the user said yes', () => {
		expect(isOptedIn('', true)).toBe(false);
		expect(isOptedIn('false', true)).toBe(false);
		expect(isOptedIn('true', true)).toBe(true);
		expect(isOptedIn('true', false)).toBe(false);
	});

	test('asks at start only while unanswered and only when the server has an address', () => {
		expect(shouldAskConsent('', true)).toBe(true);
		expect(shouldAskConsent('false', true)).toBe(false);
		expect(shouldAskConsent('true', true)).toBe(false);
		expect(shouldAskConsent('', false)).toBe(false);
	});

	test('nothing is started when not opted in', async () => {
		await initSentry('', 'https://key@sentry.example/1');
		await initSentry('false', 'https://key@sentry.example/1');
		expect(sentry.init).not.toHaveBeenCalled();
	});

	test('opted in, it starts with the server address and the original options', async () => {
		await initSentry('true', 'https://key@sentry.example/1');
		const options = sentry.init.mock.calls[0][0];
		expect(options).toMatchObject({
			dsn: 'https://key@sentry.example/1',
			environment: 'nightly',
			replaysSessionSampleRate: 0,
			replaysOnErrorSampleRate: 1.0,
			tracesSampleRate: 0.0001
		});
		expect(sentry.replayIntegration).toHaveBeenCalledWith({ maskAllText: true, blockAllMedia: true });
	});

	test('without a server address there is no prompt, no setting and no reporter', async () => {
		vi.useFakeTimers();
		api.get.mockResolvedValue({ dsn: null });
		await (errorReporting as unknown as { load(): Promise<void> }).load();
		vi.advanceTimersByTime(5000);
		expect(api.get).toHaveBeenCalledWith('/1/error-reporting');
		expect(errorReporting.available).toBe(false);
		expect(errorReporting.prompt).toBeNull();
		expect(sentry.init).not.toHaveBeenCalled();
		vi.useRealTimers();
	});

	test('drops the errors the original drops', () => {
		const event = { id: 1 };
		expect(beforeSend(event, { originalException: new Error('Request failed 404') })).toBeNull();
		expect(beforeSend(event, { originalException: new Error('No such host is known') })).toBeNull();
		expect(beforeSend(event, { originalException: new Error('Cannot read properties of undefined') })).toBe(event);
		expect(beforeSend(event, { originalException: 'not an error' })).toBe(event);
	});

	test('the memory report waits for the threshold and the cooldown', () => {
		expect(shouldReportMemory(70, 100, 1_000_000, 0)).toBe(false);
		expect(shouldReportMemory(85, 100, 1_000_000, 0)).toBe(true);
		expect(shouldReportMemory(85, 100, 1_000_000, 1_000_000 - 60_000)).toBe(false);
	});
});
