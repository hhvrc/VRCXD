// The original's anonymous error reporting (plugins/sentry.js, stores/settings/advanced.js). Reports
// go only to the address the server is configured with (VRCX:ErrorReporting:Dsn); without one the
// feature is off: no consent prompt, no switch, nothing loaded or sent.

/** The original's config key: unset until the user answers, then "true" or "false". */
export const ERROR_REPORTING_KEY = 'VRCX_SentryEnabled';

/** Whether reports may be sent: the server has an address, and the user said yes. */
export function isOptedIn(setting: string, available: boolean): boolean {
	return available && setting === 'true';
}

/** Whether to ask at start: the server has an address, and the user has not answered yet. */
export function shouldAskConsent(setting: string, available: boolean): boolean {
	return available && setting === '';
}

// Errors the original drops before sending: HTTP failures and the machine's own network and disk trouble.
const IGNORED = [
	'401',
	'403',
	'404',
	'500',
	'503',
	'No such host is known',
	'The SSL connection could not be established',
	'A connection attempt failed',
	'no data of the requested type was found',
	'An error occurred while sending the request',
	'database or disk is full',
	'disk I/O error',
	'There is not enough space on the disk.',
	'The requested address is not valid in its context.'
];

/** The original's beforeSend: null drops the event. */
export function beforeSend<E>(event: E, hint: { originalException?: unknown }): E | null {
	const error = hint.originalException as { message?: unknown } | null | undefined;
	if (error && typeof error.message === 'string') {
		const message = error.message;
		if (IGNORED.some((text) => message.includes(text))) return null;
	}
	return event;
}

/** The original's high JS heap report settings. */
export const MEMORY_REPORT = { thresholdRatio: 0.8, intervalMs: 10_000, cooldownMs: 5 * 60_000 };

/** Whether the heap is past the threshold, given the original's cooldown. */
export function shouldReportMemory(used: number, limit: number, now: number, lastSent: number, options = MEMORY_REPORT): boolean {
	const ratio = used / limit;
	if (!Number.isFinite(ratio) || ratio < options.thresholdRatio) return false;
	return now - lastSent >= options.cooldownMs;
}
