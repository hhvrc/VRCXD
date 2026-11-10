// What the user, world, avatar and group pop-ups share: the original's classes and small formatters.
export const NONE = '-';
const SECTION_TYPE = 'text-3xs font-bold uppercase tracking-wide';
export const SECTION = `${SECTION_TYPE} text-muted-foreground`;
/** The user page's section labels, in the profile theme's subtext colour. */
export const PROFILE_SECTION = `${SECTION_TYPE} text-(--profile-subtext)`;
export const SECTION_ROW = 'mb-2 flex items-center justify-between border-b border-muted-foreground/20 pb-2';
export const SECTION_TITLE = `${SECTION} mb-2 border-b border-muted-foreground/20 pb-2`;
export const INFO_ROW = 'flex items-start justify-between gap-2 text-xs';
export const ROW = 'box-border flex w-[167px] cursor-pointer items-center p-1.5 text-compact hover:rounded-[25px_5px_5px_25px] hover:bg-muted';
export const TAB_TRIGGER =
	'relative inline-flex h-10 min-w-max shrink-0 cursor-pointer items-center justify-center px-3 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none focus-visible:ring-inset data-[state=active]:text-primary after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:rounded-full after:bg-primary after:opacity-0 data-[state=active]:after:opacity-100';
export const TAB_CONTENT = 'min-h-0 flex-1 overflow-y-auto outline-none';
export const TAB_LIST_WRAP = 'relative flex w-full min-w-0 shrink-0 items-center overflow-hidden rounded-xl border-b border-border bg-(--profile-card)';
export const TAB_LIST = 'relative flex w-full items-center gap-1 overflow-x-auto pl-2';
/** A list row's first line (name) and the smaller lines under it. */
export const PERSON_NAME = 'block truncate leading-4.5 font-medium';
export const PERSON_SUBTEXT = 'block truncate text-xs';
/** A store listing's price line. */
export const PRICE = `${PERSON_SUBTEXT} italic underline`;
export const SUBTEXT_MUTED = `${PERSON_SUBTEXT} text-muted-foreground`;
/** The 96px picture at the start of a store listing. */
export const LISTING_IMAGE = 'relative mr-2.5 inline-block size-24 flex-none';
/** The pop-ups' left rail beside the tabs, and the clickable name at its top. */
export const RAIL = 'w-77 flex-none overflow-y-auto pr-4';
export const TITLE_BUTTON = 'flex cursor-pointer flex-wrap text-left font-bold';
/** The user, world, avatar and group pages opened in a tab of their own. */
export const DETAIL_PAGE = 'mx-auto flex h-full w-full max-w-5xl flex-col';
/** The pop-ups' card surface: a padded panel, the same with the small text, and a tab's full-height pane. */
export const PANEL = 'rounded-xl bg-(--profile-card) p-3';
export const CARD = `my-2 ${PANEL} text-xs`;
/** The header card at the top of the avatar, group and world pop-ups. */
export const HEADER_CARD = 'flex flex-col overflow-hidden rounded-xl bg-(--profile-card)';
export const TAB_PANE = 'flex h-full min-h-0 flex-col rounded-xl bg-(--profile-card) p-2';
export const CONTENT_LABELS: Record<string, string> = { content_horror: 'Horror', content_gore: 'Gore', content_violence: 'Violence', content_adult: 'Adult', content_sex: 'Sexual' };

type UnityPackage = { platform?: string; unityVersion?: string; unitySortNumber?: number | string; variant?: string; performanceRating?: string; created_at?: string };

/**
 * The original's compareUnityVersion: whether a package's unitySortNumber is no newer than the SDK's
 * Unity version ("2022.3.6f1" is 2022030600000). No or a malformed SDK version lets nothing through.
 */
