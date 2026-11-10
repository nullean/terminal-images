using System.Globalization;
using System.Text;

namespace Nullean.TerminalImages.Encoders;

/// <summary>How <see cref="SixelEncoder"/> picks its colours.</summary>
public enum SixelPalette
{
	/// <summary>Median cut over the image's own colours: best quality, a little slower.</summary>
	MedianCut,

	/// <summary>A fixed 3-3-2 bit palette (256 colours): the fastest, and stable between frames of an animation.</summary>
	Rgb332,
}

/// <summary>
/// The Sixel protocol, a DEC format from the 1980s that is still the most widely supported way to draw pixels: xterm, foot,
/// WezTerm, Windows Terminal, mlterm, Konsole and others.
/// </summary>
/// <remarks>
/// Sixel has no scaling: the image is drawn at its pixel size, so resize it to the cell area first. Pixels with alpha below
/// 128 are left undrawn, so the terminal background shows through.
/// </remarks>
public sealed class SixelEncoder : ITerminalImageEncoder
{
	private const int HistogramSize = 32768;

	/// <summary>How the palette is chosen.</summary>
	public SixelPalette Palette { get; init; } = SixelPalette.MedianCut;

	/// <summary>
	/// The most colours to use, 2 to 256. Terminals differ in how many colour registers they have (a few have only 16 or
	/// 256); the probe in the detection package reports it.
	/// </summary>
	public int MaxColors { get; init; } = 256;

	/// <summary>Whether to spread quantisation error to neighbouring pixels (Floyd-Steinberg). Looks better with few colours, costs time.</summary>
	public bool Dither { get; init; }

	/// <summary>Whether to compress repeated characters with <c>!n</c>. Disable only to debug.</summary>
	public bool RunLengthEncoding { get; init; } = true;

	/// <inheritdoc />
	public TerminalImageProtocol Protocol => TerminalImageProtocol.Sixel;

	/// <inheritdoc />
	public void Encode(RgbaImage image, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(output);
		var maxColors = Math.Clamp(MaxColors, 2, 256);

		var width = image.Width;
		var height = image.Height;
		var pixels = image.Pixels.Span;

		var palette = Palette == SixelPalette.Rgb332 && maxColors >= 256
			? FixedPalette()
			: MedianCut(pixels, maxColors);
		var indices = Quantize(pixels, width, height, palette, Dither);

		var hasTransparent = false;
		foreach (var index in indices)
		{
			if (index < 0)
			{
				hasTransparent = true;
				break;
			}
		}

		var used = new bool[palette.Count];
		foreach (var index in indices)
		{
			if (index >= 0)
				used[index] = true;
		}

		var text = new StringBuilder(1024);
		text.Append("\eP");
		if (hasTransparent)
			text.Append("0;1;0"); // P2=1: pixels left unset keep the existing background
		text.Append("q\"1;1;").Append(width.ToString(CultureInfo.InvariantCulture)).Append(';').Append(height.ToString(CultureInfo.InvariantCulture));
		for (var c = 0; c < palette.Count; c++)
		{
			if (!used[c])
				continue;
			text.Append('#').Append(c.ToString(CultureInfo.InvariantCulture)).Append(";2;")
				.Append(Percent(palette.R[c])).Append(';').Append(Percent(palette.G[c])).Append(';').Append(Percent(palette.B[c]));
		}

		output.Write(text.ToString());
		text.Clear();

		var present = new bool[palette.Count];
		var row = new byte[width];
		for (var top = 0; top < height; top += 6)
		{
			var rows = Math.Min(6, height - top);
			Array.Clear(present);
			for (var k = 0; k < rows; k++)
			{
				for (var x = 0; x < width; x++)
				{
					var index = indices[((top + k) * width) + x];
					if (index >= 0)
						present[index] = true;
				}
			}

			var firstColor = true;
			for (var c = 0; c < palette.Count; c++)
			{
				if (!present[c])
					continue;

				var last = -1;
				for (var x = 0; x < width; x++)
				{
					var bits = 0;
					for (var k = 0; k < rows; k++)
					{
						if (indices[((top + k) * width) + x] == c)
							bits |= 1 << k;
					}

					row[x] = (byte)bits;
					if (bits != 0)
						last = x;
				}

				if (!firstColor)
					text.Append('$'); // back to the left edge of the same band, to draw the next colour over it
				firstColor = false;
				text.Append('#').Append(c.ToString(CultureInfo.InvariantCulture));
				AppendRuns(text, row, last + 1, RunLengthEncoding);
			}

			if (top + 6 < height)
				text.Append('-'); // next band
			output.Write(text.ToString());
			text.Clear();
		}

		output.Write("\e\\");
	}

