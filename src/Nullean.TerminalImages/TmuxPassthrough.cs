using System.Text;

namespace Nullean.TerminalImages;

/// <summary>
/// Wraps escape sequences in tmux's DCS passthrough (<c>ESC P tmux ; ... ESC \</c>) so they reach the outer terminal
/// instead of being interpreted by tmux. Needs tmux 3.3 or newer with <c>allow-passthrough on</c>.
/// </summary>
public static class TmuxPassthrough
{
	/// <summary>
	/// Wraps a string of escape sequences. Every <c>ESC \</c> ends a piece, and each piece is wrapped on its own, so a long
	/// Kitty image (many APC chunks) becomes many small passthrough sequences, which tmux handles better than one huge one.
	/// Every ESC inside a piece is doubled, as the protocol requires.
	/// </summary>
	public static string Wrap(string sequences)
	{
		ArgumentNullException.ThrowIfNull(sequences);
		if (sequences.Length == 0)
			return sequences;

		var result = new StringBuilder(sequences.Length + (sequences.Length / 64) + 16);
		var start = 0;
		while (start < sequences.Length)
		{
			var end = sequences.IndexOf("\e\\", start, StringComparison.Ordinal);
			end = end < 0 ? sequences.Length : end + 2;
			result.Append("\ePtmux;");
			for (var i = start; i < end; i++)
			{
				if (sequences[i] == '\e')
					result.Append('\e');
				result.Append(sequences[i]);
			}

			result.Append("\e\\");
			start = end;
		}

		return result.ToString();
	}
}
