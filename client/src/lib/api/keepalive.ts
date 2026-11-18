// The live socket's heartbeat, as the agent keeps it (agent/src/live.rs): a Ping every 30 seconds,
// which the server answers with a Pong, and nothing at all from the server for 90 seconds means the
// connection is dead. A socket can die without a close (a laptop asleep, a VPN or proxy dropping
// idle connections), and the browser only notices when it next writes, so without this the page
// would sit on a dead socket and show stale state as current.

export const PING_INTERVAL_MS = 30_000;
export const SILENCE_TIMEOUT_MS = 90_000;

export type Keepalive = {
	/** Anything came from the server: the connection is alive. */
	received(): void;
	stop(): void;
};

export function startKeepalive(ping: () => void, dead: () => void, interval = PING_INTERVAL_MS, timeout = SILENCE_TIMEOUT_MS): Keepalive {
	let lastHeard = Date.now();
	let stopped = false;
	const pinger = setInterval(() => {
		if (stopped) return;
		try {
			ping();
		} catch {
			// A socket that can't be written to is dead; the silence check says so.
		}
	}, interval);
	let watchdog: ReturnType<typeof setTimeout> | undefined;
	const watch = () => {
		watchdog = setTimeout(
			() => {
				if (stopped) return;
				if (Date.now() - lastHeard >= timeout) {
					stop();
					dead();
				} else watch();
			},
			Math.max(0, lastHeard + timeout - Date.now())
		);
	};
	const stop = () => {
		stopped = true;
		clearInterval(pinger);
		clearTimeout(watchdog);
	};
	watch();
	return {
		received: () => {
			lastHeard = Date.now();
		},
		stop
	};
}
