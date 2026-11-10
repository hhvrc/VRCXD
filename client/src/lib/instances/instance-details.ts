import { replaceBioSymbols } from '#lib/helpers/format.js';

// The instance tooltip's details from the original's InstanceActionBar, kept free of the app's state.

export type InstanceLanguageSource = {
	languages?: string[];
	languagesIso639?: string[];
	languageRatio?: Record<string, number>;
	/** The languages the instance creator set, as ISO 639 keys. */
	creationLanguages?: string[];
};

export type InstanceLanguage = { code: string; key: string; name: string; percent: number | null; isCreationLanguage: boolean };

/**
 * The original's instanceLanguages: the spoken languages by share, then the creator's languages nobody
 * speaks yet, without a share. nameOf names an ISO 639 key, given the code VRChat reported.
 */
export function instanceLanguageList(instance: InstanceLanguageSource | undefined, nameOf: (key: string, code: string) => string): InstanceLanguage[] {
	const codes = Array.isArray(instance?.languages) ? instance.languages : [];
	const iso639 = instance?.languagesIso639 ?? [];
	const ratio = instance?.languageRatio ?? {};
	const creation = new Set(instance?.creationLanguages ?? []);
	const baseOf = (code: string) => code.split('-')[0];
	const bases = [...new Set(codes.map(baseOf))];
	const byBase = Object.fromEntries(bases.map((base, i) => [base, iso639[i]]));
	const list: InstanceLanguage[] = codes.map((code) => {
		const key = byBase[baseOf(code)] ?? code;
		const region = code.split('-')[1];
		const name = nameOf(key, code);
		return { code, key, name: region ? `${name} (${region.toUpperCase()})` : name, percent: Math.round((ratio[code] ?? 0) * 100), isCreationLanguage: creation.has(key) || creation.has(code) };
	});
	const listed = new Set(list.flatMap((lang) => [lang.code, lang.key]));
	for (const key of creation) {
		if (listed.has(key)) continue;
		list.push({ code: key, key, name: nameOf(key, key), percent: null, isCreationLanguage: true });
	}
	return list.sort((a, b) => (b.percent ?? -1) - (a.percent ?? -1));
}

/** The original's instanceDescription: the creator's description, unless it only repeats the world's. */
export function instanceDescription(instance: { description?: string | null; world?: { description?: string | null } | null } | undefined): string {
	const description = replaceBioSymbols(instance?.description ?? '').trim();
	const worldDescription = replaceBioSymbols(instance?.world?.description ?? '').trim();
	if (!description || worldDescription.startsWith(description)) return '';
	return description;
}
