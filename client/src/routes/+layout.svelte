<script lang="ts">
	import '../app.css';
	import favicon from '#lib/assets/favicon.svg';
	import AppSidebar from '#lib/components/layout/app-sidebar.svelte';
	import ImageViewerHost from '#lib/components/dialogs/image-viewer-host.svelte';
	import InstanceQueueToasts from '#lib/components/instance/instance-queue-toasts.svelte';
	import EditProfileDialog from '#lib/components/dialogs/edit-profile-dialog.svelte';
	import GroupEditDialog from '#lib/components/group/group-edit-dialog.svelte';
	import GroupEventEditDialog from '#lib/components/group/group-event-edit-dialog.svelte';
	import InviteDialog from '#lib/components/invite/invite-dialog.svelte';
	import LaunchDialog from '#lib/components/instance/launch-dialog.svelte';
	import NewInstanceDialog from '#lib/components/instance/new-instance-dialog.svelte';
	import ServerTokenGate from '#lib/components/layout/server-token-gate.svelte';
	import StatusBar from '#lib/components/layout/status-bar.svelte';
	import UserDialogHost from '#lib/components/details/user-dialog-host.svelte';
	import VrchatLoginGate from '#lib/components/layout/vrchat-login-gate.svelte';
	import PrimaryPasswordDialogs from '#lib/components/dialogs/primary-password-dialogs.svelte';
	import QuickSearchDialog from '#lib/components/dialogs/quick-search-dialog.svelte';
	import DirectAccessDialog from '#lib/components/dialogs/direct-access-dialog.svelte';
	import StoreCheckDialogs from '#lib/components/dialogs/store-check-dialogs.svelte';
	import ExternalLinkDialog from '#lib/components/dialogs/external-link-dialog.svelte';
	import YouTubeBotErrorPrompt from '#lib/components/dialogs/youtube-bot-error-prompt.svelte';
	import ErrorReportingPrompt from '#lib/components/dialogs/error-reporting-prompt.svelte';
	import SpotlightDialog from '#lib/components/dialogs/spotlight-dialog.svelte';
	import RegistryRestorePrompt from '#lib/components/tools/registry-restore-prompt.svelte';
	import AgentAlertPrompt from '#lib/components/dialogs/agent-alert-prompt.svelte';
	import RightSidebar from '#lib/components/sidebar/right-sidebar.svelte';
	import * as Resizable from '#lib/components/ui/resizable/index.js';
	import * as Sidebar from '#lib/components/ui/sidebar/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { Toaster } from '#lib/components/ui/sonner/index.js';
	import { desktop, loadInstanceCategoriesAndVibes, refreshDesktop } from '#lib/instances/instance-launch.svelte.js';
	import { connectLive, live, onLiveEvent } from '#lib/api/live.svelte.js';
	import { startNoty } from '#lib/notifications/noty.svelte.js';
	import { page } from '$app/state';
	import { getServerToken } from '#lib/api/client.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { notificationCenter } from '#lib/notifications/notification-center.svelte.js';

	let { children } = $props();

	// The original hides the friends sidebar on pages that already list friends or need the width.
	const HIDE_ASIDE = ['/friend-list', '/charts-instance', '/charts-mutual', '/charts-hot-worlds'];
	const showAside = $derived(!!vrchatAuth.ownerUserId && !HIDE_ASIDE.some((p) => page.url.pathname.startsWith(p)));

	// Whether the game can be reached is shown across the app, so it stays current for the whole session.
	$effect(() => {
		if (!vrchatAuth.ownerUserId || !getServerToken()) return;
		const disconnect = connectLive();
		startNoty();
		// The original's login flow loads the instance categories and vibes the instance tooltip names.
		void loadInstanceCategoriesAndVibes();
		const unsubscribe = onLiveEvent((event) => {
			if (event.event.case === 'desktop') refreshDesktop();
		});
		return () => {
			unsubscribe();
			disconnect();
		};
	});

	// The original's tray icon notification dot (Tray Notification Dot), on the browser tab's icon:
	// a red dot while there are unseen notifications.
	let faviconHref = $state(favicon);
	$effect(() => {
		if (!appearance.notificationIconDot.value || !notificationCenter.hasUnseen) {
			faviconHref = favicon;
			return;
		}
		let cancelled = false;
		const image = new Image();
		image.onload = () => {
			if (cancelled) return;
			const canvas = document.createElement('canvas');
			canvas.width = canvas.height = 64;
			const context = canvas.getContext('2d');
			if (!context) return;
			const scale = Math.min(64 / image.width, 64 / image.height);
			context.drawImage(image, (64 - image.width * scale) / 2, (64 - image.height * scale) / 2, image.width * scale, image.height * scale);
			context.fillStyle = '#ef4444';
			context.beginPath();
			context.arc(50, 14, 13, 0, Math.PI * 2);
			context.fill();
			faviconHref = canvas.toDataURL('image/png');
		};
		image.src = favicon;
		return () => (cancelled = true);
	});

	// The original's MainLayout: the nav panel keeps its collapsed state (VRCX_navIsCollapsed) and
	// its width (VRCX_navPanelWidth, 64-480px), dragged from its right edge.
	const sidebarOpen = $derived(!appearance.isNavCollapsed.value);

	// The right sidebar keeps its width in pixels (VRCX_rightSidebarWidth), not as a share of the
	// window, so resizing the window leaves it alone (upstream issue 1636). Dragging the handle saves it.
	const ASIDE_KEY = 'VRCX_rightSidebarWidth';
	let asideGroup = $state<HTMLElement | null>(null);
	let asidePane = $state<{ resize: (size: number) => void; isCollapsed: () => boolean } | undefined>();
	let asideDragging = false;
	const readAsideWidth = () => {
		const n = Number(readSetting<number>(ASIDE_KEY, 0));
		return Number.isFinite(n) && n > 0 ? n : 0;
	};

	function onAsideLayout(layout: number[]) {
		if (!asideDragging || !asideGroup || layout.length < 2 || !layout[1]) return;
		storeSetting(ASIDE_KEY, Math.round((layout[1] / 100) * asideGroup.clientWidth));
	}

	$effect(() => {
		const group = asideGroup;
		if (!group || !showAside) return;
		const fit = () => {
			const px = readAsideWidth();
			if (!px || !asidePane || asidePane.isCollapsed() || !group.clientWidth) return;
			asidePane.resize(Math.min(100, (px / group.clientWidth) * 100));
		};
		const observer = new ResizeObserver(fit);
		observer.observe(group);
		return () => observer.disconnect();
	});

	function startNavResize(event: PointerEvent) {
		if (!sidebarOpen) return;
		event.preventDefault();
		const zoom = appearance.zoomLevel.value / 100;
		const prevUserSelect = document.body.style.userSelect;
		const prevCursor = document.body.style.cursor;
		document.body.style.userSelect = 'none';
		document.body.style.cursor = 'col-resize';
		const move = (e: PointerEvent) => (appearance.navWidth.value = Math.round(e.clientX / zoom));
		const up = () => {
			document.body.style.userSelect = prevUserSelect;
			document.body.style.cursor = prevCursor;
			window.removeEventListener('pointermove', move);
			window.removeEventListener('pointerup', up);
		};
		window.addEventListener('pointermove', move);
		window.addEventListener('pointerup', up);
		move(event);
	}

	$effect(() => {
		// Events missed while the socket was down are not replayed.
		if (live.connected && vrchatAuth.ownerUserId) refreshDesktop();
		else if (!live.connected) desktop.connected = false;
	});