	private static string Percent(byte value) => (((value * 100) + 127) / 255).ToString(CultureInfo.InvariantCulture);

	private static void AppendRuns(StringBuilder text, byte[] row, int length, bool runLength)
	{
		var x = 0;
		while (x < length)
		{
			var value = row[x];
			var run = 1;
			while (x + run < length && row[x + run] == value)
				run++;
			var character = (char)(63 + value);
			if (runLength && run > 3)
				text.Append('!').Append(run.ToString(CultureInfo.InvariantCulture)).Append(character);
			else
				text.Append(character, run);
			x += run;
		}
	}

	private sealed class PaletteData(byte[] r, byte[] g, byte[] b, int[]? bucketToIndex)
	{
		public byte[] R { get; } = r;

		public byte[] G { get; } = g;

		public byte[] B { get; } = b;

		public int Count => R.Length;

		/// <summary>For median cut: the palette entry of each 5-5-5 colour bucket seen in the image. Null for the fixed palette.</summary>
		public int[]? BucketToIndex { get; } = bucketToIndex;
	}

	private static PaletteData FixedPalette()
	{
		var r = new byte[256];
		var g = new byte[256];
		var b = new byte[256];
		for (var i = 0; i < 256; i++)
		{
			r[i] = (byte)((i >> 5) * 255 / 7);
			g[i] = (byte)(((i >> 2) & 7) * 255 / 7);
			b[i] = (byte)((i & 3) * 255 / 3);
		}

		return new PaletteData(r, g, b, null);
	}

	private static int Bucket(int r, int g, int b) => (r >> 3 << 10) | (g >> 3 << 5) | (b >> 3);

	private static PaletteData MedianCut(ReadOnlySpan<byte> pixels, int maxColors)
	{
		var count = new int[HistogramSize];
		var sumR = new long[HistogramSize];
		var sumG = new long[HistogramSize];
		var sumB = new long[HistogramSize];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			if (pixels[i + 3] < 128)
				continue;
			var key = Bucket(pixels[i], pixels[i + 1], pixels[i + 2]);
			count[key]++;
			sumR[key] += pixels[i];
			sumG[key] += pixels[i + 1];
			sumB[key] += pixels[i + 2];
		}

		var items = new List<int>();
		for (var key = 0; key < HistogramSize; key++)
		{
			if (count[key] > 0)
				items.Add(key);
		}

		var keys = items.ToArray();
		var bucketToIndex = new int[HistogramSize];
		Array.Fill(bucketToIndex, -1);
		if (keys.Length == 0)
			return new PaletteData([], [], [], bucketToIndex);

		var boxes = new List<(int Start, int End)> { (0, keys.Length) };
		while (boxes.Count < maxColors)
		{
			// Split the box holding the most pixels that still has more than one colour.
			var pick = -1;
			long pickWeight = -1;
			for (var b = 0; b < boxes.Count; b++)
			{
				var (start, end) = boxes[b];
				if (end - start < 2)
					continue;
				long weight = 0;
				for (var i = start; i < end; i++)
					weight += count[keys[i]];
				if (weight > pickWeight)
				{
					pickWeight = weight;
					pick = b;
				}
			}

			if (pick < 0)
				break;

			var (boxStart, boxEnd) = boxes[pick];
			int minR = 31, maxR = 0, minG = 31, maxG = 0, minB = 31, maxB = 0;
			for (var i = boxStart; i < boxEnd; i++)
			{
				var key = keys[i];
				int r = (key >> 10) & 31, g = (key >> 5) & 31, b = key & 31;
				minR = Math.Min(minR, r);
				maxR = Math.Max(maxR, r);
				minG = Math.Min(minG, g);
				maxG = Math.Max(maxG, g);
				minB = Math.Min(minB, b);
				maxB = Math.Max(maxB, b);
			}

			int rangeR = maxR - minR, rangeG = maxG - minG, rangeB = maxB - minB;
			var channelShift = rangeR >= rangeG && rangeR >= rangeB ? 10 : rangeG >= rangeB ? 5 : 0;
			Array.Sort(keys, boxStart, boxEnd - boxStart, new ChannelComparer(channelShift));

			long half = pickWeight / 2, running = 0;
			var split = boxStart + 1;
			for (var i = boxStart; i < boxEnd - 1; i++)
			{
				running += count[keys[i]];
				split = i + 1;
				if (running >= half)
					break;
			}

			boxes[pick] = (boxStart, split);
			boxes.Add((split, boxEnd));
		}

