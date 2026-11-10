// A port of the original's locationParser.js: VRChat location tags ("wrld_x:123~private(usr_y)~region(eu)")
// and the sentinels offline / private / traveling, plus the text, region and access-type label the
// Location component shows.

export type ParsedLocation = {
	tag: string;
	isOffline: boolean;
	isPrivate: boolean;
	isTraveling: boolean;
	isRealInstance: boolean;
	worldId: string;
	instanceId: string;
	instanceName: string;
	/** public, group, friends+, friends, invite+, invite */
	accessType: string;
	/** accessType, or groupPublic / groupPlus for group instances with that access */
	accessTypeName: string;
	region: string;
	shortName: string;
	userId: string | null;
	groupId: string | null;
	groupAccessType: string | null;
	canRequestInvite: boolean;
	strict: boolean;
	ageGate: boolean;
};

export function parseLocation(tag: string | undefined | null): ParsedLocation {
	let _tag = String(tag || '');
	const ctx: ParsedLocation = {
		tag: _tag,
		isOffline: false,
		isPrivate: false,
		isTraveling: false,
		isRealInstance: false,
		worldId: '',
		instanceId: '',
		instanceName: '',
		accessType: '',
		accessTypeName: '',
		region: '',
		shortName: '',
		userId: null,
		groupId: null,
		groupAccessType: null,
		canRequestInvite: false,
		strict: false,
		ageGate: false
	};
	if (_tag === 'offline' || _tag === 'offline:offline') {
		ctx.isOffline = true;
	} else if (_tag === 'private' || _tag === 'private:private') {
		ctx.isPrivate = true;
	} else if (_tag === 'traveling' || _tag === 'traveling:traveling') {
		ctx.isTraveling = true;
	} else if (tag && !_tag.startsWith('local')) {
		ctx.isRealInstance = true;
		const sep = _tag.indexOf(':');
		const shortNameQualifier = '&shortName=';
		const shortNameIndex = _tag.indexOf(shortNameQualifier);
		if (shortNameIndex >= 0) {
			ctx.shortName = _tag.substring(shortNameIndex + shortNameQualifier.length);
			_tag = _tag.substring(0, shortNameIndex);
		}
		if (sep >= 0) {
			ctx.worldId = _tag.substring(0, sep);
			ctx.instanceId = _tag.substring(sep + 1);
			let hiddenId: string | null = null;
			let privateId: string | null = null;
			let friendsId: string | null = null;
			ctx.instanceId.split('~').forEach((s, i) => {
				if (i) {
					const A = s.indexOf('(');
					const Z = A >= 0 ? s.lastIndexOf(')') : -1;
					const key = Z >= 0 ? s.substring(0, A) : s;
					const value = A < Z ? s.substring(A + 1, Z) : '';
					if (key === 'hidden') hiddenId = value;
					else if (key === 'private') privateId = value;
					else if (key === 'friends') friendsId = value;
					else if (key === 'canRequestInvite') ctx.canRequestInvite = true;
					else if (key === 'region') ctx.region = value;
					else if (key === 'group') ctx.groupId = value;
					else if (key === 'groupAccessType') ctx.groupAccessType = value;
					else if (key === 'strict') ctx.strict = true;
					else if (key === 'ageGate') ctx.ageGate = true;
				} else {
					ctx.instanceName = s;
				}
			});
			ctx.accessType = 'public';
			if (privateId !== null) {
				ctx.accessType = ctx.canRequestInvite ? 'invite+' : 'invite';
				ctx.userId = privateId;
			} else if (friendsId !== null) {
				ctx.accessType = 'friends';
				ctx.userId = friendsId;
			} else if (hiddenId !== null) {
				ctx.accessType = 'friends+';
				ctx.userId = hiddenId;
			} else if (ctx.groupId !== null) {
				ctx.accessType = 'group';
			}
			ctx.accessTypeName = ctx.accessType;
			if (ctx.groupAccessType === 'public') ctx.accessTypeName = 'groupPublic';
			else if (ctx.groupAccessType === 'plus') ctx.accessTypeName = 'groupPlus';
		} else {
			ctx.worldId = _tag;
		}
	}
	return ctx;
}

export function isRealInstance(location: string | undefined | null): boolean {
	return parseLocation(location).isRealInstance;
}

/** The flag shown next to a location: the region, or "us" for instances without one. */
export function resolveRegion(L: ParsedLocation): string {
	if (L.isOffline || L.isPrivate || L.isTraveling) return '';
	if (L.region) return L.region;
	if (L.instanceId) return 'us';
	return '';
}

const accessTypeLabels: Record<string, string> = {
	public: 'Public',
	group: 'Group',
	'friends+': 'Friends+',
	friends: 'Friends',
	'invite+': 'Invite+',
	invite: 'Invite',
	groupPublic: 'Group Public',
	groupPlus: 'Group Plus',
	groupMembers: 'Members'
};

export function accessTypeLabel(accessTypeName: string): string {
	return accessTypeLabels[accessTypeName] ?? accessTypeName;
}

/** The Location component's text: sentinel labels, or "World · Access". */
export function locationText(L: ParsedLocation, hint?: string, worldName?: string): string {
	if (L.isOffline) return 'Offline';
	if (L.isPrivate) return 'Private';
	if (L.isTraveling) return 'Traveling';
	const label = accessTypeLabel(L.accessTypeName);
	if (hint) return L.instanceId ? `${hint} · ${label}` : hint;
	if (L.worldId) {
		const name = worldName || L.worldId;
		return L.instanceId ? `${name} · ${label}` : name;
	}
	return '';
}
