import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { SvelteMap } from 'svelte/reactivity';
import { accountPath, api } from '#lib/api/client.js';
import type { Instance } from '#lib/api/types.js';
import { parseLocation, type ParsedLocation } from '#lib/instances/location.js';
import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
import { createInstanceBody, DEFAULT_INSTANCE_CHOICES, quickInstanceChoices, REGIONS, usableCategories, usableVibes, type InstanceCategory, type InstanceChoices, type InstanceVibe } from '#lib/instances/instance-create.js';

export { REGIONS, type InstanceChoices };

// The original's getLaunchURL, buildLegacyInstanceTag and launch store, for a web client: launching
// goes through the vrchat:// link, which VRChat's own launcher handles on this machine.

/** Instances known to be closed, with when: what the original reads from its cachedInstances' closedAt. */
export const closedInstances = new SvelteMap<string, string>();

/**
 * Who VRChat says owns an instance, for public instances whose tag names no creator (upstream
 * 24fd42c2's cachedInstances ownerId fallback). Group owners are left out.
 */
export const instanceOwners = new SvelteMap<string, string>();

/** The instance's creator: the tag's user, else the owner a fetch of the instance reported. */
export function instanceCreator(location: string, tagUserId?: string | null): string | undefined {
	return tagUserId || instanceOwners.get(location) || undefined;
}

/** Remembers an instance's closed state from any fetch of it, and returns it unchanged. */
export function noteInstance<T extends Pick<Instance, 'location' | 'closedAt'> | undefined>(instance: T): T {
	if (instance?.closedAt) closedInstances.set(instance.location, instance.closedAt);
	return instance;
}

/** Fetches an instance only to learn whether it has closed. */
export function checkClosed(location: string): void {
	if (!parseLocation(location).instanceId || closedInstances.has(location)) return;
	api.get<Instance>(`/1/instances/${encodeURIComponent(location)}`).then(noteInstance, () => {});
}

export type ShortNames = { shortName?: string | null; secureName?: string | null };

export function launchUrl(L: Pick<ParsedLocation, 'worldId' | 'instanceId'> & { shortName?: string }): string {
	const world = `https://vrchat.com/home/launch?worldId=${encodeURIComponent(L.worldId)}`;
	if (!L.instanceId) return world;
	const instance = `${world}&instanceId=${encodeURIComponent(L.instanceId)}`;
	return L.shortName ? `${instance}&shortName=${encodeURIComponent(L.shortName)}` : instance;
}

export function buildLegacyInstanceTag(options: {
	instanceName: string;
	userId: string;
	accessType: string;
	groupId?: string;
	groupAccessType?: string;
	region: string;
	ageGate?: boolean;
	strict?: boolean;
}): string {
	const { instanceName, userId, accessType, groupId, groupAccessType, region, ageGate, strict } = options;
	const tags: string[] = [];
	if (instanceName) tags.push(instanceName);
	if (accessType !== 'public') {
		if (accessType === 'friends+') tags.push(`~hidden(${userId})`);
		else if (accessType === 'friends') tags.push(`~friends(${userId})`);
		else if (accessType === 'group') {
			tags.push(`~group(${groupId})`);
			tags.push(`~groupAccessType(${groupAccessType})`);
		} else tags.push(`~private(${userId})`);
		if (accessType === 'invite+') tags.push('~canRequestInvite');
	}
	if (accessType === 'group' && ageGate) tags.push('~ageGate');
	const code = REGIONS.find((r) => r.value === region)?.code;
	if (code) tags.push(`~region(${code})`);
	if (strict && (accessType === 'invite' || accessType === 'friends')) tags.push('~strict');
	return tags.join('');
}

/** The desktop app the game runs next to: the page reaches VRChat through it. */
export const desktop = $state({
	connected: false,
	gameRunning: false,
	clientName: '',
	/** The game session times of the account's PC (the original's $online_for and last session). */
	gameStartedAt: null as string | null,
	lastGameStartedAt: null as string | null,
	lastGameStoppedAt: null as string | null,
	/** VRChat last started in desktop mode on that PC (the original's isGameNoVR). */
	gameNoVr: false,
	/** SteamVR runs on that PC (the original's isSteamVRRunning). */
	steamVrRunning: false,
	/** Nobody wears the headset (the agent's OpenVR, the original's isHmdAfk). */
	hmdAfk: false,
	/** What that PC's agent does (see AgentCapability); an older agent declares nothing. */
	capabilities: [] as AgentCapability[],
	os: null as string | null,
	/** Where VRChat keeps its files on that PC. */
	folders: null as AgentFolders | null
});