		var red = new byte[boxes.Count];
		var green = new byte[boxes.Count];
		var blue = new byte[boxes.Count];
		for (var b = 0; b < boxes.Count; b++)
		{
			var (start, end) = boxes[b];
			long total = 0, r = 0, g = 0, bl = 0;
			for (var i = start; i < end; i++)
			{
				var key = keys[i];
				total += count[key];
				r += sumR[key];
				g += sumG[key];
				bl += sumB[key];
				bucketToIndex[key] = b;
			}

			red[b] = (byte)((r + (total / 2)) / total);
			green[b] = (byte)((g + (total / 2)) / total);
			blue[b] = (byte)((bl + (total / 2)) / total);
		}

		return new PaletteData(red, green, blue, bucketToIndex);
	}

	private sealed class ChannelComparer(int shift) : IComparer<int>
	{
		public int Compare(int x, int y) => ((x >> shift) & 31).CompareTo((y >> shift) & 31);
	}

	/// <summary>Palette index per pixel, or -1 for a transparent one.</summary>
	private static short[] Quantize(ReadOnlySpan<byte> pixels, int width, int height, PaletteData palette, bool dither)
	{
		var indices = new short[width * height];
		var nearestCache = new short[HistogramSize];
		Array.Fill(nearestCache, (short)-1);

		if (!dither)
		{
			for (var i = 0; i < indices.Length; i++)
			{
				var at = i * 4;
				indices[i] = pixels[at + 3] < 128 ? (short)-1 : Nearest(palette, nearestCache, pixels[at], pixels[at + 1], pixels[at + 2], exactBucket: true);
			}

			return indices;
		}

		// Floyd-Steinberg: carry the error of each pixel to the pixels right of it and in the row below.
		var current = new float[(width + 2) * 3];
		var next = new float[(width + 2) * 3];
		for (var y = 0; y < height; y++)
		{
			Array.Clear(next);
			for (var x = 0; x < width; x++)
			{
				var at = ((y * width) + x) * 4;
				if (pixels[at + 3] < 128)
				{
					indices[(y * width) + x] = -1;
					continue;
				}

				var e = (x + 1) * 3;
				var r = Math.Clamp(pixels[at] + current[e], 0, 255);
				var g = Math.Clamp(pixels[at + 1] + current[e + 1], 0, 255);
				var b = Math.Clamp(pixels[at + 2] + current[e + 2], 0, 255);
				var index = Nearest(palette, nearestCache, (int)Math.Round(r), (int)Math.Round(g), (int)Math.Round(b), exactBucket: false);
				indices[(y * width) + x] = index;

				float er = r - palette.R[index], eg = g - palette.G[index], eb = b - palette.B[index];
				Spread(current, e + 3, er, eg, eb, 7 / 16f);
				Spread(next, e - 3, er, eg, eb, 3 / 16f);
				Spread(next, e, er, eg, eb, 5 / 16f);
				Spread(next, e + 3, er, eg, eb, 1 / 16f);
			}

			(current, next) = (next, current);
		}

		return indices;
	}

	private static void Spread(float[] errors, int at, float r, float g, float b, float weight)
	{
		errors[at] += r * weight;
		errors[at + 1] += g * weight;
		errors[at + 2] += b * weight;
	}

	private static short Nearest(PaletteData palette, short[] cache, int r, int g, int b, bool exactBucket)
	{
		if (palette.BucketToIndex is null)
			return (short)((r >> 5 << 5) | (g >> 5 << 2) | (b >> 6)); // the fixed 3-3-2 palette: the index is the quantised colour

		var bucket = Bucket(r, g, b);
		if (exactBucket && palette.BucketToIndex[bucket] >= 0)
			return (short)palette.BucketToIndex[bucket]; // this colour was in the image: the box it landed in

		var cached = cache[bucket];
		if (cached >= 0)
			return cached;

		var best = 0;
		var bestDistance = int.MaxValue;
		for (var i = 0; i < palette.Count; i++)
		{
			int dr = r - palette.R[i], dg = g - palette.G[i], db = b - palette.B[i];
			var distance = (dr * dr) + (dg * dg) + (db * db);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}

		cache[bucket] = (short)best;
		return (short)best;
	}
}