</script>

<svelte:head>
	<link rel="icon" href={faviconHref} />
</svelte:head>

<!-- The page zoom (Settings > Interface > Zoom) scales viewport units too, so the height undoes it. -->
<div class="flex min-h-0 min-w-0 flex-col overflow-hidden" style="height: calc(100dvh / var(--app-zoom, 1))">
	<Sidebar.Provider
		class="relative min-h-0 flex-1"
		style="--sidebar-width: {appearance.navWidth.value}px; --sidebar-width-icon: 48px"
		open={sidebarOpen}
		onOpenChange={(open) => (appearance.isNavCollapsed.value = !open)}
	>
		<AppSidebar />
		{#if sidebarOpen}
			<div
				class="absolute top-0 bottom-0 z-30 hidden w-1 cursor-ew-resize select-none md:block"
				style="left: var(--sidebar-width)"
				role="separator"
				aria-orientation="vertical"
				aria-label="Resize navigation"
				onpointerdown={startNavResize}
			></div>
		{/if}
		<Sidebar.Inset class="min-w-0">
			<header class="flex h-10 shrink-0 items-center gap-2 border-b border-border px-2">
				<Sidebar.Trigger />
			</header>
			<Resizable.PaneGroup bind:ref={asideGroup} direction="horizontal" autoSaveId="vrcx-main-layout-right-sidebar" class="min-h-0 flex-1" onLayoutChange={onAsideLayout}>
				<Resizable.Pane defaultSize={75} order={1}>
					<main class="h-full min-h-0 overflow-auto p-3">
						{@render children()}
					</main>
				</Resizable.Pane>
				{#if showAside}
					<Resizable.Handle class="z-20 opacity-0" onDraggingChange={(dragging) => (asideDragging = dragging)} />
					<Resizable.Pane bind:this={asidePane} defaultSize={25} minSize={12} collapsedSize={0} collapsible order={2} style="max-width: 700px">
						<RightSidebar />
					</Resizable.Pane>
				{/if}
			</Resizable.PaneGroup>
		</Sidebar.Inset>
	</Sidebar.Provider>
	<StatusBar />
</div>
<ServerTokenGate />
<VrchatLoginGate />
<PrimaryPasswordDialogs />
<ImageViewerHost />
<InstanceQueueToasts />
<Toaster position="top-center" />
<!-- Pop-ups use tooltips (a user's location, the moderation tool), so they need the provider too. -->
<Tooltip.Provider>
	<UserDialogHost />
	<NewInstanceDialog />
	<LaunchDialog />
	<InviteDialog />
	<QuickSearchDialog />
	<DirectAccessDialog />
	<StoreCheckDialogs />
	<ExternalLinkDialog />
	<YouTubeBotErrorPrompt />
	<ErrorReportingPrompt />
	<SpotlightDialog />
	<EditProfileDialog />
	<GroupEditDialog />
	<GroupEventEditDialog />
	<RegistryRestorePrompt />
	<AgentAlertPrompt />
</Tooltip.Provider>
