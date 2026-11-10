using System.Text.RegularExpressions;

namespace Nullean.TerminalImages.Tests.Support;

/// <summary>A small Sixel reader for tests: turns the encoder's output back into a grid of colours (null = not drawn).</summary>
internal static partial class SixelDecoder
{
	public sealed record Result(int Width, int Height, (int R, int G, int B)?[,] Pixels, bool TransparentBackground, int RegistersDefined);

	public static Result Decode(string sequence)
	{
		var match = Header().Match(sequence);
		if (!match.Success)
			throw new FormatException("Not a sixel sequence: " + sequence[..Math.Min(40, sequence.Length)]);
		if (!sequence.EndsWith("\e\\", StringComparison.Ordinal))
			throw new FormatException("A sixel sequence ends with ST (ESC \\).");

		var transparent = match.Groups["p2"].Value == "1";
		var width = int.Parse(match.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture);
		var height = int.Parse(match.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture);
		var pixels = new (int R, int G, int B)?[width, height];
		var palette = new Dictionary<int, (int R, int G, int B)>();

		var body = sequence[match.Length..^2];
		var i = 0;
		var x = 0;
		var band = 0;
		var color = 0;
		var repeat = 1;

		int Number()
		{
			var start = i;
			while (i < body.Length && char.IsAsciiDigit(body[i]))
				i++;
			return i == start ? 0 : int.Parse(body[start..i], System.Globalization.CultureInfo.InvariantCulture);
		}

		while (i < body.Length)
		{
			var c = body[i];
			switch (c)
			{
				case '#':
					{
						i++;
						var index = Number();
						if (i < body.Length && body[i] == ';')
						{
							i++;
							Number(); // colour space: 2 = RGB
							i++;
							var r = Number();
							i++;
							var g = Number();
							i++;
							var b = Number();
							palette[index] = (((r * 255) + 50) / 100, ((g * 255) + 50) / 100, ((b * 255) + 50) / 100);
						}

						color = index;
						break;
					}
				case '!':
					i++;
					repeat = Number();
					break;
				case '$':
					x = 0;
					i++;
					break;
				case '-':
					band++;
					x = 0;
					i++;
					break;
				default:
					{
						if (c is < '?' or > '~')
							throw new FormatException($"Unexpected character '{c}' at {i}.");
						var bits = c - 63;
						for (var n = 0; n < repeat; n++, x++)
						{
							for (var k = 0; k < 6; k++)
							{
								if ((bits & (1 << k)) == 0)
									continue;
								var y = (band * 6) + k;
								if (x < width && y < height)
									pixels[x, y] = palette[color];
							}
						}

						repeat = 1;
						i++;
						break;
					}
			}
		}

		return new Result(width, height, pixels, transparent, palette.Count);
	}

	[GeneratedRegex("^\\eP(?<p1>\\d*)(;(?<p2>\\d*);(?<p3>\\d*))?q\"1;1;(?<w>\\d+);(?<h>\\d+)")]
	private static partial Regex Header();
}
