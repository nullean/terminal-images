using System.Buffers.Binary;
using System.IO.Compression;

namespace Nullean.TerminalImages.Png;

/// <summary>Encodes an <see cref="RgbaImage"/> as an 8-bit RGBA PNG, with per-row adaptive filtering.</summary>
public static class PngEncoder
{
	private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>Encodes the image.</summary>
	/// <param name="image">The image.</param>
	/// <param name="compression">The zlib compression level. Use <see cref="CompressionLevel.Fastest"/> when encoding on every frame.</param>
	public static byte[] Encode(RgbaImage image, CompressionLevel compression = CompressionLevel.Optimal)
	{
		ArgumentNullException.ThrowIfNull(image);
		var stride = image.Width * 4;
		var pixels = image.Pixels.Span;

		var filtered = new byte[(stride + 1) * image.Height];
		var candidates = new byte[5][];
		for (var i = 0; i < 5; i++)
			candidates[i] = new byte[stride];

		for (var y = 0; y < image.Height; y++)
		{
			var row = pixels.Slice(y * stride, stride);
			var previous = y == 0 ? [] : pixels.Slice((y - 1) * stride, stride);

			var best = 0;
			var bestScore = long.MaxValue;
			for (var filter = 0; filter < 5; filter++)
			{
				ApplyFilter(filter, row, previous, candidates[filter]);
				var score = 0L;
				foreach (var b in candidates[filter])
					score += b < 128 ? b : 256 - b; // the usual "minimum sum of absolute differences" heuristic
				if (score < bestScore)
				{
					bestScore = score;
					best = filter;
				}
			}

			var offset = y * (stride + 1);
			filtered[offset] = (byte)best;
			candidates[best].CopyTo(filtered.AsSpan(offset + 1));
		}

		using var compressed = new MemoryStream();
		using (var zlib = new ZLibStream(compressed, compression, leaveOpen: true))
			zlib.Write(filtered);

		using var output = new MemoryStream(compressed.Capacity + 64);
		output.Write(Signature);

		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
		BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)image.Height);
		header[8] = 8; // bit depth
		header[9] = 6; // colour type: RGBA
		header[10] = 0; // compression
		header[11] = 0; // filter method
		header[12] = 0; // no interlace
		WriteChunk(output, "IHDR"u8, header);
		WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
		WriteChunk(output, "IEND"u8, []);
		return output.ToArray();
	}

	private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
		output.Write(word);
		output.Write(type);
		output.Write(data);
		var crc = Crc32.Update(Crc32.Initial, type);
		crc = Crc32.Update(crc, data);
		BinaryPrimitives.WriteUInt32BigEndian(word, Crc32.Finish(crc));
		output.Write(word);
	}

	private const int BytesPerPixel = 4;

	private static void ApplyFilter(int filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> destination)
	{
		for (var i = 0; i < row.Length; i++)
		{
			int a = i >= BytesPerPixel ? row[i - BytesPerPixel] : 0;
			int b = previous.IsEmpty ? 0 : previous[i];
			int c = i >= BytesPerPixel && !previous.IsEmpty ? previous[i - BytesPerPixel] : 0;
			var predictor = filter switch
			{
				0 => 0,
				1 => a,
				2 => b,
				3 => (a + b) / 2,
				_ => Paeth(a, b, c),
			};
			destination[i] = (byte)(row[i] - predictor);
		}
	}

	internal static int Paeth(int a, int b, int c)
	{
		var p = a + b - c;
		var pa = Math.Abs(p - a);
		var pb = Math.Abs(p - b);
		var pc = Math.Abs(p - c);
		if (pa <= pb && pa <= pc)
			return a;
		return pb <= pc ? b : c;
	}
}
