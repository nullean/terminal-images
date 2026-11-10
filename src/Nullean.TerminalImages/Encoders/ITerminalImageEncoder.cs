namespace Nullean.TerminalImages.Encoders;

/// <summary>Turns an image into the escape sequences (or characters) that make a terminal show it.</summary>
public interface ITerminalImageEncoder
{
	/// <summary>The protocol this encoder speaks.</summary>
	TerminalImageProtocol Protocol { get; }

	/// <summary>Writes the image to <paramref name="output"/>. The image is drawn at its pixel size: resize it first.</summary>
	/// <param name="image">The image.</param>
	/// <param name="cells">How many terminal cells the image should cover. Protocols that scale on the terminal side use it; others ignore it.</param>
	/// <param name="output">Where the sequences go. Nothing is flushed.</param>
	void Encode(RgbaImage image, CellSize cells, TextWriter output);
}
