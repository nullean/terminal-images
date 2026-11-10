using AwesomeAssertions;
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.XenoAtom;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using P = Nullean.TerminalImages.TerminalImageProtocol;

namespace Nullean.TerminalImages.XenoAtom.Tests;

public class XenoAtomTerminalImagesTests
{
	private static (TerminalInstance Terminal, InMemoryTerminalBackend Backend) Create(
		bool ansi = true, bool redirected = false, TerminalColorLevel color = TerminalColorLevel.TrueColor)
	{
		var backend = new InMemoryTerminalBackend(new TerminalSize(80, 24), new TerminalCapabilities
		{
			AnsiEnabled = ansi,
			IsOutputRedirected = redirected,
			IsInputRedirected = false,
			ColorLevel = color,
		});
		return (new TerminalInstance(backend), backend);
	}

	private static TerminalImageCapabilities Caps(P protocol, bool trueColor = true) =>
		new() { Supported = [protocol], CellSize = new CellPixelSize(10, 20), TrueColor = trueColor };

	[Test]
	public void The_image_reaches_the_terminal_as_raw_sequences()
	{
		var (terminal, backend) = Create();

		var drawn = terminal.WriteImage(RgbaImage.Solid(100, 60, 1, 2, 3), Caps(P.Kitty));

		drawn.Should().BeTrue();
		var text = backend.GetOutText();
		text.Should().Contain("\e_Ga=T,f=32,s=100,v=60,c=10,r=3,q=2,m="); // XenoAtom did not escape or reinterpret it
		text.Should().EndWith("\e8\e[3B\r");
	}

	[Test]
	public void Half_blocks_follow_the_terminals_colour_level_through_the_capabilities()
	{
		var (terminal, backend) = Create();

		terminal.WriteImage(RgbaImage.Solid(2, 2, 255, 0, 0), Caps(P.HalfBlocks, trueColor: false));

		backend.GetOutText().Should().Contain("38;5;196").And.NotContain("38;2;");
	}

	[Test]
	public void Nothing_is_written_when_the_terminal_cannot_show_images()
	{
		var (terminal, backend) = Create();

		terminal.WriteImage(RgbaImage.Solid(2, 2, 1, 2, 3), TerminalImageCapabilities.None("test")).Should().BeFalse();

		backend.GetOutText().Should().BeEmpty();
	}

	[Test]
	public void The_image_is_fitted_to_the_terminal_window_by_default()
	{
		var (terminal, backend) = Create(); // 80x24 cells at 10x20 px: a box of 800x460 px (one row stays free)

		terminal.WriteImage(RgbaImage.Solid(4000, 4000, 1, 2, 3), Caps(P.Kitty));

		backend.GetOutText().Should().Contain("s=460,v=460,c=46,r=23");
	}

	[Test]
	public async Task Detection_gives_nothing_when_the_terminal_has_no_ANSI_or_output_is_redirected()
	{
		(await Create(ansi: false).Terminal.DetectImagesAsync()).Preferred.Should().Be(P.None);
		(await Create(redirected: true).Terminal.DetectImagesAsync()).Preferred.Should().Be(P.None);
	}

	[Test]
	public async Task Detection_takes_the_colour_level_from_XenoAtom()
	{
		(await Create(color: TerminalColorLevel.TrueColor).Terminal.DetectImagesAsync()).TrueColor.Should().BeTrue();
		(await Create(color: TerminalColorLevel.Color256).Terminal.DetectImagesAsync()).TrueColor.Should().BeFalse();
	}

	[Test]
	public void WritePng_decodes_the_file()
	{
		var (terminal, backend) = Create();
		var png = Png.PngEncoder.Encode(RgbaImage.Solid(4, 4, 9, 9, 9));

		terminal.WritePng(png, Caps(P.HalfBlocks)).Should().BeTrue();

		backend.GetOutText().Should().Contain("▀");
	}
}
