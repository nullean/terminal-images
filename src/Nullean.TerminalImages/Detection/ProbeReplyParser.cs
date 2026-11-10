using System.Globalization;

namespace Nullean.TerminalImages.Detection;

/// <summary>What a terminal answered to the probe, as far as it could be understood.</summary>
public sealed record ProbeReplies
{
	/// <summary>Whether a Primary Device Attributes reply arrived. It is the last query of the batch, so this means the terminal is done answering.</summary>
	public bool HasDeviceAttributes { get; init; }

	/// <summary>The attributes from the DA1 reply. Attribute 4 means Sixel.</summary>
	public IReadOnlyList<int> DeviceAttributes { get; init; } = [];

	/// <summary>The XTVERSION text, such as <c>kitty(0.32.2)</c> or <c>WezTerm 20240203</c>.</summary>
	public string? TerminalVersion { get; init; }

	/// <summary>The brand part of <see cref="TerminalVersion"/> (<c>kitty</c>, <c>WezTerm</c>...), or null.</summary>
	public string? TerminalName { get; init; }

	/// <summary>The version part of <see cref="TerminalVersion"/>, or null.</summary>
	public string? TerminalVersionNumber { get; init; }

	/// <summary>The answer to the Kitty graphics query: null when there was none.</summary>
	public bool? KittyGraphics { get; init; }

	/// <summary>The cell size in pixels from <c>CSI 16 t</c>.</summary>
	public CellPixelSize? CellSize { get; init; }

	/// <summary>The number of Sixel colour registers from XTSMGRAPHICS.</summary>
	public int? SixelColorRegisters { get; init; }

	/// <summary>Whether DA1 lists Sixel (attribute 4).</summary>
	public bool SupportsSixel => DeviceAttributes.Contains(4);
}

/// <summary>Reads the terminal's answers to the detection probe. Pure: it takes the text the terminal sent and returns facts.</summary>
public static class ProbeReplyParser
{
	/// <summary>The id the Kitty query uses, so its reply can be told from any other graphics reply.</summary>
	public const int KittyQueryId = 31;

	/// <summary>Parses every reply found in <paramref name="text"/>. Unknown or damaged sequences are skipped.</summary>
	public static ProbeReplies Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		var replies = new ProbeReplies();
		var i = 0;
		while (i < text.Length)
		{
			if (text[i] != '\e' || i + 1 >= text.Length)
			{
				i++;
				continue;
			}

			switch (text[i + 1])
			{
				case '[':
					i = ParseCsi(text, i + 2, ref replies);
					break;
				case 'P':
					i = ParseStringControl(text, i + 2, isDcs: true, ref replies);
					break;
				case '_':
					i = ParseStringControl(text, i + 2, isDcs: false, ref replies);
					break;
				default:
					i += 2;
					break;
			}
		}

		return replies;
	}

	/// <summary>Whether the text already holds the DA1 reply, which ends the probe.</summary>
	public static bool IsComplete(string text) => Parse(text).HasDeviceAttributes;

	private static int ParseCsi(string text, int start, ref ProbeReplies replies)
	{
		var i = start;
		var isPrivate = false;
		if (i < text.Length && text[i] is '?' or '>' or '=')
		{
			isPrivate = text[i] == '?';
			i++;
		}

		var parametersStart = i;
		while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == ';'))
			i++;
		if (i >= text.Length)
			return text.Length;

		var final = text[i];
		var parameters = ParseNumbers(text.AsSpan(parametersStart, i - parametersStart));
		i++;

		switch (final)
		{
			case 'c' when isPrivate:
				replies = replies with { HasDeviceAttributes = true, DeviceAttributes = parameters };
				break;
			case 't' when !isPrivate && parameters.Length >= 3 && parameters[0] == 6 && parameters[1] > 0 && parameters[2] > 0:
				replies = replies with { CellSize = new CellPixelSize(parameters[2], parameters[1]) }; // CSI 6 ; height ; width t
				break;
			case 'S' when isPrivate && parameters.Length >= 3 && parameters[0] == 1 && parameters[1] == 0:
				replies = replies with { SixelColorRegisters = parameters[2] }; // CSI ? 1 ; 0 ; registers S
				break;
		}

		return i;
	}

	private static int ParseStringControl(string text, int start, bool isDcs, ref ProbeReplies replies)
	{
		// The string runs to ST (ESC \) or BEL.
		var end = text.IndexOf("\e\\", start, StringComparison.Ordinal);
		var terminatorLength = 2;
		var bel = text.IndexOf('\a', start);
		if (bel >= 0 && (end < 0 || bel < end))
		{
			end = bel;
			terminatorLength = 1;
		}

		if (end < 0)
			return text.Length;

		var body = text[start..end];
		if (isDcs && body.StartsWith(">|", StringComparison.Ordinal))
		{
			var version = body[2..];
			var (name, number) = SplitVersion(version);
			replies = replies with { TerminalVersion = version, TerminalName = name, TerminalVersionNumber = number };
		}
		else if (!isDcs && body.StartsWith('G'))
		{
			var separator = body.IndexOf(';');
			var keys = separator < 0 ? body[1..] : body[1..separator];
			var message = separator < 0 ? string.Empty : body[(separator + 1)..];
			if (keys.Split(',').Contains($"i={KittyQueryId.ToString(CultureInfo.InvariantCulture)}"))
				replies = replies with { KittyGraphics = message == "OK" };
		}

		return end + terminatorLength;
	}

	/// <summary>Splits <c>kitty(0.32.2)</c> or <c>WezTerm 20240203-110809</c> into a name and a version.</summary>
	internal static (string Name, string? Version) SplitVersion(string text)
	{
		var open = text.IndexOf('(');
		if (open > 0 && text.EndsWith(')'))
			return (text[..open], text[(open + 1)..^1]);
		var space = text.IndexOf(' ');
		return space > 0 ? (text[..space], text[(space + 1)..]) : (text, null);
	}

	private static int[] ParseNumbers(ReadOnlySpan<char> text)
	{
		if (text.IsEmpty)
			return [];
		var result = new List<int>();
		foreach (var range in text.Split(';'))
			result.Add(int.TryParse(text[range], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0);
		return [.. result];
	}
}
