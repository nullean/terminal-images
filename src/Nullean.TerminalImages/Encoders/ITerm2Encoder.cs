using System.Globalization;
using Nullean.TerminalImages.Png;

namespace Nullean.TerminalImages.Encoders;

/// <summary>
/// The iTerm2 inline images protocol (OSC 1337). The terminal gets a whole image file, so pixels are sent as a PNG.
/// Spoken by iTerm2, WezTerm, Konsole, mintty and others.
/// </summary>
public sealed class ITerm2Encoder : ITerminalImageEncoder
{
	/// <inheritdoc />
	public TerminalImageProtocol Protocol => TerminalImageProtocol.ITerm2;

	/// <inheritdoc />
	public void Encode(RgbaImage image, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(image);
		var png = PngEncoder.Encode(image, System.IO.Compression.CompressionLevel.Fastest);
		Write(png, image.Width, image.Height, cells, output);
	}

	/// <summary>Writes an already-encoded image file (PNG, JPEG, GIF...) untouched. The terminal decodes it.</summary>
	public static void EncodeFile(ReadOnlySpan<byte> file, CellSize cells, TextWriter output) => Write(file, 0, 0, cells, output);

	private static void Write(ReadOnlySpan<byte> file, int pixelWidth, int pixelHeight, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(output);
		output.Write("\e]1337;File=inline=1;size=");
		output.Write(file.Length.ToString(CultureInfo.InvariantCulture));

		// Cells win: the terminal scales the image to them. Without cells, fall back to the pixel size when it is known.
		if (cells.IsSpecified)
		{
			output.Write(";width=");
			output.Write(cells.Columns.ToString(CultureInfo.InvariantCulture));
			output.Write(";height=");
			output.Write(cells.Rows.ToString(CultureInfo.InvariantCulture));
		}
		else if (pixelWidth > 0)
		{
			output.Write(string.Create(CultureInfo.InvariantCulture, $";width={pixelWidth}px;height={pixelHeight}px"));
		}

		output.Write(";preserveAspectRatio=1:");
		output.Write(Convert.ToBase64String(file));
		output.Write('\a');
	}
}
