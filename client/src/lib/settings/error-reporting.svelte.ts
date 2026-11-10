import { browser } from '$app/env';
import { m } from '#lib/paraglide/messages.js';
import { api } from '#lib/api/client.js';
import { readText, storeText } from '#lib/settings/settings-storage.js';
import {
	ERROR_REPORTING_KEY,
	MEMORY_REPORT,
	beforeSend,
	isOptedIn,
	shouldAskConsent,
	shouldReportMemory
} from '#lib/settings/error-reporting.js';

// The original's error reporting opt-in: the setting, its consent and restart prompts, and the
// reporter itself, which is loaded and started only for a browser that opted in, and only when the
// server is configured with an address to report to.

export type ErrorReportingPrompt = {
	title: string;
	description: string;
	confirmText: string;
	cancelText: string;
	resolve: (ok: boolean) => void;
};

class ErrorReporting {
	/** The stored answer: '' until asked, then 'true' or 'false'. */
	setting = $state(readText(ERROR_REPORTING_KEY));
	prompt = $state<ErrorReportingPrompt | null>(null);
	/** Whether the server has an address to report to; the setting is hidden while it has none. */
	available = $state(false);

	get enabled() {
		return this.setting === 'true';
	}

	private confirm(options: { title: string; description: string; confirmText?: string; cancelText?: string }) {
		return new Promise<boolean>((resolve) => {
			this.prompt = {
				confirmText: m.dialog_alertdialog_confirm(),
				cancelText: m.dialog_alertdialog_cancel(),
				...options,
				resolve: (ok) => {
					this.prompt = null;
					resolve(ok);
				}
			};
		});
	}

	private store(enabled: boolean) {
		this.setting = String(enabled);
		storeText(ERROR_REPORTING_KEY, this.setting);
	}

	/** The original's checkSentryConsent: asks, then asks to restart; only a yes to both turns it on. */
	async checkConsent() {
		const ok = await this.confirm({
			title: m.view_settings_advanced_advanced_anonymous_error_reporting_consent_title(),
			description: m.view_settings_advanced_advanced_anonymous_error_reporting_consent_description()
		});
		if (!ok) return;
		const restart = await this.confirm({
			title: m.confirm_restart_required_title(),
			description: m.view_settings_advanced_advanced_anonymous_error_reporting_enabled_restart_description(),
			confirmText: m.confirm_restart_now(),
			cancelText: m.confirm_restart_later()
		});
		if (!restart) return;
		this.store(true);
		location.reload();
	}

	/** The original's setSentryErrorReporting: flips the setting once the restart is confirmed. */
	async toggle() {
		if (!this.available) return;
		const ok = await this.confirm({
			title: m.confirm_restart_required_title(),
			description: m.view_settings_advanced_advanced_anonymous_error_reporting_disabled_restart_description(),
			confirmText: m.confirm_restart_now(),
			cancelText: m.confirm_restart_later()
		});
		if (!ok) return;
		this.store(!this.enabled);
		location.reload();
	}

	private started = false;

	/** At start: the original asks 2 seconds after its settings load, while unanswered. */
	start() {
		if (!browser || this.started) return;
		this.started = true;
		void this.load();
	}

	private async load() {
		let dsn: string | null = null;
		try {
			dsn = (await api.get<{ dsn?: string | null }>('/1/error-reporting')).dsn ?? null;
		} catch (e) {
			console.error('Failed to read the error reporting address:', e);
		}
		this.available = !!dsn;
		if (shouldAskConsent(this.setting, this.available)) setTimeout(() => void this.checkConsent(), 2000);
		if (dsn) void initSentry(this.setting, dsn);
	}
}

export const errorReporting = new ErrorReporting();

/** The original's initSentry. Nothing is loaded or sent unless the user opted in. */
export async function initSentry(setting: string, dsn: string) {
	if (!isOptedIn(setting, true)) return;
	try {
		const Sentry = await import('@sentry/browser');
		Sentry.init({
			dsn,
			environment: 'nightly',
			replaysSessionSampleRate: 0,
			replaysOnErrorSampleRate: 1.0,
			tracesSampleRate: 0.0001,
			beforeSend,
			integrations: [
				Sentry.replayIntegration({ maskAllText: true, blockAllMedia: true }),
				Sentry.browserTracingIntegration()
			]
		});
		console.log('Sentry initialized');
		startMemoryReport(Sentry);
	} catch (e) {
		console.error('Failed to initialize Sentry:', e);
	}
}

/** The original's renderer memory report: a warning when the JS heap nears its limit. */
function startMemoryReport(Sentry: typeof import('@sentry/browser')) {
	const read = () => {
		const memory = (performance as Performance & { memory?: { usedJSHeapSize: number; jsHeapSizeLimit: number } }).memory;
		return memory && typeof memory.usedJSHeapSize === 'number' && typeof memory.jsHeapSizeLimit === 'number' ? memory : null;
	};
	if (!read()) return;
	let lastSent = 0;
	setInterval(() => {
		const memory = read();
		if (!memory) return;
		const now = Date.now();
		if (!shouldReportMemory(memory.usedJSHeapSize, memory.jsHeapSizeLimit, now, lastSent)) return;
		lastSent = now;
		const ratio = memory.usedJSHeapSize / memory.jsHeapSizeLimit;
		Sentry.withScope((scope) => {
			scope.setLevel('warning');
			scope.setTag('reason', 'high-js-heap');
			scope.setContext('memory', { usedMB: memory.usedJSHeapSize / 1024 / 1024, limitMB: memory.jsHeapSizeLimit / 1024 / 1024, ratio });
			Sentry.captureMessage('Memory usage critical: nearing JS heap limit');
		});
	}, MEMORY_REPORT.intervalMs);
}
