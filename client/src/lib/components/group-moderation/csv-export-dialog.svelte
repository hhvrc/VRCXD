<script lang="ts" generics="T">
	import { untrack } from 'svelte';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { csvField } from '#lib/groups/group-moderation.svelte.js';
	import { copyText } from '#lib/helpers/feedback.js';

	// The original's GroupMemberModerationExportDialog and GroupMemberModerationBanExportDialog: a
	// checkbox per column, all on, the CSV in a read-only box that copies itself when clicked.
	let {
		open = $bindable(false),
		title,
		rows,
		options,
		value
	}: {
		open?: boolean;
		title: string;
		rows: T[];
		options: { label: string; text: string }[];
		value: (row: T, key: string) => string;
	} = $props();

	// Unticked columns stay unticked for the next export, as the original's do.
	let checked = $state<string[]>(untrack(() => options.map((o) => o.label)));

	function toggle(label: string, on: boolean) {
		checked = on ? (checked.includes(label) ? checked : [...checked, label]) : checked.filter((l) => l !== label);
	}

	const content = $derived.by(() => {
		const keys = options.filter((o) => checked.includes(o.label)).map((o) => o.label);
		const header = `${keys.join(',')}\n`;
		return header + rows.map((row) => keys.map((key) => csvField(value(row, key))).join(',')).join('\n');
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="bg-background sm:max-w-162.5">
		<Dialog.Header><Dialog.Title>{title}</Dialog.Title></Dialog.Header>
		<div class="mb-2 flex flex-col gap-2">
			{#each options as option (option.label)}
				<label class="inline-flex items-center gap-2 text-sm">
					<Checkbox checked={checked.includes(option.label)} onCheckedChange={(on) => toggle(option.label, on)} />
					<span>{option.text}</span>
				</label>
			{/each}
		</div>
		<Textarea
			value={content}
			rows={15}
			readonly
			class="mt-4 resize-none"
			onclick={() => copyText(content)}
		/>
	</Dialog.Content>
</Dialog.Root>
