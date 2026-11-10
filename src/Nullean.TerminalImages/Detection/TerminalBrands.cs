namespace Nullean.TerminalImages.Detection;

/// <summary>
/// What is known about specific terminals: which protocols to prefer and in which order. This is plain data so it is easy
/// to correct. The order encodes quirks, not just support: a terminal can speak a protocol badly and be better off with another.
/// </summary>
/// <remarks>
/// The entries come from how other terminal image tools treat these terminals (yazi, ratatui-image, notcurses, term-image)
/// and from the terminals' own documentation. They have not all been tried on the real terminals.
/// </remarks>
public static class TerminalBrands
{
	private static readonly Dictionary<string, TerminalImageProtocol[]> Preferences = new(StringComparer.OrdinalIgnoreCase)
	{
		["kitty"] = [TerminalImageProtocol.Kitty],
		["ghostty"] = [TerminalImageProtocol.Kitty],

		// WezTerm speaks all three, but its Kitty and Sixel support are rougher than its iTerm2 images.
		["wezterm"] = [TerminalImageProtocol.ITerm2, TerminalImageProtocol.Sixel],
		["iterm2"] = [TerminalImageProtocol.ITerm2],

		// Konsole's Sixel is buggy, and its Kitty support partial.
		["konsole"] = [TerminalImageProtocol.ITerm2],
		["warp"] = [TerminalImageProtocol.ITerm2],
		["rio"] = [TerminalImageProtocol.ITerm2, TerminalImageProtocol.Sixel],
		["bobcat"] = [TerminalImageProtocol.ITerm2],
		["mintty"] = [TerminalImageProtocol.ITerm2, TerminalImageProtocol.Sixel],
		["vscode"] = [TerminalImageProtocol.ITerm2, TerminalImageProtocol.Sixel],

		["foot"] = [TerminalImageProtocol.Sixel],
		["windows-terminal"] = [TerminalImageProtocol.Sixel],
		["mlterm"] = [TerminalImageProtocol.Sixel],
		["contour"] = [TerminalImageProtocol.Sixel],

		// Known to have none of the three: half blocks are the best there is.
		["apple-terminal"] = [],

		// xterm only has Sixel when started with -ti vt340, so it is left to the probe (DA1 reports it).
		["xterm"] = [],
	};

	/// <summary>Whether the terminal is known, and its preferred protocols (best first, possibly empty).</summary>
	public static bool TryGetPreferences(string? brand, out IReadOnlyList<TerminalImageProtocol> protocols)
	{
		if (brand is not null && Preferences.TryGetValue(brand, out var found))
		{
			protocols = found;
			return true;
		}

		protocols = [];
		return false;
	}

	/// <summary>Whether the terminal is known to draw a different kind of image when the Kitty probe is sent, so the probe is skipped.</summary>
	public static bool SkipsKittyProbe(string? brand) => string.Equals(brand, "iterm2", StringComparison.OrdinalIgnoreCase); // the query garbles the window title

	/// <summary>Whether 24-bit colour is certain for this terminal without <c>COLORTERM</c>.</summary>
	public static bool HasTrueColor(string? brand) => brand is "kitty" or "ghostty" or "wezterm" or "iterm2" or "foot" or "windows-terminal" or "warp" or "vscode" or "rio" or "konsole";

	/// <summary>Maps an XTVERSION name such as <c>kitty</c>, <c>WezTerm</c>, <c>iTerm2</c> or <c>XTerm</c> to a brand id.</summary>
	public static string? FromVersionName(string name)
	{
		var lower = name.Trim().ToLowerInvariant();
		return lower switch
		{
			"iterm2" or "iterm" => "iterm2",
			"wezterm" => "wezterm",
			"kitty" => "kitty",
			"ghostty" => "ghostty",
			"foot" => "foot",
			"xterm" => "xterm",
			"mlterm" => "mlterm",
			"contour" => "contour",
			"konsole" => "konsole",
			"mintty" => "mintty",
			"rio" => "rio",
			"warpterminal" or "warp" => "warp",
			_ => null,
		};
	}
}
