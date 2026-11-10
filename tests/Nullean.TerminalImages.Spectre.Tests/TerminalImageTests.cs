using AwesomeAssertions;
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Spectre;
using Spectre.Console;
using Spectre.Console.Rendering;
using P = Nullean.TerminalImages.TerminalImageProtocol;

namespace Nullean.TerminalImages.Spectre.Tests;

public class TerminalImageTests
{
	private static TerminalImageCapabilities Caps(P protocol, bool multiplexer = false) =>
		new() { Supported = [protocol], CellSize = new CellPixelSize(10, 20), TrueColor = true, IsMultiplexer = multiplexer };

	private static string Render(Action<IAnsiConsole> write, AnsiSupport ansi = AnsiSupport.Yes)
	{
		var writer = new StringWriter();
		var console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = ansi,
			ColorSystem = ColorSystemSupport.TrueColor,
			Interactive = InteractionSupport.No,
			Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }, // CI enrichers (GitHub Actions) would force ANSI on
			Out = new AnsiConsoleOutput(writer),
		});
		console.Profile.Width = 80;
		console.Profile.Height = 30;
		write(console);
		return writer.ToString();
	}

	[Test]
	public void Kitty_draws_in_one_control_segment_and_reserves_the_rows_with_line_breaks()
	{
		// 100x60 px at 10x20 px per cell: 10 columns, 3 rows.
		var image = new TerminalImage(RgbaImage.Solid(100, 60, 1, 2, 3), Caps(P.Kitty));

		var text = Render(console => console.Write(image));

		text.Should().StartWith("\e7\e_Ga=T,f=32,s=100,v=60,c=10,r=3,q=2,m=");
		text.Should().Contain("\e\\\e8");
		text.Replace("\r\n", "\n").Count(c => c == '\n').Should().Be(3); // one line break per row: the next write starts below the image
	}

	[Test]
	public void The_image_is_not_overwritten_by_padding()
	{
		var text = Render(console => console.Write(new TerminalImage(RgbaImage.Solid(100, 60, 1, 2, 3), Caps(P.Sixel))));

		var afterImage = text[(text.IndexOf("\e8", StringComparison.Ordinal) + 2)..];
		afterImage.Trim('\r', '\n').Should().BeEmpty(); // only line breaks follow the drawing: no spaces that would erase a Sixel image
	}

	[Test]
	public void Sixel_and_iTerm2_use_their_own_encoders()
	{
		var sixel = Render(console => console.Write(new TerminalImage(RgbaImage.Solid(20, 20, 1, 2, 3), Caps(P.Sixel))));
		var iterm = Render(console => console.Write(new TerminalImage(RgbaImage.Solid(20, 20, 1, 2, 3), Caps(P.ITerm2))));

		sixel.Should().Contain("\eP").And.Contain("q\"1;1;20;20");
		iterm.Should().Contain("\e]1337;File=inline=1;");
	}

	[Test]
	public void In_tmux_the_image_is_wrapped_in_passthrough()
	{
		var text = Render(console => console.Write(new TerminalImage(RgbaImage.Solid(10, 20, 1, 2, 3), Caps(P.Kitty, multiplexer: true))));

		text.Should().Contain("\ePtmux;\e\e_G");
	}

	[Test]
	public void Half_blocks_are_drawn_with_Spectre_colours()
	{
		var text = Render(console => console.Write(new TerminalImage(RgbaImage.Solid(2, 2, 10, 20, 30), Caps(P.HalfBlocks))));

		text.Should().Contain("▀▀"); // Spectre merges neighbouring segments with the same style
		text.Should().Contain("38;2;10;20;30").And.Contain("48;2;10;20;30");
	}

	[Test]
	public void Half_blocks_downgrade_with_the_console_colour_system()
	{
		var writer = new StringWriter();
		var console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = AnsiSupport.Yes,
			ColorSystem = ColorSystemSupport.EightBit,
			Interactive = InteractionSupport.No,
			Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false }, // CI enrichers (GitHub Actions) would force ANSI on
			Out = new AnsiConsoleOutput(writer),
		});

		console.Write(new TerminalImage(RgbaImage.Solid(2, 2, 255, 0, 0), Caps(P.HalfBlocks)));

		writer.ToString().Should().Contain("38;5;").And.NotContain("38;2;"); // Spectre did the downgrade, not us
	}

	[Test]
	public void Without_ANSI_only_the_alt_text_is_written()
	{
		var image = new TerminalImage(RgbaImage.Solid(100, 60, 1, 2, 3), Caps(P.Kitty)) { AltText = "[a gradient]" };

		var text = Render(console => console.Write(image), AnsiSupport.No);

		text.Replace("\r\n", "\n").Should().Be("[a gradient]\n");
	}

	[Test]
	public void A_terminal_with_no_protocol_writes_the_alt_text()
	{
		var image = new TerminalImage(RgbaImage.Solid(10, 10, 1, 2, 3), TerminalImageCapabilities.None("test")) { AltText = "alt" };

		Render(console => console.Write(image)).Should().Contain("alt").And.NotContain("\e_G");
	}

	[Test]
	public void MaxWidth_and_MaxHeight_limit_the_image()
	{
		var image = new TerminalImage(RgbaImage.Solid(1000, 1000, 1, 2, 3), Caps(P.Kitty)) { MaxWidth = 5, MaxHeight = 4 };

		// 5x4 cells = 50x80 px; the square image fits at 50x50: 5 columns, 3 rows (50/20 rounded up).
		Render(console => console.Write(image)).Should().Contain("s=50,v=50,c=5,r=3");
	}

	[Test]
	public void MaxWidth_cannot_make_the_image_wider_than_the_console_offers()
	{
		// The console is 80 columns wide; asking for 500 must not overflow it.
		var image = new TerminalImage(RgbaImage.Solid(4000, 100, 1, 2, 3), Caps(P.Kitty)) { MaxWidth = 500 };

		Render(console => console.Write(image)).Should().Contain(",c=80,");
	}

	[Test]
	public void The_measurement_is_the_width_in_cells_and_never_exceeds_what_is_offered()
	{
		var image = new TerminalImage(RgbaImage.Solid(2000, 100, 1, 2, 3), Caps(P.Kitty));
		var width = 0;

		Render(console =>
		{
			var measurement = ((IRenderable)image).Measure(RenderOptions.Create(console), maxWidth: 40);
			width = measurement.Max;
		});

		width.Should().Be(40);
	}

	[Test]
	public void An_image_can_sit_inside_a_layout_without_throwing()
	{
		var text = Render(console => console.Write(new Table().AddColumn("img").AddRow(new TerminalImage(RgbaImage.Solid(20, 20, 1, 2, 3), Caps(P.Kitty)))));

		text.Should().Contain("\e_G");
	}

	[Test]
	public void FromPng_decodes_the_file()
	{
		var png = Png.PngEncoder.Encode(RgbaImage.Solid(4, 4, 9, 9, 9));

		var text = Render(console => console.Write(TerminalImage.FromPng(png, Caps(P.HalfBlocks))));

		text.Should().Contain("▀");
	}
}
