// What the original's desktop app did to an image before sending it to VRChat
// (Dotnet/AppApi/Common/ImageSaving.cs), done in the browser so any client can upload.

const MAX_SIDE = 2000;
const MAX_BYTES = 10_000_000;
export const MAX_INPUT_BYTES = 20_000_000;

function loadBitmap(blob: Blob): Promise<HTMLImageElement> {
	return new Promise((resolve, reject) => {
		const url = URL.createObjectURL(blob);
		const image = new Image();
		image.onload = () => {
			URL.revokeObjectURL(url);
			resolve(image);
		};
		image.onerror = () => {
			URL.revokeObjectURL(url);
			reject(new Error("File isn't an image"));
		};
		image.src = url;
	});
}

function toPng(canvas: HTMLCanvasElement): Promise<Blob> {
	return new Promise((resolve, reject) => canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not encode the image'))), 'image/png'));
}

function drawn(source: CanvasImageSource, canvasWidth: number, canvasHeight: number, x: number, y: number, width: number, height: number): HTMLCanvasElement {
	const canvas = document.createElement('canvas');
	canvas.width = canvasWidth;
	canvas.height = canvasHeight;
	canvas.getContext('2d')!.drawImage(source, x, y, width, height);
	return canvas;
}

/** ResizeImageToFitLimits: at most 2000 on a side, optionally padded square, under 10 MB as PNG. */
export async function fitImage(blob: Blob, matchingDimensions: boolean): Promise<Blob> {
	const image = await loadBitmap(blob);
	let width = image.naturalWidth;
	let height = image.naturalHeight;
	if (width > MAX_SIDE) {
		height = Math.round(height / (width / MAX_SIDE));
		width = MAX_SIDE;
	}
	if (height > MAX_SIDE) {
		width = Math.round(width / (height / MAX_SIDE));
		height = MAX_SIDE;
	}
	let canvas = drawn(image, width, height, 0, 0, width, height);
	if (matchingDimensions && width !== height) {
		const side = Math.max(width, height);
		canvas = drawn(canvas, side, side, Math.floor((side - width) / 2), Math.floor((side - height) / 2), width, height);
	}
	let png = await toPng(canvas);
	for (let i = 0; i < 250 && png.size > MAX_BYTES; i++) {
		const nextWidth = canvas.width > canvas.height ? canvas.width - 25 : Math.round(canvas.width / (canvas.height / (canvas.height - 25)));
		const nextHeight = canvas.width > canvas.height ? Math.round(canvas.height / (canvas.width / (canvas.width - 25))) : canvas.height - 25;
		canvas = drawn(canvas, nextWidth, nextHeight, 0, 0, nextWidth, nextHeight);
		png = await toPng(canvas);
	}
	if (png.size > MAX_BYTES) throw new Error('Failed to get image into target filesize.');
	return png;
}

/** CropPrint and ResizePrintImage: an old white border comes off a 2048x1440 print, and the result is 1920x1080 inside a new border. */
export async function fitPrint(blob: Blob, cropWhiteBorder: boolean): Promise<Blob> {
	const image = await loadBitmap(blob);
	let source: CanvasImageSource = image;
	let width = image.naturalWidth;
	let height = image.naturalHeight;
	if (cropWhiteBorder && width === 2048 && height === 1440) {
		source = drawn(image, 1920, 1080, -64, -69, 2048, 1440);
		width = 1920;
		height = 1080;
	}
	if (height > width) {
		// A quarter turn counter-clockwise, as the original.
		const turned = document.createElement('canvas');
		turned.width = height;
		turned.height = width;
		const context = turned.getContext('2d')!;
		context.translate(0, width);
		context.rotate(-Math.PI / 2);
		context.drawImage(source, 0, 0);
		source = turned;
		[width, height] = [height, width];
	}
	// Too small: fitted into 1920x1080 on white.
	if (width < 1920 || height < 1080) {
		const aspect = width / height;
		const fittedWidth = aspect > 1920 / 1080 ? 1920 : Math.trunc(1080 * aspect);
		const fittedHeight = aspect > 1920 / 1080 ? Math.trunc(1920 / aspect) : 1080;
		const target = document.createElement('canvas');
		target.width = 1920;
		target.height = 1080;
		const context = target.getContext('2d')!;
		context.fillStyle = '#fff';
		context.fillRect(0, 0, 1920, 1080);
		context.drawImage(source, Math.trunc((1920 - fittedWidth) / 2), Math.trunc((1080 - fittedHeight) / 2), fittedWidth, fittedHeight);
		source = target;
		width = 1920;
		height = 1080;
	}
	// Too big: scaled down to fit.
	if (width > 1920) {
		height = Math.round(height / (width / 1920));
		width = 1920;
	}
	if (height > 1080) {
		width = Math.round(width / (height / 1080));
		height = 1080;
	}
	// The white border every print has.
	const print = document.createElement('canvas');
	print.width = 2048;
	print.height = 1440;
	const context = print.getContext('2d')!;
	context.fillStyle = '#fff';
	context.fillRect(0, 0, 2048, 1440);
	context.drawImage(source, Math.trunc((2048 - width) / 2), 69, width, height);
	return toPng(print);
}

export const EMOJI_STYLE_URL = 'https://assets.vrchat.com/www/images/emoji-previews/';

export const EMOJI_STYLES: Record<string, string> = {
	Aura: 'Preview_B2-Aura.gif',
	Bats: 'Preview_B2-Fall_Bats.gif',
	Bees: 'Preview_B2-Bees.gif',
	Bounce: 'Preview_B2-Bounce.gif',
	Cloud: 'Preview_B2-Cloud.gif',
	Confetti: 'Preview_B2-Winter_Confetti.gif',
	Crying: 'Preview_B2-Crying.gif',
	Dislike: 'Preview_B2-Dislike.gif',
	Fire: 'Preview_B2-Fire.gif',
	Idea: 'Preview_B2-Idea.gif',
	Lasers: 'Preview_B2-Lasers.gif',
	Like: 'Preview_B2-Like.gif',
	Magnet: 'Preview_B2-Magnet.gif',
	Mistletoe: 'Preview_B2-Winter_Mistletoe.gif',
	Money: 'Preview_B2-Money.gif',
	Noise: 'Preview_B2-Noise.gif',
	Orbit: 'Preview_B2-Orbit.gif',
	Pizza: 'Preview_B2-Pizza.gif',
	Rain: 'Preview_B2-Rain.gif',
	Rotate: 'Preview_B2-Rotate.gif',
	Shake: 'Preview_B2-Shake.gif',
	Snow: 'Preview_B2-Spin.gif',
	Snowball: 'Preview_B2-Winter_Snowball.gif',
	Spin: 'Preview_B2-Spin.gif',
	Splash: 'Preview_B2-SummerSplash.gif',
	Stop: 'Preview_B2-Stop.gif',
	ZZZ: 'Preview_B2-ZZZ.gif'
};
