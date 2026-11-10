<script lang="ts">
	import { FORM_FIELD } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import Copy from '@lucide/svelte/icons/copy';
	import Ellipsis from '@lucide/svelte/icons/ellipsis';
	import Info from '@lucide/svelte/icons/info';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { canInvite, canOpenInGame, checkClosed, desktop, gameLaunchLink, getShortNames, instanceDialogs, launchGame, launchUrl, loadLaunchAsDesktop, openInGame, refreshDesktop, saveLaunchAsDesktop, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's LaunchDialog. The desktop app starts the game on its PC; with no app connected,
	// the page falls back to a vrchat:// link, which only works on the PC the game is on.
	const LABEL = 'flex w-fit items-center gap-1 text-sm leading-snug font-medium';

	let url = $state('');
	let shortUrl = $state('');
	let location = $state('');
	let shortName = $state('');
	let secureOrShortName = $state('');
	let currentLocation = $state<string | null>(null);
	let launchAsDesktop = $state(loadLaunchAsDesktop());
	let secondCopy = $state<boolean | undefined>();
	let busy = $state(false);
	let notice = $state('');
	let error = $state('');

	const target = $derived(instanceDialogs.launch);
	const canSendInvite = $derived(canInvite(location, vrchatAuth.ownerUserId, currentLocation));

	$effect(() => {
		const opened = target;
		notice = '';
		error = '';
		if (!opened) return;
		refreshDesktop();
		const L = parseLocation(opened.location);
		const given = opened.shortName ?? '';
		shortName = given;
		secureOrShortName = given;
		shortUrl = given ? `https://vrch.at/${given}` : '';
		location = L.instanceId ? `${L.worldId}:${L.instanceId}` : L.worldId;
		url = launchUrl({ worldId: L.worldId, instanceId: L.instanceId, shortName: given });
		checkClosed(location);
		api.get<{ currentLocation?: string | null }>(ownPath('/instances/options')).then(
			(options) => (currentLocation = options.currentLocation ?? null),
			() => (currentLocation = null)
		);
		if (given) return;
		getShortNames(opened.location).then(
			(names) => {
				if (instanceDialogs.launch !== opened) return;
				const found = names.shortName ?? '';
				shortName = found;
				secureOrShortName = found || names.secureName || '';
				shortUrl = found ? `https://vrch.at/${found}` : '';
				url = launchUrl({ worldId: L.worldId, instanceId: L.instanceId, shortName: found });
			},
			(err) => {
				if (instanceDialogs.launch === opened) error = ApiError.from(err).message;
			}
		);
	});

	async function copy(text: string) {
		try {
			await navigator.clipboard.writeText(text);
			notice = 'Instance copied to clipboard';
		} catch {
			error = 'Instance copied failed';
		}
	}

	async function sendSelfInvite() {
		error = '';
		try {
			await selfInvite(location, shortName);
			notice = 'Self invite sent';
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	async function launch(desktopMode: boolean, allowSecondInstance = false) {
		error = '';
		secondCopy = undefined;
		if (!desktop.connected) {
			window.location.href = await gameLaunchLink(location, shortName);
			instanceDialogs.launch = undefined;
			return;
		}
		busy = true;
		try {
			await launchGame(location, shortName, { desktopMode, allowSecondInstance });
			instanceDialogs.launch = undefined;
		} catch (err) {
			const problem = ApiError.from(err);
			if (problem.type === 'Desktop.GameRunning') secondCopy = desktopMode;
			else error = problem.message;
		} finally {
			busy = false;
		}
	}

	function launchOtherMode() {
		launchAsDesktop = !launchAsDesktop;
		saveLaunchAsDesktop(launchAsDesktop);
		launch(launchAsDesktop);
	}

	async function openHere() {
		error = '';
		busy = true;
		try {
			const result = await openInGame(location, shortName);
			if (result === 'opened') instanceDialogs.launch = undefined;
			else if (result === 'invited') notice = 'Could not open it in VRChat, self invite sent';
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			busy = false;
		}
	}

	function showInvite() {
		instanceDialogs.invite = { location };
	}
</script>

{#snippet copyRow(label: string, value: string, tooltip?: string)}
	<div class={FORM_FIELD}>
		<span class={LABEL}>{label}{#if tooltip}<span title={tooltip}><Info class="size-4 text-muted-foreground" aria-label={tooltip} /></span>{/if}</span>
		<div class="flex items-center gap-2">
			<Input {value} readonly class="h-8" onclick={(event) => event.currentTarget.select()} />
			<Button class="rounded-full" size="icon-sm" variant="ghost" title={m.dialog_launch_copy_tooltip()} aria-label={m.dialog_launch_copy_tooltip()} onclick={() => copy(value)}><Copy /></Button>
		</div>
	</div>
{/snippet}

<Dialog.Root bind:open={() => !!instanceDialogs.launch, (open) => { if (!open) instanceDialogs.launch = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-xl" onOpenAutoFocus={(event) => event.preventDefault()}>
		<Dialog.Header><Dialog.Title>{m.dialog_launch_header()}</Dialog.Title></Dialog.Header>
		<div class="flex flex-col gap-4">
			{@render copyRow('URL', url)}
			{#if shortUrl}{@render copyRow('Short URL', shortUrl, "Short URL's expire after a set period of time")}{/if}
			{@render copyRow('Location', location)}
		</div>
		{#if error}<p class="text-xs text-destructive">{error}</p>{:else if notice}<p class="text-xs text-muted-foreground">{notice}</p>{/if}
		<Dialog.Footer>
			<Button class="mr-1.5" variant="outline" disabled={!canSendInvite} onclick={showInvite}>{m.dialog_launch_invite()}</Button>
			{#if canOpenInGame()}
				<Button variant="outline" disabled={!secureOrShortName || busy} onclick={openHere}>{m.dialog_launch_open_ingame()}</Button>
			{:else}
				<Button class="mr-1.25" variant="outline" disabled={!secureOrShortName} onclick={sendSelfInvite}>{m.dialog_launch_self_invite()}</Button>
			{/if}
			{#if desktop.connected}
				<div class="flex w-fit items-stretch [&>*:first-child]:rounded-r-none [&>*:last-child]:rounded-l-none [&>*:last-child]:border-l-0">
					<Button disabled={!secureOrShortName || busy} onclick={() => launch(launchAsDesktop)}>{launchAsDesktop ? 'Launch (Desktop)' : 'Launch (VR)'}</Button>
					<DropdownMenu.Root>
						<DropdownMenu.Trigger disabled={!secureOrShortName || busy}>
							{#snippet child({ props })}
								<Button {...props} size="icon" aria-label={m.dialog_launch_header()}><Ellipsis class="size-4" /></Button>
							{/snippet}
						</DropdownMenu.Trigger>
						<DropdownMenu.Content align="end">
							<DropdownMenu.Item onSelect={launchOtherMode}>{launchAsDesktop ? 'Launch (VR)' : 'Launch (Desktop)'}</DropdownMenu.Item>
						</DropdownMenu.Content>
					</DropdownMenu.Root>
				</div>
			{:else}
				<Button disabled={!secureOrShortName} title="The desktop app is not connected, so this opens a vrchat:// link on this computer" onclick={() => launch(false)}>{m.dialog_launch_launch()}</Button>
			{/if}
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => secondCopy !== undefined, (open) => { if (!open) secondCopy = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_launch_header()}</Dialog.Title>
			<Dialog.Description>{m.dialog_launch_game_running_warning()}</Dialog.Description>
		</Dialog.Header>
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (secondCopy = undefined)}>{m.dialog_launch_confirm_no()}</Button>
			<Button onclick={() => launch(secondCopy ?? false, true)}>{m.dialog_launch_confirm_yes()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
