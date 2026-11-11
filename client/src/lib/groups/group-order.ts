// The original's shared/utils/groupOrder.js and the in-game sort of UserDialogGroupsTab: VRChat
// keeps your group order in its registry as VRC_GROUP_ORDER_<user id>, a JSON list of group ids.

export const groupOrderKey = (userId: string): string => `VRC_GROUP_ORDER_${userId}`;

/** The registry value as a list; Unity's strings may end with a NUL. Anything else is no order. */
export function parseGroupOrder(raw: unknown): string[] {
	if (typeof raw !== 'string' || !raw) return [];
	try {
		const parsed: unknown = JSON.parse(raw.replace(/\0+$/, ''));
		return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : [];
	} catch {
		return [];
	}
}

/** normalizeGroupOrder: the saved ids still valid, in their order, then the groups it lacks. */
export function normalizeGroupOrder(order: string[], groupIds: string[]): string[] {
	const valid = new Set(groupIds);
	const seen = new Set<string>();
	const out: string[] = [];
	for (const id of [...order.filter((id) => valid.has(id)), ...groupIds]) {
		if (!seen.has(id)) {
			seen.add(id);
			out.push(id);
		}
	}
	return out;
}

/** moveGroupInOrder: moves in place; false when the move does nothing or is out of range. */
export function moveGroupInOrder(order: string[], groupId: string, toIndex: number): boolean {
	const from = order.indexOf(groupId);
	if (from === -1 || toIndex < 0 || toIndex >= order.length || from === toIndex) return false;
	order.splice(toIndex, 0, ...order.splice(from, 1));
	return true;
}

/** sortGroupsByInGame: ordered ones first in their order, the rest after as they were. */
export function compareInGame(order: string[], a: string, b: string): number {
	const x = order.indexOf(a);
	const y = order.indexOf(b);
	if (x === -1 && y === -1) return 0;
	if (x === -1) return 1;
	if (y === -1) return -1;
	return x - y;
}
