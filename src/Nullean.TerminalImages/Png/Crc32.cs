namespace Nullean.TerminalImages.Png;

/// <summary>The CRC-32 used by PNG chunks (polynomial 0xEDB88320). Hand-written so the package needs no hashing dependency.</summary>
internal static class Crc32
{
	private static readonly uint[] Table = BuildTable();

	private static uint[] BuildTable()
	{
		var table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			var c = n;
			for (var k = 0; k < 8; k++)
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}
		return table;
	}

	/// <summary>Continues a running CRC. Start with <see cref="Initial"/> and finish with <see cref="Finish"/>.</summary>
	public static uint Update(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (var b in data)
			crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	public const uint Initial = 0xFFFFFFFFu;

	public static uint Finish(uint crc) => crc ^ 0xFFFFFFFFu;

	public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));
}