/** The ClientAction families an agent declares, as the server names them. */
export type AgentCapability =
	| 'launch'
	| 'open_instance'
	| 'screenshots'
	| 'ipc'
	| 'folders'
	| 'pick_path'
	| 'vrchat_config'
	| 'registry'
	| 'local_moderations'
	| 'cache'
	| 'save_media'
	| 'notifications'
	| 'discord'
	| 'calendar'
	| 'settings'
	| 'openvr';

export type AgentFolders = { appData: string; photos: string; screenshots: string; cache: string; ugcPhotos: string; crashDumps: string };

/** Whether the account's PC is connected and its agent does this. */
export const agentCan = (capability: AgentCapability): boolean => desktop.connected && desktop.capabilities.includes(capability);

export async function refreshDesktop(): Promise<void> {
	const owner = vrchatAuth.ownerUserId;
	if (!owner) return;
	try {
		const status = await api.get<{
			connected: boolean;
			gameRunning: boolean;
			clientName?: string | null;
			gameStartedAt?: string | null;
			lastGameStartedAt?: string | null;
			lastGameStoppedAt?: string | null;
			gameNoVr?: boolean;
			steamVrRunning?: boolean;
			hmdAfk?: boolean;
			capabilities?: string[];
			os?: string | null;
			folders?: AgentFolders | null;
		}>(accountPath(owner, '/desktop'));
		desktop.connected = status.connected;
		desktop.gameRunning = status.gameRunning;
		desktop.clientName = status.clientName ?? '';
		desktop.gameStartedAt = status.gameStartedAt ?? null;
		desktop.lastGameStartedAt = status.lastGameStartedAt ?? null;
		desktop.lastGameStoppedAt = status.lastGameStoppedAt ?? null;
		desktop.gameNoVr = status.gameNoVr ?? false;
		desktop.steamVrRunning = status.steamVrRunning ?? false;
		desktop.hmdAfk = status.hmdAfk ?? false;
		desktop.capabilities = (status.capabilities ?? []) as AgentCapability[];
		desktop.os = status.os ?? null;
		desktop.folders = status.folders ?? null;
	} catch {
		desktop.connected = false;
		desktop.gameRunning = false;
		desktop.steamVrRunning = false;
		desktop.hmdAfk = false;
		desktop.capabilities = [];
	}
}

/** The original's canOpenInstanceInGame. */
export const canOpenInGame = (): boolean => desktop.connected && desktop.gameRunning;

/** The original's launchGame, on the desktop app's PC. Throws Desktop.GameRunning unless a second copy is asked for. */
export async function launchGame(location: string, shortName: string | null | undefined, options: { desktopMode: boolean; allowSecondInstance: boolean }): Promise<void> {
	const url = await gameLaunchLink(location, shortName);
	await api.post(ownPath('/game/launch'), { url, ...options });
}

// The original's isOpeningInstance: a second click while one is opening, or within a second of it,
// is ignored, since two quick opens hang VRChat (upstream issue 1384).
let openingInstance = false;

/** The original's tryOpenInstanceInVrc: falls back to a self invite. Returns which of the two happened, or 'ignored' during the guard. */
export async function openInGame(location: string, shortName?: string | null): Promise<'opened' | 'invited' | 'ignored'> {
	if (openingInstance) return 'ignored';
	openingInstance = true;
	try {
		const url = await gameLaunchLink(location, shortName);
		await api.post(ownPath('/game/open'), { url });
		return 'opened';
	} catch {
		await selfInvite(location, shortName);
		return 'invited';
	} finally {
		setTimeout(() => (openingInstance = false), 1000);
	}
}

/** Open it in the running game when that can be done, else send the self invite. Returns what to tell the user. */
export async function joinInstance(location: string, shortName?: string | null): Promise<string> {
	if (!canOpenInGame()) {
		await selfInvite(location, shortName);
		return 'Self invite sent';
	}
	const result = await openInGame(location, shortName);
	if (result === 'ignored') return '';
	return result === 'opened' ? 'Opened in VRChat' : 'Could not open it in VRChat, self invite sent';
}

const LAUNCH_MODE_KEY = 'vrcx.launchAsDesktop';

export const loadLaunchAsDesktop = (): boolean => readSetting<boolean>(LAUNCH_MODE_KEY, false) === true;
export const saveLaunchAsDesktop = (value: boolean): void => storeSetting(LAUNCH_MODE_KEY, value);

