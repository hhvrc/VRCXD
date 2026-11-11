<script lang="ts">
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as RadioGroup from '#lib/components/ui/radio-group/index.js';
	import { m } from '#lib/paraglide/messages.js';
	import { notySettings } from '#lib/notifications/noty.svelte.js';

	// The original's NotificationPositionDialog: a headset view with a radio button at each place
	// the SteamVR overlay's notifications can stack.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	const POSITIONS = [
		{ value: 'topLeft', class: 'top-[20%] left-[10%]' },
		{ value: 'top', class: 'top-[20%] left-1/2' },
		{ value: 'topRight', class: 'top-[20%] left-[90%]' },
		{ value: 'centerLeft', class: 'top-1/2 left-[10%]' },
		{ value: 'center', class: 'top-1/2 left-1/2' },
		{ value: 'centerRight', class: 'top-1/2 left-[90%]' },
		{ value: 'bottomLeft', class: 'top-[80%] left-[10%]' },
		{ value: 'bottom', class: 'top-[80%] left-1/2' },
		{ value: 'bottomRight', class: 'top-[80%] left-[90%]' }
	];
</script>

<Dialog.Root bind:open>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_notification_position_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="text-xs">{m.dialog_notification_position_description()}</div>
		<div class="relative mx-auto mt-4 size-75">
			<svg version="1.1" xmlns="http://www.w3.org/2000/svg" viewBox="80 80 80 100" class="absolute inset-0 mt-6 size-full">
				<path
					style="fill: black"
					d="M291.89,5A3.11,3.11,0,0,1,295,8.11V160.64a3.11,3.11,0,0,1-3.11,3.11H8.11A3.11,3.11,0,0,1,5,160.64V8.11A3.11,3.11,0,0,1,8.11,5H291.89m0-5H8.11A8.11,8.11,0,0,0,0,8.11V160.64a8.11,8.11,0,0,0,8.11,8.11H291.89a8.11,8.11,0,0,0,8.11-8.11V8.11A8.11,8.11,0,0,0,291.89,0Z"
				/>
				<rect style="fill: #c4c4c4" x="5" y="5" width="290" height="158.75" rx="2.5" />
			</svg>
			<RadioGroup.Root value={notySettings.notificationPosition} class="absolute inset-0" onValueChange={(v) => v && notySettings.setNotificationPosition(v)}>
				{#each POSITIONS as position (position.value)}
					<RadioGroup.Item id={`notificationPosition-${position.value}`} aria-label={position.value} value={position.value} class={['absolute -translate-x-1/2 -translate-y-1/2', position.class]} />
				{/each}
			</RadioGroup.Root>
		</div>
		<Dialog.Footer>
			<Button onclick={() => (open = false)}>{m.dialog_notification_position_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
