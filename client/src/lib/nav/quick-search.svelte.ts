import { accountPath, api } from '#lib/api/client.js';
import removeConfusables, { removeWhitespace } from '#lib/people/confusables.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original's quick search store and worker: the index is read when the dialog opens, and each
// category shows up to ten matches, prefix matches first.
export type QuickFriend = { id: string; name: string; memo: string; note: string; imageUrl?: string | null };
export type QuickItem = { id: string; name: string; ownerId?: string | null; imageUrl?: string | null };
type Index = { friends: QuickFriend[]; ownAvatars: QuickItem[]; favAvatars: QuickItem[]; ownWorlds: QuickItem[]; favWorlds: QuickItem[]; groups: QuickItem[] };
export type FriendResult = QuickFriend & { matchedField: 'name' | 'memo' | 'note' };
export type Results = {
	friends: FriendResult[];
	ownAvatars: QuickItem[];
	favAvatars: QuickItem[];
	ownWorlds: QuickItem[];
	favWorlds: QuickItem[];
	ownGroups: QuickItem[];
	joinedGroups: QuickItem[];
};

const LIMIT = 10;
const EMPTY: Results = { friends: [], ownAvatars: [], favAvatars: [], ownWorlds: [], favWorlds: [], ownGroups: [], joinedGroups: [] };

function localeIncludes(text: string, search: string, comparer: Intl.Collator): boolean {
	if (search === '') return true;
	if (!text || search.length > text.length) return false;
	for (let i = 0; i < text.length - search.length + 1; i++) {
		if (comparer.compare(text.substring(i, i + search.length), search) === 0) return true;
	}
	return false;
}

type Normalized<T> = T & { normalized: string };
const normalize = <T extends { name: string }>(items: T[]): Normalized<T>[] => items.map((i) => ({ ...i, normalized: i.name ? removeConfusables(i.name) : '' }));

function matchName(item: { name: string; normalized: string }, cleanQuery: string, comparer: Intl.Collator) {
	if (!item.name || !cleanQuery) return false;
	return localeIncludes(item.normalized, cleanQuery, comparer) || localeIncludes(item.name, cleanQuery, comparer);
}

function sortAndLimit<T extends { name: string }>(results: T[], cleanQuery: string, comparer: Intl.Collator): T[] {
	const prefix = new Set(results.filter((r) => comparer.compare(r.name.substring(0, cleanQuery.length), cleanQuery) === 0));
	results.sort((a, b) => {
		const [ap, bp] = [prefix.has(a), prefix.has(b)];
		if (ap !== bp) return ap ? -1 : 1;
		return comparer.compare(a.name, b.name);
	});
	return results.slice(0, LIMIT);
}

class QuickSearch {
	open = $state(false);
	query = $state('');
	#index: { friends: Normalized<QuickFriend>[]; ownAvatars: Normalized<QuickItem>[]; favAvatars: Normalized<QuickItem>[]; ownWorlds: Normalized<QuickItem>[]; favWorlds: Normalized<QuickItem>[]; groups: Normalized<QuickItem>[] } | null = $state.raw(null);

	async load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const index = await api.get<Index>(accountPath(account, '/quick-search')).catch(() => null);
		// Dropped when the account changed while it was loading.
		if (!index || account !== vrchatAuth.ownerUserId) return;
		this.#index = {
			friends: normalize(index.friends),
			ownAvatars: normalize(index.ownAvatars),
			favAvatars: normalize(index.favAvatars),
			ownWorlds: normalize(index.ownWorlds),
			favWorlds: normalize(index.favWorlds),
			groups: normalize(index.groups)
		};
	}

	show() {
		this.open = true;
		this.load();
	}

	/** Another account's index is no use: forget it, as the original's worker restarts. */
	clear() {
		this.#index = null;
		this.query = '';
	}

	results: Results = $derived.by(() => {
		const index = this.#index;
		const query = this.query;
		if (!index || !query) return EMPTY;
		const comparer = new Intl.Collator(navigator.language.replace('_', '-'), { usage: 'search', sensitivity: 'base' });
		const clean = removeWhitespace(query);
		const friends: FriendResult[] = [];
		for (const f of index.friends) {
			let field: FriendResult['matchedField'] | null = matchName(f, clean, comparer) ? 'name' : null;
			if (!field && f.memo && query.length >= 2 && localeIncludes(f.memo, query, comparer)) field = 'memo';
			if (!field && f.note && query.length >= 2 && localeIncludes(f.note, query, comparer)) field = 'note';
			if (field) friends.push({ id: f.id, name: f.name, memo: f.memo, note: f.note, imageUrl: f.imageUrl, matchedField: field });
		}
		const items = (list: Normalized<QuickItem>[], ownerId?: string) =>
			sortAndLimit(
				list.filter((i) => (!ownerId || i.ownerId === ownerId) && matchName(i, clean, comparer)).map(({ normalized: _, ...i }) => i),
				clean,
				comparer
			);
		const account = vrchatAuth.ownerUserId;
		const ownAvatars = items(index.ownAvatars);
		const ownWorlds = items(index.ownWorlds);
		const ownGroups = items(index.groups, account);
		const without = (list: QuickItem[], own: QuickItem[]) => {
			const ids = new Set(own.map((i) => i.id));
			return list.filter((i) => !ids.has(i.id));
		};
		return {
			friends: sortAndLimit(friends, clean, comparer),
			ownAvatars,
			favAvatars: without(items(index.favAvatars), ownAvatars),
			ownWorlds,
			favWorlds: without(items(index.favWorlds), ownWorlds),
			ownGroups,
			joinedGroups: without(items(index.groups), ownGroups)
		};
	});

	get hasResults() {
		return Object.values(this.results).some((list) => list.length > 0);
	}
}

export const quickSearch = new QuickSearch();