export function getShortNames(location: string): Promise<ShortNames> {
	return api.get<ShortNames>(ownPath('/instances/short-name'), { location });
}

export function selfInvite(location: string, shortName?: string | null): Promise<unknown> {
	return api.post(ownPath('/instances/self-invite'), { location, shortName: shortName || null });
}

/** The original's getLaunchUrl: a closed instance needs a short or secure name, and a public one may have none. */
export async function gameLaunchLink(location: string, shortName?: string | null): Promise<string> {
	const L = parseLocation(location);
	if (shortName && L.accessType !== 'public' && L.groupAccessType !== 'public') {
		return `vrchat://launch?ref=vrcx.app&id=${location}&shortName=${shortName}`;
	}
	const names = await getShortNames(location).catch((): ShortNames => ({}));
	const name = names.shortName || names.secureName;
	return name ? `vrchat://launch?ref=vrcx.app&id=${location}&shortName=${name}` : `vrchat://launch?ref=vrcx.app&id=${location}`;
}

/** The original's checkCanInviteSelf. */
export function canInviteSelf(location: string, ownUserId: string, friendIds?: ReadonlySet<string>): boolean {
	if (!location || closedInstances.has(location)) return false;
	const L = parseLocation(location);
	if (L.userId === ownUserId) return true;
	return !(L.accessType === 'friends' && friendIds && !friendIds.has(L.userId ?? ''));
}

export type CreatedInstance = { location: string; instanceId: string; shortName?: string | null; secureName?: string | null };

const CHOICES_KEY = 'vrcx.newInstance';

/** The New Instance choices, remembered between visits as the original's config keys remember them. */
export function loadInstanceChoices(): InstanceChoices {
	return { ...DEFAULT_INSTANCE_CHOICES, ...readSetting<Partial<InstanceChoices>>(CHOICES_KEY, {}) };
}

export function saveInstanceChoices(choices: InstanceChoices, ownUserId: string): void {
	storeSetting(CHOICES_KEY, { ...choices, legacyUserId: choices.legacyUserId === ownUserId ? '' : choices.legacyUserId });
}

/** The original's createNewInstance. */
export function createInstance(worldId: string, choices: InstanceChoices, extra: { roleIds?: string[]; canAgeGate?: boolean } = {}): Promise<CreatedInstance> {
	return api.post<CreatedInstance>(ownPath('/instances'), createInstanceBody(worldId, choices, extra));
}

/** The world menu's New Instance and Self Invite: the remembered choices, without the dialog. */
export async function createAndSelfInvite(worldId: string): Promise<string> {
	const created = await createInstance(worldId, quickInstanceChoices(loadInstanceChoices()));
	return joinInstance(created.location, created.shortName);
}

/** The original's checkCanInvite. */
export function canInvite(location: string, ownUserId: string, currentLocation?: string | null): boolean {
	if (!location || closedInstances.has(location)) return false;
	const L = parseLocation(location);
	if (L.accessType === 'public' || L.accessType === 'group' || L.userId === ownUserId) return true;
	if (L.accessType === 'invite' || L.accessType === 'friends') return false;
	return currentLocation === location;
}

/** The original's instanceCategories and instanceVibes: VRChat's lists, loaded once per sign-in. */
export const instanceCatalog = $state<{ categories: InstanceCategory[]; vibes: InstanceVibe[] }>({ categories: [], vibes: [] });
let catalogFor = '';

/** The original's loadInstanceCategoriesAndVibes: each list is fetched until one fetch of it succeeds. */
export async function loadInstanceCategoriesAndVibes(): Promise<void> {
	const owner = vrchatAuth.ownerUserId;
	if (!owner) return;
	if (catalogFor !== owner) {
		catalogFor = owner;
		instanceCatalog.categories = [];
		instanceCatalog.vibes = [];
	}
	const loads: Promise<void>[] = [];
	if (instanceCatalog.categories.length === 0)
		loads.push(api.get<InstanceCategory[]>(accountPath(owner, '/instances/categories')).then((list) => { if (catalogFor === owner) instanceCatalog.categories = usableCategories(list); }, () => {}));
	if (instanceCatalog.vibes.length === 0)
		loads.push(api.get<InstanceVibe[]>(accountPath(owner, '/instances/vibes')).then((list) => { if (catalogFor === owner) instanceCatalog.vibes = usableVibes(list); }, () => {}));
	await Promise.all(loads);
}

export const instanceDialogs = $state<{
	newInstance?: { worldId: string };
	launch?: { location: string; shortName?: string | null };
	invite?: { location: string };
}>({});
