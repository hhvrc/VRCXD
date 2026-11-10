// The original's createNewInstance request and the New Instance choices, kept free of the app's state.

export const REGIONS = [
	{ value: 'US West', code: 'us' },
	{ value: 'US East', code: 'use' },
	{ value: 'Europe', code: 'eu' },
	{ value: 'Japan', code: 'jp' }
] as const;

export type InstanceChoices = {
	accessType: string;
	region: string;
	instanceName: string;
	legacyUserId: string;
	groupId: string;
	groupAccessType: string;
	queueEnabled: boolean;
	ageGate: boolean;
	displayName: string;
	minimumAvatarPerformance: string;
	description: string;
	languages: string[];
	categoryId: string;
	vibeIds: string[];
};

export const DEFAULT_INSTANCE_CHOICES: InstanceChoices = {
	accessType: 'public',
	region: 'US West',
	instanceName: '',
	legacyUserId: '',
	groupId: '',
	groupAccessType: 'plus',
	queueEnabled: true,
	ageGate: false,
	displayName: '',
	minimumAvatarPerformance: '',
	description: '',
	languages: [],
	categoryId: '',
	vibeIds: []
};

/** VRChat allows three languages and three vibes on an instance. */
export const MAX_INSTANCE_TAGS = 3;

const TYPE_FOR_ACCESS: Record<string, string> = { friends: 'friends', 'friends+': 'hidden', invite: 'private', 'invite+': 'private', group: 'group' };

export type CreateInstanceBody = {
	worldId: string;
	type: string;
	canRequestInvite: boolean;
	region: string;
	groupId: string | null;
	groupAccessType: string;
	queueEnabled: boolean;
	roleIds: string[];
	minimumAvatarPerformance: string;
	ageGate: boolean;
	displayName: string | null;
	description: string | null;
	languages: string[];
	categoryId: string | null;
	vibeIds: string[];
};

/**
 * What the server's create endpoint is sent. canAgeGate is the group's age-gated create permission for
 * a group instance; for any other instance the server checks the account is 18+ verified.
 */
export function createInstanceBody(worldId: string, choices: InstanceChoices, extra: { roleIds?: string[]; canAgeGate?: boolean } = {}): CreateInstanceBody {
	const type = TYPE_FOR_ACCESS[choices.accessType] ?? 'public';
	const canAgeGate = extra.canAgeGate ?? type !== 'group';
	return {
		worldId,
		type,
		canRequestInvite: choices.accessType === 'invite+',
		region: REGIONS.find((r) => r.value === choices.region)?.code ?? 'us',
		groupId: choices.groupId || null,
		groupAccessType: choices.groupAccessType,
		queueEnabled: choices.queueEnabled,
		roleIds: extra.roleIds ?? [],
		minimumAvatarPerformance: choices.minimumAvatarPerformance === 'None' ? '' : choices.minimumAvatarPerformance,
		ageGate: choices.ageGate && canAgeGate,
		displayName: choices.displayName || null,
		description: choices.description || null,
		languages: (choices.languages ?? []).slice(0, MAX_INSTANCE_TAGS),
		categoryId: choices.categoryId || null,
		vibeIds: (choices.vibeIds ?? []).slice(0, MAX_INSTANCE_TAGS)
	};
}

/**
 * The world menu's quick create: the original's createNewInstance without options reads only the
 * access type, region, group, group access, queue and age gate choices.
 */
export function quickInstanceChoices(choices: InstanceChoices): InstanceChoices {
	const { accessType, region, groupId, groupAccessType, queueEnabled, ageGate } = choices;
	return { ...DEFAULT_INSTANCE_CHOICES, accessType, region, groupId, groupAccessType, queueEnabled, ageGate };
}

export type InstanceCategory = { id: string; name: string; iconUrl?: string | null; order?: number; deleted?: boolean };
export type InstanceVibe = { id: string; title: string; deleted?: boolean };

/** The original's instanceCategoryIcons: the Remix icon for each category's icon URL. */
export const INSTANCE_CATEGORY_ICONS: Record<string, string> = {
	'vrchat://category_hangout': 'ri-goblet-line',
	'vrchat://category_music_shows': 'ri-music-2-line',
	'vrchat://category_games': 'ri-gamepad-line',
	'vrchat://category_roleplay': 'ri-emotion-line',
	'vrchat://category_avatars': 'ri-user-3-line',
	'vrchat://category_explore': 'ri-sailboat-line',
	'vrchat://category_watch_party': 'ri-vidicon-line',
	'vrchat://category_other': 'ri-apps-2-line'
};

/** The original's loadInstanceCategoriesAndVibes filtering: deleted ones dropped, categories in their order. */
export function usableCategories(list: InstanceCategory[] | null | undefined): InstanceCategory[] {
	return (list ?? []).filter((c) => !c.deleted).sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
}

export function usableVibes(list: InstanceVibe[] | null | undefined): InstanceVibe[] {
	return (list ?? []).filter((v) => !v.deleted);
}
