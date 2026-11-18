<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { pickPath } from '#lib/agent/agent.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';

	// The original's LaunchOptionsDialog. How VRChat is started is the PC's own setting, which the
	// server never sends (LaunchVrchat in vrcx_live.proto), so nothing here can type an argument or a
	// path into it: the arguments are set on the PC with the agent's setup command, and the path
	// override is chosen in a picker the agent shows on the PC.
	let { open = $bindable(false) }: { open?: boolean } = $props();

	let picking = $state(false);
	let chosen = $state('');

	let wasOpen = false;
	$effect(() => {
		if (open && !wasOpen) chosen = '';
		wasOpen = open;
	});

	async function choosePath() {
		picking = true;
		try {
			const path = await pickPath('vrchat_path');
			if (path) {
				chosen = path;
				toast.success('Updated launch options');
			}
		} catch (err) {
			toastError(err);
		} finally {
			picking = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_launch_options_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="text-xs">
			{m.dialog_launch_options_description()}<br />
			{m.dialog_launch_options_example()}
			<div>--fps=144</div>
			<div>--enable-debug-gui</div>
			<div>--enable-sdk-log-levels</div>
			<div>--enable-udon-debug-logging</div>
		</div>
		<div class="grid gap-1 text-sm">
			<span>The launch arguments are kept on your PC, not on the server. Set them there with the agent:</span>
			<code class="rounded bg-muted px-2 py-1 text-xs select-all">vrcxd-agent setup --launch-arguments "--fps=144 --enable-sdk-log-levels"</code>
			<span class="text-xs text-muted-foreground">A running agent picks the change up within 30 seconds.</span>
		</div>
		<div class="grid gap-2">
			<div class="text-sm">{m.dialog_launch_options_path_override()}</div>
			<div class="flex items-center gap-2">
				<Button variant="outline" size="sm" disabled={picking || !agentCan('pick_path')} onclick={choosePath}>Choose launch.exe on the PC</Button>
				<span class="min-w-0 truncate text-xs text-muted-foreground" title={chosen}>{chosen}</span>
			</div>
			{#if !agentCan('pick_path')}
				<span class="text-xs text-muted-foreground">Choosing the path needs the VRCXD agent running on your PC. Or set it there: <code>vrcxd-agent setup --vrchat-path &lt;folder or launch.exe&gt;</code></span>
			{/if}
		</div>
		<Dialog.Footer>
			<Button variant="outline" href="https://docs.vrchat.com/docs/launch-options" target="_blank" rel="noopener noreferrer">{m.dialog_launch_options_vrchat_docs()}</Button>
			<Button variant="outline" href="https://docs.unity3d.com/Manual/CommandLineArguments.html" target="_blank" rel="noopener noreferrer">{m.dialog_launch_options_unity_manual()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
