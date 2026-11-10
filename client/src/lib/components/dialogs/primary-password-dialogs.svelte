<script lang="ts">
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { answerPrimaryPassword, primaryPrompt, primarySetDialog, setPrimaryPassword } from '#lib/api/primary-password.svelte.js';
	import { isPrimaryPassword, PRIMARY_PASSWORD_MAX_LENGTH } from '#lib/api/saved-logins.js';

	// The original's primary password prompt and its PrimaryPasswordDialog (a new one, typed twice).

	function submitPrompt(event: SubmitEvent) {
		event.preventDefault();
		if (isPrimaryPassword(primaryPrompt.value)) answerPrimaryPassword(primaryPrompt.value);
	}
</script>

<Dialog.Root open={primaryPrompt.open} onOpenChange={(open) => { if (!open) answerPrimaryPassword(null); }}>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header>
			<Dialog.Title>{m.prompt_primary_password_header()}</Dialog.Title>
			<Dialog.Description>{m.prompt_primary_password_description()}</Dialog.Description>
		</Dialog.Header>
		<form id="primary-password-prompt" onsubmit={submitPrompt}>
			<Input type="password" autocomplete="off" maxlength={PRIMARY_PASSWORD_MAX_LENGTH} bind:value={primaryPrompt.value} />
		</form>
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => answerPrimaryPassword(null)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button type="submit" form="primary-password-prompt" disabled={!isPrimaryPassword(primaryPrompt.value)}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={primarySetDialog.open}>
	<Dialog.Content class={SMALL_DIALOG} interactOutsideBehavior="ignore">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_primary_password_header()}</Dialog.Title>
		</Dialog.Header>
		<Input type="password" autocomplete="new-password" maxlength={PRIMARY_PASSWORD_MAX_LENGTH} placeholder={m.dialog_primary_password_password_placeholder()} bind:value={primarySetDialog.password} />
		<Input class="mt-1.5" type="password" autocomplete="new-password" maxlength={PRIMARY_PASSWORD_MAX_LENGTH} placeholder={m.dialog_primary_password_re_input_placeholder()} bind:value={primarySetDialog.rePassword} />
		<Dialog.Footer>
			<Button disabled={primarySetDialog.password.length === 0 || primarySetDialog.password !== primarySetDialog.rePassword} onclick={setPrimaryPassword}>{m.dialog_primary_password_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
