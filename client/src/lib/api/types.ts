// Response shapes of the server's REST API (API/Models/Response).

export type LoginResponse = {
	status: 'loggedIn' | 'needsTwoFactor';
	userId?: string;
	displayName?: string;
	pendingId?: string;
	methods?: string[];
};

export type Account = {
	userId: string;
	displayName: string;
	username?: string;
	sessionValid: boolean;
	apiEndpoint?: string | null;
	pipelineEndpoint?: string | null;
	connected: boolean;
	loggedInAt: string;
	lastFriendSyncAt?: string;
	lastFullSyncAt?: string;
	homeLocation?: string;
	thumbnailImageUrl?: string | null;
	iconFrame?: string | null;
	/** A password is saved (encrypted on the server) for logging in again; never the password itself. */
	hasSavedCredentials?: boolean;
	/** That password is encrypted with the primary password, so using it asks for it. */
	savedCredentialsNeedPrimaryPassword?: boolean;
};

export type UserState = 'offline' | 'active' | 'online';

export type Friend = {
	friendNumber: number;
	friendsSince: string;
	userId: string;
	displayName: string;
	trustLevel: string;
	state: UserState;
	status: string;
	statusDescription?: string;
	location?: string;
	travelingToLocation?: string | null;
	locationSince?: string | null;
	/** When they last came online, went active or went offline. */
	onlineFor?: string | null;
	lastActivity?: string | null;
	lastLogin?: string | null;
	thumbnailImageUrl?: string;
	tags?: string[];
	developerType?: string;
	platform?: string | null;
	nickname?: string | null;
	pendingOffline?: boolean;
	nameHue?: number;
	/** The icon frame VRChat shows around their picture (an inventory template id). */
	iconFrame?: string | null;
	/** Their profile effect template id, behind the Friends Locations card. */
	profileEffect?: string | null;
};

export type FriendDetail = {
	friendNumber: number;
	userId: string;
	displayName: string;
	trustLevel: string;
	tags: string[];
	developerType: string;
	status: string;
	statusDescription?: string | null;
	thumbnailImageUrl?: string | null;
	bio?: string | null;
	bioLinks: string[];
	note?: string | null;
	memo?: string | null;
	joinCount: number;
	timeTogetherMs: number;
	lastSeen?: string | null;
	mutualCount: number;
	mutualOptedOut: boolean;
	lastActivity?: string | null;
	lastLogin?: string | null;
	dateJoined?: string | null;
	isFavorite: boolean;
	needsLoad: boolean;
	nameHue: number;
};

export type FriendDetails = { friends: FriendDetail[]; mutualOptOut: boolean };

export type FriendLogEntry = {
	key: string;
	type: string;
	createdAt: string;
	userId: string;
	displayName: string;
	previousDisplayName?: string;
	trustLevel?: string;
	previousTrustLevel?: string;
	friendNumber?: number;
};

/** A badge on a user's profile, as VRChat's profile/{id} lists it. */
export type UserBadge = {
	badgeId: string;
	badgeName: string;
	badgeDescription: string;
	badgeImageUrl: string;
	showcased: boolean;
	hidden?: boolean | null;
	assignedAt?: string | null;
	updatedAt?: string | null;
};

export type User = {
	id: string;
	displayName: string;
	/** Cosmetics VRChat shows on the user: inventory template ids. */
	iconFrame?: string;
	nameplateEffect?: string;
	profileEffect?: string;
	discordId?: string;
	ageVerified?: boolean;
	bio?: string;
	bioLinks: string[];
	pronouns?: string;
	status: string;
	statusDescription?: string;
	state: UserState;
	tags: string[];
	trustLevel: string;
	developerType: string;
	currentAvatarImageUrl?: string;
	currentAvatarThumbnailImageUrl?: string;
	profilePicOverride?: string;
	userIcon?: string;
	iconUrl?: string;
	platform?: string;
	lastPlatform?: string;
	lastLogin?: string;
	lastActivity?: string;
	dateJoined?: string;
	ageVerificationStatus?: string;
	allowAvatarCopying: boolean;
	location?: string;
	travelingToLocation?: string;
	currentInstanceLocation?: string;
	presenceChangedAt?: string;
	/** When they last came online, went active or went offline. */
	stateSince?: string;
	isStub: boolean;
	firstSeenAt: string;
	updatedAt: string;
	lastFullFetchAt?: string;
	badges?: UserBadge[];
};

export type UserHistory = {
	displayNames: { changedAt: string; previousDisplayName: string; displayName: string }[];
	trust: { changedAt: string; previousTrustLevel: string; trustLevel: string }[];
	status: {
		changedAt: string;
		status: string;
		statusDescription?: string;
		previousStatus: string;
		previousStatusDescription?: string;
	}[];
	bio: { changedAt: string; bio?: string; previousBio?: string }[];
	avatars: { changedAt: string; imageUrl?: string; thumbnailImageUrl?: string; previousImageUrl?: string }[];
	presence: {
		at: string;
		kind: string;
		location?: string;
		previousLocation?: string;
		instanceLocation?: string;
		platform?: string;
	}[];
	visits: Visit[];
};