export function compareUnityVersion(unitySortNumber: number | string | undefined, sdkUnityVersion: string | null | undefined): boolean {
	if (!sdkUnityVersion) return false;
	const array = sdkUnityVersion.split('.');
	if (array.length < 3) return false;
	let current = array[0] + array[1].padStart(2, '0');
	const letter = array[2].search(/[a-zA-Z]/);
	if (letter > -1) current += array[2].slice(0, letter).padStart(2, '0') + '0' + (array[2][letter] === 'p' ? '1' : '0') + '0';
	else current += '000';
	current = current.replace(/\D/g, '');
	return parseInt(String(unitySortNumber), 10) <= parseInt(current, 10);
}

/**
 * "PC/2022.3.22f1" per platform and version, the newest first. Once the SDK's Unity version is known,
 * packages newer than it are left out (compareUnityVersion). The avatar pop-up also leaves out
 * impostors and other variants; the world pop-up does not.
 */
export function platformText(packages: UnityPackage[] | undefined, options: { sdkUnityVersion?: string | null; skipVariants?: boolean } = {}): string {
	const { sdkUnityVersion, skipVariants = true } = options;
	const seen: string[] = [];
	for (const p of packages ?? []) {
		if (skipVariants && p.variant && p.variant !== 'standard' && p.variant !== 'security') continue;
		if (sdkUnityVersion !== undefined && !compareUnityVersion(p.unitySortNumber, sdkUnityVersion)) continue;
		const name = p.platform === 'standalonewindows' ? 'PC' : p.platform === 'android' ? 'Android' : p.platform || 'PC';
		const text = `${name}/${p.unityVersion}`;
		if (!seen.includes(text)) seen.unshift(text);
	}
	return seen.join('\n');
}

/** The original's worldDialogPlatformCreatedAt: each platform's newest standard or security build date. */
export function platformCreatedAt(packages: UnityPackage[] | undefined): Record<string, string> | null {
	if (!packages) return null;
	const newest: Record<string, string> = {};
	for (const p of packages) {
		if (p.variant && p.variant !== 'standard' && p.variant !== 'security') continue;
		const platform = p.platform ?? '';
		const createdAt = p.created_at ?? '';
		if (!newest[platform] || new Date(createdAt) > new Date(newest[platform])) newest[platform] = createdAt;
	}
	return newest;
}

/** The original's getAvailablePlatforms: the platforms with a standard or security build. */
export function availablePlatforms(packages: UnityPackage[] | undefined): Set<string> {
	const found = new Set<string>();
	for (const p of packages ?? []) {
		if (p.variant && p.variant !== 'standard' && p.variant !== 'security') continue;
		if (p.platform) found.add(p.platform);
	}
	return found;
}

/** The original's getPlatformInfo for one platform: its last standard or security package, where a None rating does not replace a real one. */
export function platformPackage<T extends UnityPackage>(packages: T[] | undefined, platform: string): T | undefined {
	let found: T | undefined;
	for (const p of packages ?? []) {
		if (p.variant && p.variant !== 'standard' && p.variant !== 'security') continue;
		if (p.platform !== platform) continue;
		if (p.performanceRating === 'None' && found?.performanceRating) continue;
		found = p;
	}
	return found;
}

export const authorTagsText = (tags: string[]) => tags.filter((t) => t.startsWith('author_tag')).map((t) => t.replace('author_tag_', '')).join(', ');
export const contentTagsOf = (tags: string[]) => tags.filter((t) => t.startsWith('content_'));
export const contentLabel = (tag: string) => CONTENT_LABELS[tag] ?? tag.replace('content_', '');

/** The original's replaceVrcPackageUrl: a package on the API is downloaded through the website. */
export const replaceVrcPackageUrl = (url: string | null | undefined) => (url ? url.replace('https://api.vrchat.cloud/', 'https://vrchat.com/') : '');

/** Download Unity Package: the original's openExternalLink, in a new tab. */
export const openPackage = (url: string | null | undefined) => window.open(replaceVrcPackageUrl(url), '_blank', 'noopener,noreferrer');

/** A store listing row: like ROW, full width and not a link. */
export const LISTING_ROW = 'box-border flex w-full cursor-default items-center p-1.5 text-compact';
