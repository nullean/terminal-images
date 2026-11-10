namespace Nullean.TerminalImages;

/// <summary>How an image is sent to the terminal.</summary>
public enum TerminalImageProtocol
{
	/// <summary>No image support: nothing is drawn.</summary>
	None = 0,

	/// <summary>Unicode half-block characters with 24-bit colour. Works wherever truecolor does.</summary>
	HalfBlocks,

	/// <summary>The Sixel graphics protocol.</summary>
	Sixel,

	/// <summary>The iTerm2 inline images protocol (OSC 1337).</summary>
	ITerm2,

	/// <summary>The Kitty graphics protocol.</summary>
	Kitty,
}
