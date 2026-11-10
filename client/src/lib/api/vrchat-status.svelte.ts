import { api } from '#lib/api/client.js';

type Report = { status: string; indicator: string; summary: string; updatedAt?: string | null; hasIssue: boolean };

const FAST = 2 * 60 * 1000;
const SLOW = 15 * 60 * 1000;
export const STATUS_PAGE = 'https://status.vrchat.com';

/**
 * The original's vrcStatus store (upstream issue 1396): VRChat's status page through the server,
 * every 2 minutes while there is an issue and the window has focus, every 15 otherwise, and again on
 * focus when over a minute old.
 */
class VrchatStatus {
	status = $state('');
	indicator = $state('');
	summary = $state('');
	#fetchedAt = 0;
	#started = false;

	readonly hasIssue = $derived(!!this.status);
	readonly isMajor = $derived(this.indicator === 'major');
	readonly text = $derived(this.status && this.summary ? `${this.status}: ${this.summary}` : this.status);

	async refresh() {
		this.#fetchedAt = Date.now();
		try {
			const report = await api.get<Report>('/1/vrchat-status');
			this.status = report.status;
			this.indicator = report.indicator;
			this.summary = report.summary;
		} catch {
			this.status = 'Failed to fetch VRC status';
			this.indicator = '';
			this.summary = '';
		}
	}

	start() {
		if (this.#started || typeof window === 'undefined') return;
		this.#started = true;
		void this.refresh();
		setInterval(() => {
			const interval = document.hasFocus() && this.hasIssue ? FAST : SLOW;
			if (Date.now() - this.#fetchedAt > interval) void this.refresh();
		}, FAST);
		window.addEventListener('focus', () => {
			if (Date.now() - this.#fetchedAt > 60 * 1000) void this.refresh();
		});
	}

	openPage() {
		window.open(STATUS_PAGE, '_blank', 'noopener,noreferrer');
	}
}

export const vrchatStatus = new VrchatStatus();
