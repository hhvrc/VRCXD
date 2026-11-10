// Kept apart from panels.ts, which lazily imports the widgets that use these.

/** The original's filter toggling: an empty list means every type. */
export function toggleFilter(all: readonly string[], current: unknown, type: string): string[] {
	const list = Array.isArray(current) ? (current as string[]) : [];
	if (!list.length) return all.filter((f) => f !== type);
	if (list.includes(type)) return list.filter((f) => f !== type);
	const next = [...list, type];
	return next.length === all.length ? [] : next;
}

export const filterActive = (current: unknown, type: string) => !Array.isArray(current) || !current.length || current.includes(type);