export type Visit = {
	id?: number;
	userId?: string;
	displayName?: string;
	instanceLocation: string;
	worldId?: string;
	worldName: string;
	accessType?: string;
	joinedAt: string;
	leftAt?: string;
	source: string;
};

export type World = {
	id: string;
	name: string;
	description?: string;
	authorId?: string;
	authorName?: string;
	capacity?: number;
	imageUrl?: string;
	thumbnailImageUrl?: string;
	releaseStatus?: string;
	tags: string[];
	visits?: number;
	favorites?: number;
	platforms: string[];
	isStub: boolean;
	createdAt?: string;
	updatedAt?: string;
	fetchedAt?: string;
};

export type Instance = {
	location: string;
	worldId: string;
	worldName: string;
	instanceName: string;
	type: string;
	region?: string;
	ownerUserId?: string;
	groupId?: string;
	name?: string;
	capacity?: number;
	closedAt?: string | null;
	firstSeenAt: string;
	fetchedAt?: string;
	present: { userId: string; displayName: string; joinedAt: string; source: string }[];
};

export type Notification = {
	id: string;
	version: number;
	type: string;
	senderUserId?: string;
	senderDisplayName?: string;
	title?: string;
	message: string;
	imageUrl?: string;
	link?: string;
	linkText?: string | null;
	details?: Record<string, unknown> | string | null;
	responses?: { type: string; data: string; text: string; icon?: string | null }[] | null;
	seen: boolean;
	hidden: boolean;
	createdAt: string;
	expiresAt?: string;
};

export type PlayerModeration = {
	id: string;
	sourceUserId: string;
	sourceDisplayName: string;
	targetUserId: string;
	targetDisplayName: string;
	type: string;
	createdAt?: string;
};

export type Favorite = {
	id: string;
	type: 'friend' | 'world' | 'avatar';
	targetId: string;
	targetName: string;
	thumbnailImageUrl?: string;
	groupName?: string;
	groupDisplayName?: string;
};

export type FavoriteGroup = {
	id: string;
	type: string;
	name: string;
	displayName: string;
	visibility: string;
};

export type Favorites = { groups: FavoriteGroup[]; favorites: Favorite[] };

export type Memo = { userId: string; memo: string; updatedAt: string };

// GET /1/accounts/{id}/feed: the original's five feed tables unioned, newest first.
export type FeedType = 'GPS' | 'Online' | 'Offline' | 'Status' | 'Avatar' | 'Bio';
export const FEED_TYPES: FeedType[] = ['GPS', 'Online', 'Offline', 'Status', 'Avatar', 'Bio'];

export type FeedEntry = {
	type: FeedType;
	rowId: number;
	createdAt: string;
	userId: string;
	displayName: string;
	location?: string | null;
	worldName?: string | null;
	groupName?: string | null;
	previousLocation?: string | null;
	previousWorldName?: string | null;
	/** Milliseconds at the previous location (GPS) or at the location left (Offline). */
	time?: number | null;
	status?: string | null;
	statusDescription?: string | null;
	previousStatus?: string | null;
	previousStatusDescription?: string | null;
	bio?: string | null;
	previousBio?: string | null;
	bioLinks?: string[] | null;
	previousBioLinks?: string[] | null;
	ownerId?: string | null;
	previousOwnerId?: string | null;
	avatarName?: string | null;
	previousAvatarName?: string | null;
	currentAvatarImageUrl?: string | null;
	currentAvatarThumbnailImageUrl?: string | null;
	previousCurrentAvatarImageUrl?: string | null;
	previousCurrentAvatarThumbnailImageUrl?: string | null;
};

// ---- charts ----------------------------------------------------------------------------
export type ActivityVisit = {
	id: number;
	userId: string;
	displayName: string;
	location: string;
	worldName: string;
	joinedAt: string;
	leftAt: string;
	time: number;
	isFriend: boolean | null;
	isFavorite: boolean | null;
};

export type InstanceActivity = {
	currentUserData: ActivityVisit[];
	detailData: Record<string, ActivityVisit[]>;
};

export type HotWorld = {
	worldId: string;
	worldName: string;
	visitCount: number;
	uniqueFriends: number;
	lastVisited: string;
	trend: 'rising' | 'cooling' | 'stable';
};

export type HotWorldFriend = { userId: string; displayName: string; visitCount: number; lastVisit: string };

export type MutualGraphFetchStatus = {
	running: boolean;
	cancelRequested: boolean;
	processed: number;
	total: number;
	lastCompletedAt: string | null;
};

