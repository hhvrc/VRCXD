<script lang="ts" module>
	export type Confirming = {
		/** Asks "Are you sure you want to <command>?". */
		command?: string;
		/** Or says this instead. */
		description?: string;
		title?: string;
		confirmText?: string;
		destructive?: boolean;
		/** A cancel button that does something, as the original's "Copy Avatar IDs". */
		cancelText?: string;
		oncancel?: () => void;
		run: () => Promise<unknown> | void;
	};
	export type Prompting = {
		title: string;
		description: string;
		value: string;
		multiline?: boolean;
		numeric?: boolean;
		/** An empty answer is allowed (the original's Rename Instance clears the name with one). */
		allowEmpty?: boolean;
		errorMessage: string;
		run: (value: string) => Promise<unknown>;
	};
</script>

<script lang="ts">
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';

	// The original's confirm and prompt modals, shared by the world and avatar pop-ups.
	let { confirming = $bindable(), prompting = $bindable(), busy = false }: { confirming?: Confirming; prompting?: Prompting; busy?: boolean } = $props();

	let invalid = $state(false);

	function submit() {
		if (!prompting) return;
		const value = prompting.value.trim();
		if ((!value && !prompting.allowEmpty) || (prompting.numeric && value && !/^\d+$/.test(value))) {
			invalid = true;
			return;
		}
		invalid = false;
		prompting.run(value);
	}
</script>

<Dialog.Root bind:open={() => !!confirming, (open) => { if (!open) confirming = undefined; }}>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header><Dialog.Title>{confirming?.title ?? 'Confirm'}</Dialog.Title></Dialog.Header>
		<p class="text-sm whitespace-pre-line">{confirming?.description ?? `Are you sure you want to ${confirming?.command}?`}</p>
		<Dialog.Footer>
			<Button
				variant="secondary"
				class="mr-2"
				onclick={() => {
					const cancel = confirming?.oncancel;
					confirming = undefined;
					cancel?.();
				}}>{confirming?.cancelText ?? 'Cancel'}</Button
			>
			<Button disabled={busy} variant={confirming?.destructive ? 'destructive' : 'default'} onclick={() => confirming?.run()}>{confirming?.confirmText ?? 'Confirm'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => !!prompting, (open) => { if (!open) { prompting = undefined; invalid = false; } }}>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header><Dialog.Title>{prompting?.title}</Dialog.Title></Dialog.Header>
		{#if prompting}
			<p class="text-sm text-muted-foreground">{prompting.description}</p>
			{#if prompting.multiline}
				<textarea
					rows="6"
					bind:value={prompting.value}
					class="w-full resize-none rounded-md border border-input bg-transparent p-2 text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring"
				></textarea>
			{:else}
				<Input bind:value={prompting.value} />
			{/if}
			{#if invalid}<p class="text-xs text-destructive">{prompting.errorMessage}</p>{/if}
		{/if}
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (prompting = undefined)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button disabled={busy} onclick={submit}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
