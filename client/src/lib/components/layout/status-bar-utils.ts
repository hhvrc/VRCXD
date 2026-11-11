// The original's statusBarUtils: UTC offsets for the clocks, the saved visibility, clocks and clock
// count, and the uptime text. Saved values are what local storage holds: JSON text (as the original
// stored it, and as an import brings it) or the value itself.

export const defaultVisibility = { vrchat: true, steamvr: true, ws: true, nowPlaying: true, uptime: true, clocks: true, zoom: true, servers: true };
export type Visibility = typeof defaultVisibility;
export type Clock = { offset: number };

/** Clamps and rounds to a UTC offset in [-12, 14]; 0 for anything not a number. */
export function normalizeUtcHour(value: unknown): number {
	const n = Number(value);
	if (!Number.isFinite(n)) return 0;
	return Math.max(-12, Math.min(14, Math.round(n)));
}

export function formatUtcHour(offset: number): string {
	const n = normalizeUtcHour(offset);
	return `UTC${n >= 0 ? '+' : ''}${n}`;
}

/** A number, "5", "UTC+9", "UTC-5:30", or an old IANA zone name, as an offset. */
export function parseClockOffset(value: unknown): number {
	if (typeof value === 'number') return normalizeUtcHour(value);
	if (typeof value !== 'string') return 0;
	if (/^[+-]?\d+$/.test(value.trim())) return normalizeUtcHour(Number(value));
	const utc = value.match(/^UTC([+-])(\d{1,2})(?::(\d{1,2}))?$/i);
	if (utc) return normalizeUtcHour((utc[1] === '+' ? 1 : -1) * (Number(utc[2]) + Number(utc[3] || 0) / 60));
	// Clocks once stored IANA zone names.
	try {
		const parts = new Intl.DateTimeFormat('en-US', { timeZone: value, timeZoneName: 'longOffset' }).formatToParts(new Date());
		const name = parts.find((p) => p.type === 'timeZoneName')?.value ?? '';
		const m = name.match(/GMT([+-])(\d{2}):(\d{2})/);
		return m ? normalizeUtcHour((m[1] === '+' ? 1 : -1) * (Number(m[2]) + Number(m[3]) / 60)) : 0;
	} catch {
		return 0;
	}
}

export function normalizeClock(entry: unknown): Clock {
	if (entry && typeof entry === 'object') {
		if ('offset' in entry) return { offset: parseClockOffset((entry as { offset: unknown }).offset) };
		if ('timezone' in entry) return { offset: parseClockOffset((entry as { timezone: unknown }).timezone) };
	}
	return { offset: 0 };
}

/** The saved value as data; undefined when there is none or it does not parse. */
function parseSaved(saved: unknown): unknown {
	if (saved === null || saved === undefined || saved === '') return undefined;
	if (typeof saved !== 'string') return saved;
	try {
		return JSON.parse(saved);
	} catch {
		return undefined;
	}
}

/** The saved visibility over the defaults, as a new object. */
export function loadVisibility(saved: unknown): Visibility {
	const parsed = parseSaved(saved);
	return { ...defaultVisibility, ...(parsed && typeof parsed === 'object' ? (parsed as Partial<Visibility>) : {}) };
}

/** The three saved clocks, or copies of the defaults when they are missing or not three. */
export function loadClocks(saved: unknown, defaults: Clock[]): Clock[] {
	const parsed = parseSaved(saved);
	if (Array.isArray(parsed) && parsed.length === 3) return parsed.map(normalizeClock);
	return defaults.map((c) => ({ ...c }));
}

/** The saved clock count, 0 to 3; 2 when it is missing or anything else. */
export function loadClockCount(saved: unknown): number {
	if (saved === null || saved === undefined) return 2;
	const n = Number(saved);
	return n >= 0 && n <= 3 ? n : 2;
}

export function formatAppUptime(elapsedSeconds: number): string {
	const safe = Math.max(0, Math.floor(elapsedSeconds));
	const pad = (n: number) => String(n).padStart(2, '0');
	return `${pad(Math.floor(safe / 3600))}:${pad(Math.floor((safe % 3600) / 60))}:${pad(safe % 60)}`;
}

/** A ProTV producer's clock (the original's nowPlaying with spec set): the position at a logged time. */
export type NowPlayingClock = { position: number; at: number; speed: number; paused: boolean };

/** updateNowPlaying for a spec media: it holds while paused and stops at its length instead of clearing. */
export function clockElapsed(clock: NowPlayingClock, length: number, now: number): number {
	const elapsed = clock.paused ? clock.position : clock.position + (now - clock.at) * clock.speed;
	return Math.max(0, length > 0 ? Math.min(elapsed, length) : elapsed);
}
