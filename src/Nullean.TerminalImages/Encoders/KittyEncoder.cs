using System.Globalization;
using System.IO.Compression;

namespace Nullean.TerminalImages.Encoders;

/// <summary>
/// The Kitty graphics protocol: base64 pixel data in APC sequences of at most 4096 bytes each. Terminals that speak it
/// include Kitty, Ghostty and WezTerm.
/// </summary>
/// <remarks>Responses are suppressed (<c>q=2</c>), so the terminal never writes replies into the application's input.</remarks>
public sealed class KittyEncoder : ITerminalImageEncoder
{
	private const int ChunkSize = 4096;

	/// <summary>An id that lets the image be replaced or deleted later. Zero means anonymous.</summary>
	public uint ImageId { get; init; }

	/// <summary>Whether to deflate the pixels (<c>o=z</c>). Smaller on the wire, a little more work.</summary>
	public bool Compress { get; init; }

	/// <inheritdoc />
	public TerminalImageProtocol Protocol => TerminalImageProtocol.Kitty;

	/// <inheritdoc />
	public void Encode(RgbaImage image, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(output);

		var pixels = image.Pixels.ToArray();
		var keys = new System.Text.StringBuilder("a=T,f=32,s=")
			.Append(image.Width.ToString(CultureInfo.InvariantCulture))
			.Append(",v=").Append(image.Height.ToString(CultureInfo.InvariantCulture));
		if (Compress)
		{
			pixels = Deflate(pixels);
			keys.Append(",o=z");
		}

		WriteChunks(keys, pixels, cells, output);
	}

	/// <summary>Writes a PNG file as-is (<c>f=100</c>): the terminal decodes it, so nothing is re-encoded.</summary>
	public void EncodePng(ReadOnlySpan<byte> png, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(output);
		WriteChunks(new System.Text.StringBuilder("a=T,f=100"), png.ToArray(), cells, output);
	}

	/// <summary>The sequence that deletes an image and frees its data (<c>a=d,d=I</c>).</summary>
	public static string Delete(uint imageId) =>
		string.Create(CultureInfo.InvariantCulture, $"\e_Ga=d,d=I,i={imageId},q=2\e\\");

	private void WriteChunks(System.Text.StringBuilder firstKeys, byte[] payload, CellSize cells, TextWriter output)
	{
		if (ImageId != 0)
			firstKeys.Append(",i=").Append(ImageId.ToString(CultureInfo.InvariantCulture));
		if (cells.IsSpecified)
			firstKeys.Append(",c=").Append(cells.Columns.ToString(CultureInfo.InvariantCulture)).Append(",r=").Append(cells.Rows.ToString(CultureInfo.InvariantCulture));
		firstKeys.Append(",q=2");

		var encoded = Convert.ToBase64String(payload);
		var first = true;
		var offset = 0;
		do
		{
			var length = Math.Min(ChunkSize, encoded.Length - offset);
			var more = offset + length < encoded.Length;
			output.Write("\e_G");
			if (first)
			{
				output.Write(firstKeys.ToString());
				output.Write(more ? ",m=1" : ",m=0");
			}
			else
			{
				output.Write(more ? "m=1" : "m=0");
			}

			output.Write(';');
			output.Write(encoded.AsSpan(offset, length));
			output.Write("\e\\");
			first = false;
			offset += length;
		} while (offset < encoded.Length);
	}

	private static byte[] Deflate(byte[] data)
	{
		using var buffer = new MemoryStream();
		using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
			zlib.Write(data);
		return buffer.ToArray();
	}
}
