<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import LayoutDashboard from '@lucide/svelte/icons/layout-dashboard';
	import MousePointerClick from '@lucide/svelte/icons/mouse-pointer-click';
	import Search from '@lucide/svelte/icons/search';
	import UserSearch from '@lucide/svelte/icons/user-search';
	import vrcxLogo from '#lib/assets/vrcx-logo.png';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

	// The original's onboarding SpotlightDialog: the first time VRCX opens in this browser, a welcome
	// with four features people tend to miss. Escape, a click outside or Get Started dismisses it for good.
	const SEEN_KEY = 'VRCX_onboarding_welcome_seen';

	const features = [
		{ key: 'user_profiles', icon: UserSearch, hue: 280, get title() { return m.onboarding_welcome_features_user_profiles_title(); }, get description() { return m.onboarding_welcome_features_user_profiles_description(); } },
		{ key: 'right_click', icon: MousePointerClick, hue: 200, get title() { return m.onboarding_welcome_features_right_click_title(); }, get description() { return m.onboarding_welcome_features_right_click_description(); } },
		{ key: 'dashboard', icon: LayoutDashboard, hue: 45, get title() { return m.onboarding_welcome_features_dashboard_title(); }, get description() { return m.onboarding_welcome_features_dashboard_description(); } },
		{ key: 'quick_search', icon: Search, hue: 142, get title() { return m.onboarding_welcome_features_quick_search_title(); }, get description() { return m.onboarding_welcome_features_quick_search_description(); } }
	];

	let open = $state(false);

	$effect(() => {
		if (readSetting(SEEN_KEY, false)) return;
		const timer = setTimeout(() => (open = true), 800);
		return () => clearTimeout(timer);
	});

	function dismiss() {
		open = false;
		storeSetting(SEEN_KEY, true);
	}
</script>

<Dialog.Root bind:open={() => open, (v) => !v && dismiss()}>
	<Dialog.Content class="border border-border bg-background/85 shadow-lg backdrop-blur-xl backdrop-saturate-[1.4] sm:max-w-2xl" showCloseButton={false}>
		<div class="pt-2 text-center">
			<div class="mb-2 flex justify-center">
				<img src={vrcxLogo} alt="VRCXD" class="size-12 rounded-xl" />
			</div>
			<h2 class="m-0 text-[22px] font-bold tracking-tight">{m.onboarding_welcome_title()}</h2>
			<p class="mt-1.5 text-sm text-muted-foreground">{m.onboarding_welcome_subtitle()}</p>
		</div>
		<div class="my-4 grid auto-rows-fr grid-cols-4 gap-2.5">
			{#each features as feature, index (feature.key)}
				<div
					class="feature flex h-full cursor-default flex-col items-center rounded-[10px] border border-transparent bg-muted/50 px-2 py-3.5 pb-3 text-center transition-all duration-250 hover:-translate-y-0.5 hover:border-primary/25 hover:bg-muted/80"
					style:animation-delay="{0.1 + index * 0.1}s"
				>
					<div class="mb-2.5 flex size-10 items-center justify-center rounded-[10px]" style:background="hsl({feature.hue} 60% 50% / 0.12)" style:color="hsl({feature.hue} 60% 55%)">
						<feature.icon class="size-5" />
					</div>
					<div class="mb-1 w-full text-compact leading-snug font-semibold">{feature.title}</div>
					<div class="w-full text-[11.5px] leading-snug text-muted-foreground">{feature.description}</div>
				</div>
			{/each}
		</div>
		<div class="flex flex-col items-center">
			<Button class="w-full text-sm font-semibold" size="lg" onclick={dismiss}>{m.onboarding_welcome_cta()}</Button>
		</div>
	</Dialog.Content>
</Dialog.Root>

<style>
	.feature {
		animation: feature-appear 0.4s ease-out both;
	}
	@keyframes feature-appear {
		from {
			opacity: 0;
			transform: translateY(12px) scale(0.95);
		}
		to {
			opacity: 1;
			transform: translateY(0) scale(1);
		}
	}
</style>
