namespace Nullean.TerminalImages;

/// <summary>
/// An image as 8-bit RGBA pixels (non-premultiplied alpha), row by row with no padding: <c>Width * Height * 4</c> bytes.
/// </summary>
public sealed class RgbaImage
{
	/// <summary>Creates an image over the given pixels. The pixels are not copied.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The size is not positive.</exception>
	/// <exception cref="ArgumentException">The pixel buffer is not exactly <c>width * height * 4</c> bytes.</exception>
	public RgbaImage(int width, int height, ReadOnlyMemory<byte> pixels)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		var expected = checked((long)width * height * 4);
		if (pixels.Length != expected)
			throw new ArgumentException($"Expected {expected} bytes for a {width}x{height} RGBA image, got {pixels.Length}.", nameof(pixels));
		Width = width;
		Height = height;
		Pixels = pixels;
	}

	/// <summary>Width in pixels.</summary>
	public int Width { get; }

	/// <summary>Height in pixels.</summary>
	public int Height { get; }

	/// <summary>The pixels: R, G, B, A per pixel, rows top to bottom.</summary>
	public ReadOnlyMemory<byte> Pixels { get; }

	/// <summary>Whether any pixel is not fully opaque.</summary>
	public bool HasTransparency
	{
		get
		{
			var span = Pixels.Span;
			for (var i = 3; i < span.Length; i += 4)
			{
				if (span[i] != 255)
					return true;
			}
			return false;
		}
	}

	/// <summary>Creates a fully transparent image filled with one colour, mainly for tests and placeholders.</summary>
	public static RgbaImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
	{
		var pixels = new byte[checked(width * height * 4)];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = r;
			pixels[i + 1] = g;
			pixels[i + 2] = b;
			pixels[i + 3] = a;
		}
		return new RgbaImage(width, height, pixels);
	}

	/// <summary>Returns a copy composited over an opaque background colour.</summary>
	public RgbaImage CompositeOver(byte r, byte g, byte b)
	{
		var src = Pixels.Span;
		var dst = new byte[src.Length];
		for (var i = 0; i < src.Length; i += 4)
		{
			var a = src[i + 3];
			dst[i] = Blend(src[i], r, a);
			dst[i + 1] = Blend(src[i + 1], g, a);
			dst[i + 2] = Blend(src[i + 2], b, a);
			dst[i + 3] = 255;
		}
		return new RgbaImage(Width, Height, dst);
	}

	private static byte Blend(byte foreground, byte background, byte alpha) => (byte)(((foreground * alpha) + (background * (255 - alpha)) + 127) / 255);
}
