using System.Globalization;

namespace Nullean.TerminalImages.Encoders;

/// <summary>How many colours the half-block output may use.</summary>
public enum HalfBlockColorDepth
{
	/// <summary>24-bit colour (<c>38;2;r;g;b</c>).</summary>
	TrueColor,

	/// <summary>The xterm 256-colour palette (<c>38;5;n</c>), for terminals without truecolor.</summary>
	Palette256,
}

/// <summary>
/// Draws with the upper half block (U+2580): each character cell shows two vertically stacked pixels, the top as
/// foreground and the bottom as background colour. Works in any terminal with colour, which makes it the fallback.
/// </summary>
/// <remarks>Transparent pixels (alpha below 128) leave the terminal's own background showing.</remarks>
public sealed class HalfBlockEncoder : ITerminalImageEncoder
{
	/// <summary>The colour depth to emit.</summary>
	public HalfBlockColorDepth ColorDepth { get; init; }

	/// <inheritdoc />
	public TerminalImageProtocol Protocol => TerminalImageProtocol.HalfBlocks;

	/// <summary>
	/// Writes one text line per two pixel rows, each ending in a newline. An odd last row is drawn with a transparent partner.
	/// </summary>
	public void Encode(RgbaImage image, CellSize cells, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(output);

		var pixels = image.Pixels.Span;
		var line = new System.Text.StringBuilder();
		for (var y = 0; y < image.Height; y += 2)
		{
			line.Clear();
			int? foreground = -1, background = -1; // -1: unknown. null: terminal default.
			for (var x = 0; x < image.Width; x++)
			{
				var top = Pixel(pixels, image.Width, x, y);
				var bottom = y + 1 < image.Height ? Pixel(pixels, image.Width, x, y + 1) : null;

				char glyph;
				int? wantForeground, wantBackground;
				if (top is not null && bottom is not null)
				{
					glyph = '▀';
					wantForeground = top;
					wantBackground = bottom;
				}
				else if (top is not null)
				{
					glyph = '▀';
					wantForeground = top;
					wantBackground = null;
				}
				else if (bottom is not null)
				{
					glyph = '▄';
					wantForeground = bottom;
					wantBackground = null;
				}
				else
				{
					glyph = ' ';
					wantForeground = foreground == -1 ? null : foreground; // a space shows only the background
					wantBackground = null;
				}

				if (foreground != wantForeground)
				{
					AppendColor(line, foreground: true, wantForeground);
					foreground = wantForeground;
				}

				if (background != wantBackground)
				{
					AppendColor(line, foreground: false, wantBackground);
					background = wantBackground;
				}

				line.Append(glyph);
			}

			line.Append("\e[0m\n");
			output.Write(line.ToString());
		}
	}

	private static int? Pixel(ReadOnlySpan<byte> pixels, int stride, int x, int y)
	{
		var at = ((y * stride) + x) * 4;
		return pixels[at + 3] < 128 ? null : (pixels[at] << 16) | (pixels[at + 1] << 8) | pixels[at + 2];
	}

	private void AppendColor(System.Text.StringBuilder line, bool foreground, int? rgb)
	{
		if (rgb is null)
		{
			line.Append(foreground ? "\e[39m" : "\e[49m");
			return;
		}

		var r = (rgb.Value >> 16) & 0xFF;
		var g = (rgb.Value >> 8) & 0xFF;
		var b = rgb.Value & 0xFF;
		line.Append("\e[").Append(foreground ? '3' : '4');
		if (ColorDepth == HalfBlockColorDepth.TrueColor)
			line
				.Append("8;2;")
				.Append(r.ToString(CultureInfo.InvariantCulture))
				.Append(';')
				.Append(g.ToString(CultureInfo.InvariantCulture))
				.Append(';')
				.Append(b.ToString(CultureInfo.InvariantCulture));
		else
			line.Append("8;5;").Append(ToXterm256(r, g, b).ToString(CultureInfo.InvariantCulture));
		line.Append('m');
	}

	/// <summary>The nearest entry of the xterm 256-colour palette: the 6x6x6 cube (16-231) or the grey ramp (232-255).</summary>
	internal static int ToXterm256(int r, int g, int b)
	{
		static int Level(int value) => value < 48 ? 0 : value < 115 ? 1 : (value - 35) / 40;

		static int CubeValue(int level) => level == 0 ? 0 : 55 + (40 * level);

		var cr = Level(r);
		var cg = Level(g);
		var cb = Level(b);
		var cube = 16 + (36 * cr) + (6 * cg) + cb;

		var average = (r + g + b) / 3;
		var greyIndex = Math.Clamp((average - 8 + 5) / 10, 0, 23);
		var greyValue = 8 + (10 * greyIndex);

		var cubeDistance = Square(r - CubeValue(cr)) + Square(g - CubeValue(cg)) + Square(b - CubeValue(cb));
		var greyDistance = Square(r - greyValue) + Square(g - greyValue) + Square(b - greyValue);
		return greyDistance < cubeDistance ? 232 + greyIndex : cube;
	}

	private static int Square(int value) => value * value;
}
