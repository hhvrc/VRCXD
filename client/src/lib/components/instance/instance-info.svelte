<script lang="ts" module>
	import { accountPath, api } from '#lib/api/client.js';

	// VRChat's instance as it sends it, fetched once a minute at most per location, as the original
	// keeps one cachedInstances entry each view reads.
	export type LiveInstance = {
		location?: string;
		ownerId?: string | null;
		userCount?: number;
		capacity?: number;
		hasCapacityForYou?: boolean;
		platforms?: { standalonewindows?: number; android?: number; ios?: number };
		languages?: string[];
		languagesIso639?: string[];
		languageRatio?: Record<string, number>;
		gameServerVersion?: number;
		queueEnabled?: boolean;
		queueSize?: number;
		ageGate?: boolean;
		roleRestricted?: boolean;
		calendarEntryId?: string | null;
		minimumAvatarPerformance?: string | null;
		contentSettings?: Record<string, boolean>;
		displayName?: string | null;
		description?: string | null;
		categoryId?: string | null;
		vibeIds?: string[];
		creationLanguages?: string[];
		world?: { description?: string | null } | null;
		closedAt?: string | null;
		hypeTrain?: { current?: { currentGiftCount?: number; totalGiftGoal?: number; totalGiftContributors?: number; startDate?: string; endDate?: string } | null } | null;
	};
	type Live = { instance: LiveInstance; groupPermissions?: string[] | null; inThisInstance?: boolean; lastJoinedAt?: string | null };

	const cache = new Map<string, { at: number; promise: Promise<Live | null> }>();

	export function fetchLiveInstance(account: string, location: string, force = false): Promise<Live | null> {
		const key = `${account}|${location}`;
		const hit = cache.get(key);
		if (!force && hit && Date.now() - hit.at < 60_000) return hit.promise;
		const promise = api.get<Live>(accountPath(account, '/instances/live'), { location }).catch(() => null);
		cache.set(key, { at: Date.now(), promise });
		return promise;
	}

	/** The original's instanceContentSettings: what an instance can turn off. */
	const CONTENT_SETTINGS = ['emoji', 'stickers', 'pedestals', 'prints', 'drones', 'props'];

	/** The original's computeDisabledContentSettings. */
	export function disabledContent(settings: Record<string, boolean> | undefined): string[] {
		if (!settings || Object.keys(settings).length === 0) return [];
		return CONTENT_SETTINGS.filter((s) => settings[s] === false);
	}
</script>