export type MutualGraphSnapshot = {
	friends: Record<string, string[]>;
	meta: Record<string, { lastFetchedAt: string; optedOut: boolean }>;
	names: Record<string, string>;
	optedOut: boolean;
	fetch: MutualGraphFetchStatus;
};

export type MutualRefreshResult = { success: boolean; optedOut: boolean };

// GET/PUT /1/accounts/{id}/settings: the original's app settings, kept per account.
export type AccountSettings = {
	screenshotHelper: boolean;
	screenshotHelperModifyFilename: boolean;
	screenshotHelperCopyToClipboard: boolean;
	youtubeApi: boolean;
	youtubeApiKey: string;
	vrcQuitFix: boolean;
	logResourceLoad: boolean;
	udonExceptionLogging: boolean;
	autoStateChangeEnabled: boolean;
	autoStateChangeAloneStatus: string;
	autoStateChangeCompanyStatus: string;
	autoStateChangeInstanceTypes: string[];
	autoStateChangeNoFriends: boolean;
	autoStateChangeAloneDescEnabled: boolean;
	autoStateChangeAloneDesc: string;
	autoStateChangeCompanyDescEnabled: boolean;
	autoStateChangeCompanyDesc: string;
	autoStateChangeGroups: string[];
	autoAcceptInviteRequests: string;
	autoAcceptInviteGroups: string[];
	autoAcceptInviteStatuses: string[];
	autoDeclineFriendRequests: boolean;
	autoDeleteOldPrints: boolean;
	relaunchVrchatAfterCrash: boolean;
	autoSweepVrchatCache: boolean;
	selfInviteOverride: boolean;
	ugcFolderPath: string;
	saveInstancePrints: boolean;
	cropInstancePrints: boolean;
	saveInstanceStickers: boolean;
	saveInstanceEmoji: boolean;
	vrcRegistryAutoBackup: boolean;
	vrcRegistryAskRestore: boolean;
	enableAppLauncher: boolean;
	enableAppLauncherAutoClose: boolean;
	enableAppLauncherRunProcessOnce: boolean;
	discordActive: boolean;
	discordInstance: boolean;
	discordHideInvite: boolean;
	discordJoinButton: boolean;
	discordHideImage: boolean;
	discordShowPlatform: boolean;
	discordWorldIntegration: boolean;
	discordWorldNameAsDiscordStatus: boolean;
	discordMediaImage: boolean;
	/** The Photon lobby's timeout threshold in ms (VRCX_photonLobbyTimeoutThreshold). */
	photonLobbyTimeoutThreshold: number;
	/** Users whose chatbox messages are hidden, by id with their name. */
	chatboxUserBlacklist: Record<string, string>;
	chatboxBlacklist: string[];
	photonEventTypeFilter: string[];
	/** The Photon event and timeout HUDs on the agent's SteamVR overlay. */
	photonEventOverlay: boolean;
	photonEventOverlayFilter: string;
	photonEventTypeOverlayFilter: string[];
	/** ms */
	photonOverlayMessageTimeout: number;
	timeoutHudOverlay: boolean;
	timeoutHudOverlayFilter: string;
	/** The video progress pie on the agent's SteamVR overlay, only in dance worlds when filtered. */
	progressPie: boolean;
	progressPieFilter: boolean;
	/** OpenVR on the PC (the original's openVR), and whether it also runs with VRChat closed. */
	openVr: boolean;
	openVrAlways: boolean;
	overlayWrist: boolean;
	overlayButton: boolean;
	/** 0 either hand, 1 left, 2 right. */
	overlayHand: number;
	favoriteFriendGroups: string[];
	/** The original's VRCX_avatarAutoCleanup: Off, 30, 90, 180 or 365 days. */
	avatarAutoCleanup: string;
	/** When the server last ran the auto-cleanup; it ignores what the client sends. */
	lastAvatarCleanupAt?: string | null;
	autoLoginDelayEnabled: boolean;
	autoLoginDelaySeconds: number;
};

// ---- my avatars -----------------------------------------------------------------------
export type Avatar = {
	id: string;
	name: string;
	description?: string | null;
	authorId?: string | null;
	authorName?: string | null;
	imageUrl?: string | null;
	thumbnailImageUrl?: string | null;
	releaseStatus?: string | null;
	tags: string[];
	version?: number | null;
	performancePc?: string | null;
	performanceAndroid?: string | null;
	performanceIos?: string | null;
	platforms: string[];
	/** The impostorizer version of the avatar's last impostor package (My Avatars' Impostor column). */
	impostorVersion?: string | null;
	createdAt?: string | null;
	updatedAt?: string | null;
	isStub: boolean;
	fetchedAt?: string | null;
};

export type OwnAvatarTag = { tag: string; color: string | null };

export type OwnAvatar = {
	avatar: Avatar;
	tags: OwnAvatarTag[];
	timeSpentMs: number;
	isCurrent: boolean;
};
