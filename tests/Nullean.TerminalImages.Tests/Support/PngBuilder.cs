using System.Buffers.Binary;
using System.IO.Compression;

namespace Nullean.TerminalImages.Tests.Support;

/// <summary>
/// Builds PNG files of any colour type, bit depth and interlace method for decoder tests. It is deliberately independent of
/// <c>PngEncoder</c>, so the decoder is not checked against the code it shares bugs with. Row filters rotate through all five
/// types so every unfilter path is exercised.
/// </summary>
internal static class PngBuilder
{
	public static byte[] Build(
		int colorType, int bitDepth, bool interlace, int width, int height,
		Func<int, int, int, int> sample, byte[]? palette = null, byte[]? transparency = null)
	{
		var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
		var bitsPerPixel = channels * bitDepth;
		var bytesPerPixel = Math.Max(1, bitsPerPixel / 8);

		using var raw = new MemoryStream();
		var filterIndex = 0;

		void WritePass(int startX, int startY, int stepX, int stepY)
		{
			var passWidth = (width - startX + stepX - 1) / stepX;
			var passHeight = (height - startY + stepY - 1) / stepY;
			if (passWidth <= 0 || passHeight <= 0)
				return;
			var stride = ((passWidth * bitsPerPixel) + 7) / 8;
			var previous = new byte[stride];
			for (var j = 0; j < passHeight; j++)
			{
				var row = new byte[stride];
				for (var i = 0; i < passWidth; i++)
				{
					for (var c = 0; c < channels; c++)
					{
						var value = sample(startX + (i * stepX), startY + (j * stepY), c);
						PutSample(row, (i * channels) + c, bitDepth, value);
					}
				}

				var filter = filterIndex++ % 5;
				raw.WriteByte((byte)filter);
				for (var k = 0; k < stride; k++)
				{
					int a = k >= bytesPerPixel ? row[k - bytesPerPixel] : 0;
					int b = previous[k];
					int c2 = k >= bytesPerPixel ? previous[k - bytesPerPixel] : 0;
					var predictor = filter switch
					{
						0 => 0,
						1 => a,
						2 => b,
						3 => (a + b) / 2,
						_ => Paeth(a, b, c2),
					};
					raw.WriteByte((byte)(row[k] - predictor));
				}

				previous = row;
			}
		}

		if (!interlace)
		{
			WritePass(0, 0, 1, 1);
		}
		else
		{
			int[] startX = [0, 4, 0, 2, 0, 1, 0], startY = [0, 0, 4, 0, 2, 0, 1], stepX = [8, 8, 4, 4, 2, 2, 1], stepY = [8, 8, 8, 4, 4, 2, 2];
			for (var pass = 0; pass < 7; pass++)
				WritePass(startX[pass], startY[pass], stepX[pass], stepY[pass]);
		}

		using var compressed = new MemoryStream();
		using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
			zlib.Write(raw.ToArray());

		using var file = new MemoryStream();
		file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		var header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
		header[8] = (byte)bitDepth;
		header[9] = (byte)colorType;
		header[12] = (byte)(interlace ? 1 : 0);
		Chunk(file, "IHDR", header);
		if (palette is not null)
			Chunk(file, "PLTE", palette);
		if (transparency is not null)
			Chunk(file, "tRNS", transparency);
		// Split the image data in two chunks: decoders must join them.
		var data = compressed.ToArray();
		var half = data.Length / 2;
		Chunk(file, "IDAT", data[..half]);
		Chunk(file, "IDAT", data[half..]);
		Chunk(file, "IEND", []);
		return file.ToArray();
	}

	public static void Chunk(Stream file, string type, byte[] data)
	{
		var word = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
		file.Write(word);
		var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
		file.Write(typeBytes);
		file.Write(data);
		BinaryPrimitives.WriteUInt32BigEndian(word, SlowCrc(typeBytes, data));
		file.Write(word);
	}

	/// <summary>A bitwise CRC-32, written differently from the library's table version.</summary>
	private static uint SlowCrc(byte[] type, byte[] data)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var b in type.Concat(data))
		{
			crc ^= b;
			for (var k = 0; k < 8; k++)
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}

		return ~crc;
	}

	private static void PutSample(byte[] row, int index, int bitDepth, int value)
	{
		switch (bitDepth)
		{
			case 8:
				row[index] = (byte)value;
				break;
			case 16:
				row[index * 2] = (byte)(value >> 8);
				row[(index * 2) + 1] = (byte)value;
				break;
			default:
				{
					var bit = index * bitDepth;
					row[bit / 8] |= (byte)(value << (8 - bitDepth - (bit % 8)));
					break;
				}
		}
	}

	private static int Paeth(int a, int b, int c)
	{
		var p = a + b - c;
		int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