<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import CalendarDays from '@lucide/svelte/icons/calendar-days';
	import CalendarPlus from '@lucide/svelte/icons/calendar-plus';
	import MapPin from '@lucide/svelte/icons/map-pin';
	import Megaphone from '@lucide/svelte/icons/megaphone';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Loader2 from '@lucide/svelte/icons/loader-2';
	import CalendarEventDetails from '#lib/components/tools/calendar-event-details.svelte';
	import type { CalendarEvent } from '#lib/nav/tools.js';
	import InstanceAnnouncementDialog from '#lib/components/instance/instance-announcement-dialog.svelte';
	import InstanceCalendarLinkDialog from '#lib/components/instance/instance-calendar-link-dialog.svelte';
	import IdCard from '@lucide/svelte/icons/id-card';
	import Pencil from '@lucide/svelte/icons/pencil';
	import Power from '@lucide/svelte/icons/power';
	import SquareStack from '@lucide/svelte/icons/square-stack';
	import Star from '@lucide/svelte/icons/star';
	import TrainFront from '@lucide/svelte/icons/train-front';
	import UserLock from '@lucide/svelte/icons/user-lock';
	import UserPlus2 from '@lucide/svelte/icons/user-plus-2';
	import UsersRound from '@lucide/svelte/icons/users-round';
	import { toast } from 'svelte-sonner';
	import ActionDialogs, { type Confirming, type Prompting } from '#lib/components/dialogs/action-dialogs.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { hasPermission } from '#lib/groups/group-moderation.svelte.js';
	import { closedInstances, instanceCatalog, instanceOwners, loadInstanceCategoriesAndVibes } from '#lib/instances/instance-launch.svelte.js';
	import { INSTANCE_CATEGORY_ICONS } from '#lib/instances/instance-create.js';
	import { instanceDescription, instanceLanguageList } from '#lib/instances/instance-details.js';
	import { languageOptions } from '#lib/groups/group-dialog.svelte.js';
	import { isRealInstance } from '#lib/instances/location.js';
	import { languageClass } from '#lib/people/user-display.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The info half of the original's InstanceActionBar: Rename and Close beside the other buttons,
	// players against capacity with the instance's details on hover (description, platforms, category,
	// vibes, languages, game version, queue, disabled content), then friends here, queue size, age gate,
	// role restriction, calendar event, hype train and the minimum avatar performance.
	// showRefresh: the original's Refresh Instance Info button, which the user and group pop-ups show (upstream issue 1394).
	let { location, friendCount = 0, showRefresh = false, class: className = '' }: { location: string; friendCount?: number; showRefresh?: boolean; class?: string } = $props();

	let live = $state<Live | null>(null);
	let confirming = $state<Confirming | undefined>();
	let prompting = $state<Prompting | undefined>();
	const now = $derived(clock(1000).now);

	const account = $derived(vrchatAuth.ownerUserId ?? '');

	$effect(() => {
		const [who, where] = [account, location];
		live = null;
		if (!who || !isRealInstance(where)) return;
		void fetchLiveInstance(who, where).then((result) => {
			if (location === where) apply(result);
		});
	});

	function apply(result: Live | null) {
		live = result;
		if (result?.instance.closedAt) closedInstances.set(location, result.instance.closedAt);
		const owner = result?.instance.ownerId;
		if (owner && !owner.startsWith('grp_')) instanceOwners.set(location, owner);
	}

	const instance = $derived(live?.instance);
	const ownerId = $derived(instance?.ownerId ?? '');
	const isOwn = $derived(!!ownerId && ownerId === account);
	const permissions = $derived(live?.groupPermissions ?? []);
	const canClose = $derived(isOwn || (ownerId.startsWith('grp_') && hasPermission(permissions, 'group-instance-moderate')));
	const canRename = $derived(isOwn || (ownerId.startsWith('grp_') && hasPermission(permissions, 'group-instance-manage')));
	const isGroup = $derived(ownerId.startsWith('grp_'));
	// The original's canSendAnnouncement needs the game in this very instance; canLinkCalendarEvent does not.
	const canAnnounce = $derived(isGroup && hasPermission(permissions, 'group-instance-announcement-create') && live?.inThisInstance === true);
	const canLinkCalendar = $derived(isGroup && hasPermission(permissions, 'group-instance-manage') && hasPermission(permissions, 'group-instance-calendar-link'));
	let announceOpen = $state(false);
	let calendarLinkOpen = $state(false);
	const ageGated = $derived(instance?.ageGate === true || location.includes('~ageGate'));
	const disabled = $derived(disabledContent(instance?.contentSettings).join(', '));
	const performance = $derived(instance?.minimumAvatarPerformance && instance.minimumAvatarPerformance !== 'None' ? instance.minimumAvatarPerformance : '');
	const train = $derived(instance?.hypeTrain?.current ?? null);

	const lastJoinedAt = $derived(live?.lastJoinedAt ?? undefined);

	// VRChat's names for its language keys (the original's subsetOfLanguages), else the browser's.
	let languageNames = $state<Record<string, string>>({});
	$effect(() => {
		if (account) languageOptions(account).then((names) => (languageNames = names));
	});
	const languages = $derived.by(() => {
		const names = new Intl.DisplayNames(['en'], { type: 'language', fallback: 'code' });
		return instanceLanguageList(instance, (key, code) => {
			if (languageNames[key]) return languageNames[key];
			try {
				return names.of(code.split('-')[0]) ?? code;
			} catch {
				// Not a code the browser knows.
				return code;
			}
		});
	});
	const description = $derived(instanceDescription(instance));
	const category = $derived.by(() => {
		const found = instanceCatalog.categories.find((c) => c.id === instance?.categoryId);
		return found ? { name: found.name, icon: found.iconUrl ? INSTANCE_CATEGORY_ICONS[found.iconUrl] : undefined } : null;
	});
	const vibeTitles = $derived.by(() => {
		const byId = new Map(instanceCatalog.vibes.map((v) => [v.id, v.title]));
		return (instance?.vibeIds ?? []).map((id) => byId.get(id) ?? id).join(', ');
	});
	// The names come from lists loaded at sign-in; a failed load is tried again here.
	$effect(() => {
		if (instance?.categoryId || instance?.vibeIds?.length) void loadInstanceCategoriesAndVibes();
	});

	/** The original's formatSeconds: mm:ss, with hh: when there are hours. */
	function formatSeconds(duration: number): string {
		const pad = (n: number) => String(n).padStart(2, '0');
		const hours = Math.floor(duration / 3600);
		const minutes = Math.floor(duration / 60) % 60;
		const seconds = Math.floor(duration) % 60;
		return `${hours > 0 ? `${pad(hours)}:` : ''}${pad(minutes)}:${pad(seconds)}`;
	}

	let calendarEvent = $state<CalendarEvent | null>(null);
	let calendarEventFailed = $state(false);

	/** The original's fetchCalendarEvent: once per linked event; a failure (not in the group) shows the plain label. */
	async function loadCalendarEvent() {
		const eventId = instance?.calendarEntryId;
		if (!eventId || !ownerId || calendarEvent?.id === eventId) return;
		calendarEventFailed = false;
		try {
			const event = await api.get<CalendarEvent>(accountPath(account, `/groups/${encodeURIComponent(ownerId)}/calendar/${encodeURIComponent(eventId)}`));
			if (instance?.calendarEntryId === eventId) calendarEvent = event;
		} catch {
			calendarEventFailed = true;
		}
	}

	/** The original's Timer: time since, as timeToText. */
	function relative(iso: string | undefined): string {
		const at = iso ? Date.parse(iso) : NaN;
		if (!at) return '-';
		return timeToText(now - at);
	}

	const remaining = $derived.by(() => {
		const end = train?.endDate ? Date.parse(train.endDate) : NaN;
		return end ? formatSeconds(Math.max(0, (end - now) / 1000)) : '';
	});

	const hasMetadata = $derived(!!instance && (!!instance.queueSize || ageGated || instance.roleRestricted === true || !!instance.calendarEntryId || !!train || !!performance));

	async function refresh() {
		apply(await fetchLiveInstance(account, location, true));
	}

	function closeInstance() {
		confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_close_instance(); },
			run: async () => {
				try {
					const closed = await api.post<LiveInstance>(accountPath(account, '/instances/close'), { location });
					if (closed?.closedAt) closedInstances.set(location, closed.closedAt);
					toast.success(m.message_instance_closed());
					await refresh();
				} catch (err) {
					toastError(err);
				}
			}
		};
	}

	function renameInstance() {
		prompting = {
			get title() { return m.dialog_user_info_rename_instance(); },
			get description() { return m.prompt_rename_instance_description(); },
			value: instance?.displayName ?? '',
			errorMessage: '',
			allowEmpty: true,
			run: async (value) => {
				prompting = undefined;
				const displayName = value.trim() || null;
				if (displayName === (instance?.displayName ?? null)) return;
				try {
					await api.put(accountPath(account, '/instances/display-name'), { location, displayName });
					toast.success(m.message_instance_renamed());
					await refresh();
				} catch (err) {
					toastError(err);
				}
			}
		};
	}
