// The original's customUserTags and addCustomTag (stores/user.js, userCoordinator.js): tags other
// programs on the account's PC give users over the VRCX named pipe, shown in the user dialog in
// their colour.

export type CustomTag = { tag: string; colour: string };

export type CustomTagRow = { userId: string; tag: string; colour?: string | null };

export type CustomTagChange = { userId: string; tag: string; colour: string; cleared: boolean };

/** The tags by user id, from the server's list. */
export function tagsFromRows(rows: CustomTagRow[]): Map<string, CustomTag> {
	const tags = new Map<string, CustomTag>();
	for (const row of rows) if (row.tag) tags.set(row.userId, { tag: row.tag, colour: row.colour ?? '' });
	return tags;
}

/** One change applied in place: a tag replaces the user's, an empty one removes it, cleared drops them all. */
export function applyTagChange(tags: Map<string, CustomTag>, change: CustomTagChange): void {
	if (change.cleared) {
		tags.clear();
		return;
	}
	if (!change.userId) return;
	if (change.tag) tags.set(change.userId, { tag: change.tag, colour: change.colour });
	else tags.delete(change.userId);
}

/** The badge's colour and border, as the original's :style; none without a colour. */
export function tagStyle(tag: CustomTag): string {
	return tag.colour ? `color: ${tag.colour}; border-color: ${tag.colour}` : '';
}
