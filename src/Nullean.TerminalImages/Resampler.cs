namespace Nullean.TerminalImages;

/// <summary>How <see cref="Resampler.Resize"/> picks output pixels.</summary>
public enum ResampleMode
{
	/// <summary>Area averaging when shrinking, bilinear when growing. The right choice for photos.</summary>
	Auto,

	/// <summary>The nearest source pixel. Keeps hard edges, for pixel art.</summary>
	Nearest,
}

/// <summary>Resizing in managed code, so the package needs no imaging library.</summary>
public static class Resampler
{
	/// <summary>The size an image of <paramref name="width"/>x<paramref name="height"/> gets when fitted inside a box, keeping its aspect ratio. Never returns less than 1x1.</summary>
	/// <param name="width">The image width in pixels.</param>
	/// <param name="height">The image height in pixels.</param>
	/// <param name="maxWidth">The box width.</param>
	/// <param name="maxHeight">The box height.</param>
	/// <param name="allowUpscale">Whether a small image may grow to fill the box.</param>
	public static (int Width, int Height) FitWithin(int width, int height, int maxWidth, int maxHeight, bool allowUpscale = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

		var scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
		if (!allowUpscale && scale > 1)
			scale = 1;
		return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
	}

	/// <summary>Returns the image resized to the given size. Returns the same instance when the size already matches.</summary>
	public static RgbaImage Resize(RgbaImage image, int width, int height, ResampleMode mode = ResampleMode.Auto)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		if (width == image.Width && height == image.Height)
			return image;

		var output = new byte[checked(width * height * 4)];
		if (mode == ResampleMode.Nearest)
			Nearest(image, width, height, output);
		else if (width <= image.Width && height <= image.Height)
			Area(image, width, height, output);
		else
			Bilinear(image, width, height, output);
		return new RgbaImage(width, height, output);
	}

	private static void Nearest(RgbaImage image, int width, int height, byte[] output)
	{
		var source = image.Pixels.Span;
		for (var y = 0; y < height; y++)
		{
			var sy = Math.Min(image.Height - 1, (int)((y + 0.5) * image.Height / height));
			for (var x = 0; x < width; x++)
			{
				var sx = Math.Min(image.Width - 1, (int)((x + 0.5) * image.Width / width));
				source.Slice(((sy * image.Width) + sx) * 4, 4).CopyTo(output.AsSpan(((y * width) + x) * 4));
			}
		}
	}

	/// <summary>Each output pixel is the coverage-weighted average of the source pixels it covers, in premultiplied alpha.</summary>
	private static void Area(RgbaImage image, int width, int height, byte[] output)
	{
		var source = image.Pixels.Span;
		var scaleX = (double)image.Width / width;
		var scaleY = (double)image.Height / height;
		for (var y = 0; y < height; y++)
		{
			var y0 = y * scaleY;
			var y1 = (y + 1) * scaleY;
			var firstRow = (int)y0;
			var lastRow = Math.Min(image.Height - 1, (int)Math.Ceiling(y1) - 1);
			for (var x = 0; x < width; x++)
			{
				var x0 = x * scaleX;
				var x1 = (x + 1) * scaleX;
				var firstColumn = (int)x0;
				var lastColumn = Math.Min(image.Width - 1, (int)Math.Ceiling(x1) - 1);

				double r = 0, g = 0, b = 0, a = 0, total = 0;
				for (var sy = firstRow; sy <= lastRow; sy++)
				{
					var wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
					for (var sx = firstColumn; sx <= lastColumn; sx++)
					{
						var weight = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
						var at = ((sy * image.Width) + sx) * 4;
						var alpha = source[at + 3] / 255.0;
						r += source[at] * alpha * weight;
						g += source[at + 1] * alpha * weight;
						b += source[at + 2] * alpha * weight;
						a += alpha * weight;
						total += weight;
					}
				}

				WritePremultiplied(output, ((y * width) + x) * 4, r, g, b, a, total);
			}
		}
	}

	private static void Bilinear(RgbaImage image, int width, int height, byte[] output)
	{
		var source = image.Pixels.Span;
		for (var y = 0; y < height; y++)
		{
			var fy = Math.Clamp(((y + 0.5) * image.Height / height) - 0.5, 0, image.Height - 1);
			var y0 = (int)fy;
			var y1 = Math.Min(image.Height - 1, y0 + 1);
			var ty = fy - y0;
			for (var x = 0; x < width; x++)
			{
				var fx = Math.Clamp(((x + 0.5) * image.Width / width) - 0.5, 0, image.Width - 1);
				var x0 = (int)fx;
				var x1 = Math.Min(image.Width - 1, x0 + 1);
				var tx = fx - x0;

				double r = 0, g = 0, b = 0, a = 0;
				Accumulate(source, image.Width, x0, y0, (1 - tx) * (1 - ty), ref r, ref g, ref b, ref a);
				Accumulate(source, image.Width, x1, y0, tx * (1 - ty), ref r, ref g, ref b, ref a);
				Accumulate(source, image.Width, x0, y1, (1 - tx) * ty, ref r, ref g, ref b, ref a);
				Accumulate(source, image.Width, x1, y1, tx * ty, ref r, ref g, ref b, ref a);
				WritePremultiplied(output, ((y * width) + x) * 4, r, g, b, a, 1);
			}
		}
	}

	private static void Accumulate(ReadOnlySpan<byte> source, int stride, int x, int y, double weight, ref double r, ref double g, ref double b, ref double a)
	{
		var at = ((y * stride) + x) * 4;
		var alpha = source[at + 3] / 255.0;
		r += source[at] * alpha * weight;
		g += source[at + 1] * alpha * weight;
		b += source[at + 2] * alpha * weight;
		a += alpha * weight;
	}

	private static void WritePremultiplied(byte[] output, int at, double r, double g, double b, double a, double total)
	{
		if (a <= 0)
		{
			output[at] = output[at + 1] = output[at + 2] = output[at + 3] = 0;
			return;
		}

		// r, g, b are alpha-weighted sums: dividing by the accumulated alpha gives the straight colour.
		output[at] = ToByte(r / a);
		output[at + 1] = ToByte(g / a);
		output[at + 2] = ToByte(b / a);
		output[at + 3] = ToByte(a / total * 255);
	}

	private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