</script>

{#snippet tip(text: string, icon: import('svelte').Snippet)}
	<Tooltip.Root>
		<Tooltip.Trigger>{#snippet child({ props })}<span {...props} class="flex items-center gap-0.5">{@render icon()}</span>{/snippet}</Tooltip.Trigger>
		<Tooltip.Content side="top">{text}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#if instance && (canAnnounce || canLinkCalendar || canRename || (!instance.closedAt && canClose) || showRefresh)}
	<span class="inline-flex items-center gap-2">
		{#if canAnnounce}
			<Button class="size-6 rounded-full text-xs text-muted-foreground hover:text-foreground" size="icon-sm" variant="outline" title={m.dialog_user_info_instance_announcement_tooltip()} aria-label={m.dialog_user_info_instance_announcement_tooltip()} onclick={() => (announceOpen = true)}><Megaphone class="size-4" /></Button>
		{/if}
		{#if canLinkCalendar}
			<Button class="size-6 rounded-full text-xs text-muted-foreground hover:text-foreground" size="icon-sm" variant="outline" title={m.dialog_user_info_instance_calendar_link_tooltip()} aria-label={m.dialog_user_info_instance_calendar_link_tooltip()} onclick={() => (calendarLinkOpen = true)}><CalendarPlus class="size-4" /></Button>
		{/if}
		{#if canRename}
			<Button class="size-6 rounded-full text-xs text-muted-foreground hover:text-foreground" size="icon-sm" variant="outline" title={m.dialog_user_info_rename_instance()} aria-label={m.dialog_user_info_rename_instance()} onclick={renameInstance}><Pencil class="size-4" /></Button>
		{/if}
		{#if !instance.closedAt && canClose}
			<Button class="size-6 rounded-full text-xs text-muted-foreground hover:text-red-500" size="icon-sm" variant="outline" title={m.dialog_user_info_close_instance()} aria-label={m.dialog_user_info_close_instance()} onclick={closeInstance}><Power class="size-4" /></Button>
		{/if}
		{#if showRefresh}
			<Button class="size-6 rounded-full text-xs text-muted-foreground hover:text-foreground" size="icon" variant="outline" title={m.dialog_user_info_refresh_instance_info()} aria-label={m.dialog_user_info_refresh_instance_info()} onclick={() => refresh().catch(toastError)}><RefreshCw class="size-4" /></Button>
		{/if}
	</span>
{/if}
{#if instance}
	<div class="flex items-center gap-1.5 rounded-full border border-muted-foreground/10 px-2 py-0.5 text-sm text-muted-foreground {className}">
		<HoverCard.Root openDelay={150} closeDelay={100}>
			<HoverCard.Trigger>
				{#snippet child({ props })}
					<span {...props} class={['flex items-center gap-0.5', instance.hasCapacityForYou === false && 'text-red-500']}>
						<UsersRound class="size-4" />
						{#if instance.userCount}<span>{instance.userCount}/{instance.capacity}</span>{/if}
					</span>
				{/snippet}
			</HoverCard.Trigger>
			<HoverCard.Content side="top" class="w-auto max-w-80 text-xs">
				<div class="flex flex-col flex-wrap gap-x-6 gap-y-2">
					{#if description}
						<div class="flex flex-col gap-1">
							<span>{m.dialog_user_info_instance_description()}</span>
							<span class="max-w-80 break-words whitespace-pre-wrap">{description}</span>
						</div>
					{/if}
					{#if instance.platforms?.standalonewindows || instance.platforms?.android || instance.platforms?.ios}
						<div class="flex flex-col gap-1">
							{#if instance.platforms.standalonewindows}<span><span class="text-platform-pc">PC: </span>{instance.platforms.standalonewindows}</span>{/if}
							{#if instance.platforms.android}<span><span class="text-platform-quest">Android: </span>{instance.platforms.android}</span>{/if}
							{#if instance.platforms.ios}<span><span class="text-platform-ios">iOS: </span>{instance.platforms.ios}</span>{/if}
						</div>
					{/if}
					{#if instance.categoryId}
						<span class="flex items-center gap-1">
							{m.dialog_user_info_instance_category()}
							{#if category?.icon}<i class={['translate-y-px leading-none', category.icon]}></i>{/if}
							{category?.name ?? instance.categoryId}
						</span>
					{/if}
					{#if instance.vibeIds?.length}<span>{m.dialog_user_info_instance_vibes()} {vibeTitles}</span>{/if}
					{#if languages.length}
						<div class="flex flex-col gap-1">
							<span>{m.dialog_user_info_instance_languages()}</span>
							{#each languages as lang (lang.code)}
								<span class="flex items-center gap-1.5">
									<span class={['flags inline-block', languageClass(lang.key)]}></span>
									{lang.name}
									{#if lang.percent !== null}<span class="text-muted-foreground">{lang.percent}%</span>{/if}
									{#if lang.isCreationLanguage}<span title={m.dialog_user_info_instance_creation_language()}><Star class="size-3 text-yellow-500" /></span>{/if}
								</span>
							{/each}
						</div>
					{/if}
					{#if instance.gameServerVersion}<span>Game Version: {instance.gameServerVersion}</span>{/if}
					{#if instance.queueEnabled}<span class="font-medium text-yellow-500">{m.dialog_user_info_instance_queuing_enabled()}</span>{/if}
					{#if disabled}<span>Disabled Content: {disabled}</span>{/if}
				</div>
			</HoverCard.Content>
		</HoverCard.Root>

		{#if friendCount}
			{#snippet friendsIcon()}<UserPlus2 class="size-4" />{friendCount}{/snippet}
			{@render tip('Friends in this instance', friendsIcon)}
		{/if}

		{#if lastJoinedAt}
			{#snippet lastJoinIcon()}<MapPin class="size-4" />{relative(lastJoinedAt)}{/snippet}
			{@render tip('Last Join', lastJoinIcon)}
		{/if}

		{#if hasMetadata}
			{#if instance.queueSize}
				{#snippet queueIcon()}<SquareStack class="size-4" />{instance?.queueSize}{/snippet}
				{@render tip('Queue', queueIcon)}
			{/if}
			{#if ageGated}
				{#snippet ageIcon()}<IdCard class="size-4 text-red-500" />{/snippet}
				{@render tip('Age Gated', ageIcon)}
			{/if}
			{#if instance.roleRestricted}
				{#snippet roleIcon()}<UserLock class="size-4 text-red-500" />{/snippet}
				{@render tip('Role Restricted', roleIcon)}
			{/if}
			{#if instance.calendarEntryId && ownerId.startsWith('grp_')}
				<!-- The original's calendar event popover: the linked event's details on hover. -->
				<HoverCard.Root openDelay={0} closeDelay={100} onOpenChange={(open) => open && loadCalendarEvent()}>
					<HoverCard.Trigger>{#snippet child({ props })}<span {...props} class="flex items-center gap-0.5"><CalendarDays class="size-4" /></span>{/snippet}</HoverCard.Trigger>
					<HoverCard.Content side="top" class="w-125 p-3">
						{#if calendarEvent && calendarEvent.id === instance.calendarEntryId}
							<CalendarEventDetails event={calendarEvent} relativeTime />
						{:else if calendarEventFailed}
							<span class="text-xs">{m.dialog_user_info_instance_calendar_event()}</span>
						{:else}
							<div class="flex justify-center"><Loader2 class="size-4 animate-spin" /></div>
						{/if}
					</HoverCard.Content>
				</HoverCard.Root>
			{:else if instance.calendarEntryId}
				{#snippet calendarIcon()}<CalendarDays class="size-4" />{/snippet}
				{@render tip('Calendar Event', calendarIcon)}
			{/if}
			{#if train}
				<Tooltip.Root>
					<Tooltip.Trigger>{#snippet child({ props })}<span {...props} class="flex items-center gap-0.5"><TrainFront class="size-4" /></span>{/snippet}</Tooltip.Trigger>
					<Tooltip.Content side="top">
						<div class="flex flex-col gap-1">
							<span class="font-medium">{m.dialog_user_info_instance_hype_train()}</span>
							<span>Gifts: {train.currentGiftCount}/{train.totalGiftGoal}</span>
							<span>Contributors: {train.totalGiftContributors}</span>
							{#if remaining}<span>Time Remaining: {remaining}</span>{/if}
							{#if train.startDate}<span>Started: {relative(train.startDate)}</span>{/if}
						</div>
					</Tooltip.Content>
				</Tooltip.Root>
			{/if}
			{#if performance}
				{#snippet perfIcon()}<img src="/images/performance_ranks/{performance.toLowerCase()}.png" class="size-4" alt={performance} />{/snippet}
				{@render tip(`Minimum Avatar Performance: ${performance}`, perfIcon)}
			{/if}
		{/if}
	</div>
{/if}

<ActionDialogs bind:confirming bind:prompting />
{#if canAnnounce}<InstanceAnnouncementDialog bind:open={announceOpen} {location} />{/if}
{#if canLinkCalendar}<InstanceCalendarLinkDialog bind:open={calendarLinkOpen} {location} currentEventId={instance?.calendarEntryId ?? ''} onchanged={refresh} />{/if}
