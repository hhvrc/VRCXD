// The one debounce for refreshes driven by live events. A plain trailing debounce restarts on every
// call, so under a steady stream of events (a busy instance, a big friend list) the refresh it guards
// never runs. This one waits `wait` ms after the last call, but never more than `maxWait` ms after the
// first call it is holding, and never runs twice within `minInterval` ms (for reloads that can
// themselves fetch from VRChat).

export type DebounceOptions = {
	/** Quiet time after the last call before running. */
	wait: number;
	/** The longest a held call waits, however many calls follow it. Defaults to five times `wait`. */
	maxWait?: number;
	/** The least time between two runs. */
	minInterval?: number;
	/** When it last ran before this debounce took over (the page's own first load), for `minInterval`. */
	lastRun?: number;
};

export type Debounced = {
	(): void;
	/** Drops a held call. */
	cancel(): void;
	/** Runs a held call now. */
	flush(): void;
	/** Whether a call is waiting to run. */
	readonly pending: boolean;
};

export function debounce(run: () => unknown, options: DebounceOptions): Debounced {
	const { wait } = options;
	const maxWait = Math.max(wait, options.maxWait ?? wait * 5);
	const minInterval = options.minInterval ?? 0;
	let timer: ReturnType<typeof setTimeout> | undefined;
	let first = 0;
	let lastRun = options.lastRun ?? -Infinity;

	const fire = () => {
		timer = undefined;
		first = 0;
		lastRun = Date.now();
		void run();
	};

	const call = (() => {
		const now = Date.now();
		if (!timer) first = now;
		clearTimeout(timer);
		const due = Math.max(Math.min(now + wait, first + maxWait), lastRun + minInterval);
		timer = setTimeout(fire, Math.max(0, due - now));
	}) as Debounced;
	call.cancel = () => {
		clearTimeout(timer);
		timer = undefined;
		first = 0;
	};
	call.flush = () => {
		if (!timer) return;
		clearTimeout(timer);
		fire();
	};
	Object.defineProperty(call, 'pending', { get: () => timer !== undefined });
	return call;
}
