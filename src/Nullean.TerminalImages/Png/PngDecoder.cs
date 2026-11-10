using System.Buffers.Binary;
using System.IO.Compression;

namespace Nullean.TerminalImages.Png;

/// <summary>
/// Decodes PNG files of every colour type and bit depth, including Adam7 interlacing and <c>tRNS</c> transparency, to
/// 8-bit RGBA. Ancillary chunks such as gamma and colour profiles are ignored. 16-bit samples are reduced to their high byte.
/// </summary>
public static class PngDecoder
{
	private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>Whether the data starts with the PNG signature.</summary>
	public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

	/// <summary>Decodes a PNG file.</summary>
	/// <exception cref="InvalidDataException">The data is not a valid PNG.</exception>
	public static RgbaImage Decode(ReadOnlySpan<byte> png)
	{
		if (!IsPng(png))
			throw new InvalidDataException("Not a PNG: the signature is missing.");

		var position = 8;
		var header = default(Header);
		var haveHeader = false;
		byte[]? palette = null;
		byte[]? paletteAlpha = null;
		byte[]? transparentKey = null;
		using var idat = new MemoryStream();
		var sawEnd = false;

		while (position + 12 <= png.Length)
		{
			var length = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
			if (length > int.MaxValue || position + 12L + length > png.Length)
				throw new InvalidDataException("A PNG chunk runs past the end of the data.");
			var type = png.Slice(position + 4, 4);
			var data = png.Slice(position + 8, (int)length);
			var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png[(position + 8 + (int)length)..]);
			var actualCrc = Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Initial, type), data));
			if (expectedCrc != actualCrc)
				throw new InvalidDataException($"The {System.Text.Encoding.ASCII.GetString(type)} chunk has a bad CRC.");
			position += 12 + (int)length;

			if (type.SequenceEqual("IHDR"u8))
			{
				header = ParseHeader(data);
				haveHeader = true;
			}
			else if (!haveHeader)
			{
				throw new InvalidDataException("The first PNG chunk must be IHDR.");
			}
			else if (type.SequenceEqual("PLTE"u8))
			{
				if (data.Length % 3 != 0 || data.Length == 0 || data.Length > 768)
					throw new InvalidDataException("Invalid PLTE chunk.");
				palette = data.ToArray();
			}
			else if (type.SequenceEqual("tRNS"u8))
			{
				if (header.ColorType == 3)
					paletteAlpha = data.ToArray();
				else if (header.ColorType is 0 or 2)
					transparentKey = data.ToArray();
				// Colour types 4 and 6 have their own alpha channel, so tRNS is not allowed; ignore it.
			}
			else if (type.SequenceEqual("IDAT"u8))
			{
				idat.Write(data);
			}
			else if (type.SequenceEqual("IEND"u8))
			{
				sawEnd = true;
				break;
			}
		}

		if (!haveHeader)
			throw new InvalidDataException("The PNG has no IHDR chunk.");
		if (!sawEnd)
			throw new InvalidDataException("The PNG has no IEND chunk.");
		if (header.ColorType == 3 && palette is null)
			throw new InvalidDataException("A palette PNG has no PLTE chunk.");

		var raw = Inflate(idat.GetBuffer().AsSpan(0, (int)idat.Length));
		var rgba = new byte[checked(header.Width * header.Height * 4)];
		var converter = new PixelConverter(header, palette, paletteAlpha, transparentKey);

		if (header.Interlace == 0)
		{
			var consumed = DecodePass(raw, 0, header, header.Width, header.Height, converter, rgba, 0, 0, 1, 1);
			if (consumed < 0)
				throw new InvalidDataException("The PNG image data is shorter than the header says.");
		}
		else
		{
			var offset = 0;
			for (var pass = 0; pass < 7; pass++)
			{
				var passWidth = (header.Width - AdamStartX[pass] + AdamStepX[pass] - 1) / AdamStepX[pass];
				var passHeight = (header.Height - AdamStartY[pass] + AdamStepY[pass] - 1) / AdamStepY[pass];
				if (passWidth <= 0 || passHeight <= 0)
					continue;
				offset = DecodePass(raw, offset, header, passWidth, passHeight, converter, rgba, AdamStartX[pass], AdamStartY[pass], AdamStepX[pass], AdamStepY[pass]);
				if (offset < 0)
					throw new InvalidDataException("The PNG image data is shorter than the header says.");
			}
		}

		return new RgbaImage(header.Width, header.Height, rgba);
	}

	private static readonly int[] AdamStartX = [0, 4, 0, 2, 0, 1, 0];
	private static readonly int[] AdamStartY = [0, 0, 4, 0, 2, 0, 1];
	private static readonly int[] AdamStepX = [8, 8, 4, 4, 2, 2, 1];
	private static readonly int[] AdamStepY = [8, 8, 8, 4, 4, 2, 2];

	private readonly record struct Header(int Width, int Height, int BitDepth, int ColorType, int Interlace)
	{
		public int Channels => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };

		public int BitsPerPixel => Channels * BitDepth;
	}

	private static Header ParseHeader(ReadOnlySpan<byte> data)
	{
		if (data.Length != 13)
			throw new InvalidDataException("Invalid IHDR chunk.");
		var width = BinaryPrimitives.ReadUInt32BigEndian(data);
		var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
		int depth = data[8];
		int colorType = data[9];
		int interlace = data[12];

		if (width == 0 || height == 0 || width > 65535 * 4u || height > 65535 * 4u || (ulong)width * height > 400_000_000UL)
			throw new InvalidDataException($"Unsupported PNG size {width}x{height}.");
		var validDepth = colorType switch
		{
			0 => depth is 1 or 2 or 4 or 8 or 16,
			2 or 4 or 6 => depth is 8 or 16,
			3 => depth is 1 or 2 or 4 or 8,
			_ => false,
		};
		if (!validDepth)
			throw new InvalidDataException($"Invalid PNG colour type {colorType} with bit depth {depth}.");
		if (data[10] != 0 || data[11] != 0 || interlace > 1)
			throw new InvalidDataException("Unsupported PNG compression, filter or interlace method.");
		return new Header((int)width, (int)height, depth, colorType, interlace);
	}

	private static byte[] Inflate(ReadOnlySpan<byte> compressed)
	{
		using var input = new MemoryStream(compressed.ToArray());
		using var zlib = new ZLibStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		try
		{
			zlib.CopyTo(output);
		}
		catch (InvalidDataException exception)
		{
			throw new InvalidDataException("The PNG image data is corrupt.", exception);
		}
		return output.ToArray();
	}

	/// <summary>Reads one (sub)image from <paramref name="raw"/>, returning the offset after it, or -1 when the data is too short.</summary>
	private static int DecodePass(byte[] raw, int offset, Header header, int width, int height, PixelConverter converter, byte[] rgba, int startX, int startY, int stepX, int stepY)
	{
		var stride = ((width * header.BitsPerPixel) + 7) / 8;
		var bytesPerPixel = Math.Max(1, header.BitsPerPixel / 8);
		if (offset + ((long)(stride + 1) * height) > raw.Length)
			return -1;

		var previous = new byte[stride];
		var current = new byte[stride];
		for (var y = 0; y < height; y++)
		{
			var filter = raw[offset++];
			raw.AsSpan(offset, stride).CopyTo(current);
			offset += stride;
			Unfilter(filter, current, previous, bytesPerPixel);

			var destY = startY + (y * stepY);
			for (var x = 0; x < width; x++)
			{
				var destination = ((destY * header.Width) + startX + (x * stepX)) * 4;
				converter.Convert(current, x, rgba.AsSpan(destination, 4));
			}

			(previous, current) = (current, previous);
		}

		return offset;
	}

	private static void Unfilter(byte filter, byte[] row, byte[] previous, int bytesPerPixel)
	{
		switch (filter)
		{
			case 0:
				break;
			case 1:
				for (var i = bytesPerPixel; i < row.Length; i++)
					row[i] = (byte)(row[i] + row[i - bytesPerPixel]);
				break;
			case 2:
				for (var i = 0; i < row.Length; i++)
					row[i] = (byte)(row[i] + previous[i]);
				break;
			case 3:
				for (var i = 0; i < row.Length; i++)
				{
					var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
					row[i] = (byte)(row[i] + ((left + previous[i]) / 2));
				}
				break;
			case 4:
				for (var i = 0; i < row.Length; i++)
				{
					var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
					var upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
					row[i] = (byte)(row[i] + PngEncoder.Paeth(left, previous[i], upperLeft));
				}
				break;
			default:
				throw new InvalidDataException($"Unknown PNG row filter {filter}.");
		}
	}

	private sealed class PixelConverter(Header header, byte[]? palette, byte[]? paletteAlpha, byte[]? transparentKey)
	{
		private readonly int _maxSample = (1 << header.BitDepth) - 1;

		public void Convert(byte[] row, int x, Span<byte> destination)
		{
			switch (header.ColorType)
			{
				case 0:
					{
						var sample = Sample(row, x, 0, 1);
						var gray = Scale(sample);
						var transparent = transparentKey is { Length: >= 2 } && sample == ((transparentKey[0] << 8) | transparentKey[1]);
						destination[0] = destination[1] = destination[2] = gray;
						destination[3] = transparent ? (byte)0 : (byte)255;
						break;
					}
				case 2:
					{
						int r = Sample(row, x, 0, 3), g = Sample(row, x, 1, 3), b = Sample(row, x, 2, 3);
						var transparent = transparentKey is { Length: >= 6 }
							&& r == ((transparentKey[0] << 8) | transparentKey[1])
							&& g == ((transparentKey[2] << 8) | transparentKey[3])
							&& b == ((transparentKey[4] << 8) | transparentKey[5]);
						destination[0] = Scale(r);
						destination[1] = Scale(g);
						destination[2] = Scale(b);
						destination[3] = transparent ? (byte)0 : (byte)255;
						break;
					}
				case 3:
					{
						var index = Sample(row, x, 0, 1);
						if (palette is null || (index * 3) + 2 >= palette.Length)
							throw new InvalidDataException("A PNG pixel refers to a palette entry that does not exist.");
						destination[0] = palette[index * 3];
						destination[1] = palette[(index * 3) + 1];
						destination[2] = palette[(index * 3) + 2];
						destination[3] = paletteAlpha is not null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
						break;
					}
				case 4:
					{
						var gray = Scale(Sample(row, x, 0, 2));
						destination[0] = destination[1] = destination[2] = gray;
						destination[3] = Scale(Sample(row, x, 1, 2));
						break;
					}
				default:
					destination[0] = Scale(Sample(row, x, 0, 4));
					destination[1] = Scale(Sample(row, x, 1, 4));
					destination[2] = Scale(Sample(row, x, 2, 4));
					destination[3] = Scale(Sample(row, x, 3, 4));
					break;
			}
		}

		/// <summary>The raw sample (not scaled) for one channel of pixel <paramref name="x"/>.</summary>
		private int Sample(byte[] row, int x, int channel, int channels)
		{
			switch (header.BitDepth)
			{
				case 8:
					return row[(x * channels) + channel];
				case 16:
					{
						var at = ((x * channels) + channel) * 2;
						return (row[at] << 8) | row[at + 1];
					}
				default:
					{
						// 1, 2 and 4 bits: only one channel, packed most significant bit first.
						var bitIndex = x * header.BitDepth;
						var shift = 8 - header.BitDepth - (bitIndex % 8);
						return (row[bitIndex / 8] >> shift) & _maxSample;
					}
			}
		}

		private byte Scale(int sample) => header.BitDepth switch
		{
			8 => (byte)sample,
			16 => (byte)(sample >> 8),
			_ => (byte)(sample * 255 / _maxSample),
		};
	}
}
