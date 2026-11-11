<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import FolderOpen from '@lucide/svelte/icons/folder-open';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { cacheInfo, clearCache, sweepCache } from '#lib/agent/cache.js';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as InputGroup from '#lib/components/ui/input-group/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { externalLink } from '#lib/nav/external-link.svelte.js';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import { pickPath, readVrchatConfig, writeVrchatConfig } from '#lib/agent/agent.js';
	import { configToSave, DEFAULT_RESOLUTION_KEY, parseConfig, RESOLUTION_FIELDS, resolutionKey, resolutionLabel, type VrchatConfig } from '#lib/agent/vrchat-config.js';

	// The original's VRChatConfigDialog: VRChat's config.json on the PC, read and written by its agent,
	// with the cache's size, Delete Cache and Sweep Cache at the top.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	let config = $state<VrchatConfig>({});
	let loading = $state(false);
	let usedCache = $state('');
	let cacheLoading = $state(false);
	let confirming = $state<Confirming | undefined>();
	// The original's totalCacheSize: the configured cache_size, else VRChat's 30 GB.
	const totalCache = $derived(config.cache_size || 30);

	// getVRChatCacheSize, in GB to two places.
	async function refreshCacheSize() {
		if (!agentCan('cache')) return;
		cacheLoading = true;
		try {
			usedCache = ((await cacheInfo()).size / 1073741824).toFixed(2);
		} catch (err) {
			toastError(err);
		} finally {
			cacheLoading = false;
		}
	}

	function askClearCache() {
		confirming = {
			title: m.confirm_title(),
			description: m.confirm_clear_cache(),
			destructive: true,
			run: async () => {
				confirming = undefined;
				try {
					await clearCache();
					toast.success(m.message_cache_deleted());
				} catch (err) {
					toast.error(m.message_cache_delete_error({ error: err instanceof Error ? err.message : String(err) }));
				}
				refreshCacheSize();
			}
		};
	}

	// runSweepVRChatCacheFlow, then the size again.
	async function sweep() {
		try {
			await sweepCache();
		} catch (err) {
			toastError(err);
		}
		refreshCacheSize();
	}

	const fields = $derived([
		{ key: 'cache_size', name: m.dialog_config_json_max_cache_size(), placeholder: '30', type: 'number', min: 30 },
		{ key: 'cache_expiry_delay', name: m.dialog_config_json_cache_expiry_delay(), placeholder: '30', type: 'number', min: 30 },
		{ key: 'cache_directory', name: m.dialog_config_json_cache_directory(), placeholder: '%AppData%\\..\\LocalLow\\VRChat\\VRChat', folder: true },
		{ key: 'picture_output_folder', name: m.dialog_config_json_picture_directory(), placeholder: '%UserProfile%\\Pictures\\VRChat', folder: true },
		{ key: 'fpv_steadycam_fov', name: m.dialog_config_json_fpv_steadycam_fov(), placeholder: '50', type: 'number', min: 30, max: 110 }
	]);

	const resolutionLabels = $derived({
		camera: m.dialog_config_json_camera_resolution(),
		spout: m.dialog_config_json_spout_resolution(),
		screenshot: m.dialog_config_json_screenshot_resolution()
	});

	$effect(() => {
		if (!open) return;
		untrack(() => {
			load();
			refreshCacheSize();
		});
	});

	async function load() {
		loading = true;
		config = {};
		try {
			config = parseConfig(await readVrchatConfig());
		} catch (err) {
			if (err instanceof SyntaxError || (err instanceof Error && err.message.startsWith('Invalid JSON'))) toast.error(m.message_cache_invalid_config());
			else toastError(err);
			open = false;
		} finally {
			loading = false;
		}
	}

	function text(key: string): string {
		const v = config[key];
		return v === undefined || typeof v === 'boolean' ? '' : String(v);
	}

	// openConfigFolderBrowser: the PC's folder picker, starting at the folder set now.
	async function browse(key: string) {
		try {
			const path = await pickPath('folder', text(key));
			if (path) config[key] = path;
		} catch (err) {
			toastError(err);
		}
	}

	function setResolution(field: (typeof RESOLUTION_FIELDS)[number], value: string) {
		const row = field.rows.find((r) => resolutionKey(r.width, r.height) === value);
		if (!row) return;
		config[field.width] = row.width;
		config[field.height] = row.height;
	}

	function resolutionName(field: (typeof RESOLUTION_FIELDS)[number]): string {
		// The original's getVRChat*Resolution: the size set, named, or 1080p when unset.
		const key = resolutionKey(config[field.width], config[field.height]);
		return key === DEFAULT_RESOLUTION_KEY ? '1920x1080 (1080p)' : resolutionLabel(key);
	}

	async function save() {
		loading = true;
		try {
			await writeVrchatConfig(configToSave(config));
			open = false;
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_config_json_header()}</Dialog.Title>
			<Dialog.Description>{m.dialog_config_json_description1()} {m.dialog_config_json_description2()}</Dialog.Description>
		</Dialog.Header>
		<div class="flex max-h-[60vh] flex-col gap-4 overflow-y-auto pr-1">
			{#if agentCan('cache')}
				<div class="flex items-center justify-between gap-2 text-sm">
					<span class="font-medium">{m.dialog_config_json_cache_size()}</span>
					<span class="flex items-center gap-2">
						<span>{usedCache} / {totalCache} GB</span>
						<Button class="rounded-full" variant="outline" size="icon-sm" title={m.dialog_config_json_refresh()} aria-label={m.dialog_config_json_refresh()} disabled={cacheLoading} onclick={refreshCacheSize}>
							{#if cacheLoading}<Spinner />{:else}<RefreshCw />{/if}
						</Button>
					</span>
				</div>
				<div class="flex items-center justify-between gap-2 text-sm">
					<span class="font-medium">{m.dialog_config_json_delete_all_cache()}</span>
					<Button size="sm" variant="outline" onclick={askClearCache}>{m.dialog_config_json_delete_cache()}</Button>
				</div>
				<div class="flex items-center justify-between gap-2 text-sm">
					<span class="font-medium">{m.dialog_config_json_delete_old_cache()}</span>
					<Button size="sm" variant="outline" onclick={sweep}>{m.dialog_config_json_sweep_cache()}</Button>
				</div>
			{/if}
			{#each fields as field (field.key)}
				<label class="flex flex-col gap-2 text-sm">
					<span class="font-medium">{field.name}</span>
					<InputGroup.Root class="h-8">
						<InputGroup.Input
							class="h-8"
							type={field.type ?? 'text'}
							min={field.min}
							max={field.max}
							placeholder={field.placeholder}
							value={text(field.key)}
							oninput={(e) => (config[field.key] = e.currentTarget.value)} />
						{#if field.folder}
							<InputGroup.Addon align="inline-end">
								<Button variant="ghost" size="icon-sm" aria-label={field.name} disabled={!agentCan('pick_path')} onclick={() => browse(field.key)}><FolderOpen /></Button>
							</InputGroup.Addon>
						{/if}
					</InputGroup.Root>
				</label>
			{/each}
			{#each RESOLUTION_FIELDS as field (field.key)}
				<div class="flex items-center justify-between gap-2 text-sm">
					<span class="font-medium">{resolutionLabels[field.key]}</span>
					<Select.Root type="single" value={resolutionKey(config[field.width], config[field.height])} onValueChange={(v) => setResolution(field, v)}>
						<Select.Trigger size="sm" class="w-48">{resolutionName(field)}</Select.Trigger>
						<Select.Content>
							{#each field.rows as row (row.name)}<Select.Item value={resolutionKey(row.width, row.height)} label={row.name} />{/each}
						</Select.Content>
					</Select.Root>
				</div>
			{/each}
			<label class="flex w-fit cursor-pointer items-center gap-2 text-sm">
				<Checkbox checked={config.picture_output_split_by_date === true} onCheckedChange={(v) => (config.picture_output_split_by_date = v === true)} />
				{m.dialog_config_json_picture_sort_by_date()}
			</label>
			<label class="flex w-fit cursor-pointer items-center gap-2 text-sm">
				<Checkbox checked={config.disableRichPresence === true} onCheckedChange={(v) => (config.disableRichPresence = v === true)} />
				{m.dialog_config_json_disable_discord_presence()}
			</label>
		</div>
		<Dialog.Footer>
			<Button variant="outline" class="sm:mr-auto" onclick={() => externalLink.open('https://docs.vrchat.com/docs/configuration-file')}>{m.dialog_config_json_vrchat_docs()}</Button>
			<Button variant="secondary" onclick={() => (open = false)}>{m.dialog_config_json_cancel()}</Button>
			<Button disabled={loading} onclick={save}>{m.dialog_config_json_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<ActionDialogs bind:confirming />
