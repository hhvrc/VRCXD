import { accountPath, api, ApiError } from '#lib/api/client.js';
import type { Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

export type LogType = { value: string; readonly label: string };

// The state the original's log tables (Friend Log, Moderation) share: rows from one account
// endpoint, a type filter kept under the original's storage name, and a search box. Each page
// keeps its own columns and how a row matches the search.
export class LogTable<Row extends { type: string }> {
	rows = $state<Row[]>([]);
	loading = $state(false);
	error = $state('');
	types = $state<string[]>([]);
	search = $state('');
	confirming = $state<Confirming | undefined>();

	readonly shown = $derived.by(() => {
		const wanted = new Set(this.types.map((t) => t.toLowerCase()));
		const term = this.search.trim().toLowerCase();
		return this.rows.filter((row) => (wanted.size === 0 || wanted.has(row.type.toLowerCase())) && this.#matches(row, term));
	});

	readonly options: LogType[];
	readonly #filtersKey: string;
	readonly #path: string;
	readonly #matches: (row: Row, term: string) => boolean;

	/** `matches` says whether a row stays for the trimmed, lowercased search term (empty when none). */
	constructor(options: LogType[], filtersKey: string, path: string, matches: (row: Row, term: string) => boolean) {
		this.options = options;
		this.#filtersKey = filtersKey;
		this.#path = path;
		this.#matches = matches;
		this.types = readSetting<string[]>(filtersKey, []);
	}

	typeLabel(type: string) {
		return this.options.find((t) => t.value === type)?.label ?? type;
	}

	setTypes = (value: string[]) => {
		this.types = value;
		storeSetting(this.#filtersKey, value);
	};

	load = async () => {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		this.loading = true;
		try {
			this.rows = await api.get<Row[]>(accountPath(account, this.#path));
			this.error = '';
		} catch (err) {
			this.error = ApiError.from(err).message;
		} finally {
			this.loading = false;
		}
	};
}
