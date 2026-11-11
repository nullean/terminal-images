using AwesomeAssertions;
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Tests.Support;
using P = Nullean.TerminalImages.TerminalImageProtocol;

namespace Nullean.TerminalImages.Tests;

public class RendererTests
{
	private static TerminalImageCapabilities Caps(
		P protocol,
		bool multiplexer = false,
		CellPixelSize? cell = null,
		int? registers = null,
		bool trueColor = true
	) => new() { Supported = [protocol], IsMultiplexer = multiplexer, CellSize = cell, SixelColorRegisters = registers, TrueColor = trueColor };

	private static string Render(TerminalImageCapabilities caps, RgbaImage image, TerminalImageRenderOptions? options = null)
	{
		var writer = new StringWriter();
		new TerminalImageRenderer(caps, options).Render(image, writer);
		return writer.ToString();
	}

	[Test]
	public void The_layout_fits_the_image_in_the_box_using_the_real_cell_size()
	{
		var renderer = new TerminalImageRenderer(Caps(P.Kitty, cell: new CellPixelSize(10, 20)));

		// Box: 80x24 cells = 800x480 px. 1000x500 shrinks by 0.8 to 800x400: 80 columns, 20 rows.
		renderer.Layout(RgbaImage.Solid(1000, 500, 0, 0, 0)).Should().Be(new TerminalImageLayout(800, 400, new CellSize(80, 20)));
	}

	[Test]
	public void Half_blocks_have_two_pixels_per_cell_vertically()
	{
		var renderer = new TerminalImageRenderer(Caps(P.HalfBlocks));

		// Box: 80 columns x 24 rows = 80x48 px. 100x100 shrinks to 48x48: 48 columns, 24 rows.
		renderer.Layout(RgbaImage.Solid(100, 100, 0, 0, 0)).Should().Be(new TerminalImageLayout(48, 48, new CellSize(48, 24)));
	}

	[Test]
	public void A_small_image_keeps_its_size_unless_upscaling_is_allowed()
	{
		var caps = Caps(P.Kitty, cell: new CellPixelSize(10, 20));
		var image = RgbaImage.Solid(30, 40, 0, 0, 0);

		new TerminalImageRenderer(caps).Layout(image).Should().Be(new TerminalImageLayout(30, 40, new CellSize(3, 2)));
		new TerminalImageRenderer(caps, new TerminalImageRenderOptions { AllowUpscale = true, MaxColumns = 10, MaxRows = 10 })
			.Layout(image)
			.Should()
			.Be(new TerminalImageLayout(100, 133, new CellSize(10, 7))); // width-limited: 100/30 = 3.33x
	}

	[Test]
	public void Kitty_output_reserves_room_draws_and_leaves_the_cursor_below()
	{
		var text = Render(Caps(P.Kitty, cell: new CellPixelSize(10, 20)), RgbaImage.Solid(100, 60, 1, 2, 3));

		// 100x60 px at 10x20 = 10 columns, 3 rows.
		text.Should().StartWith("\n\n\n\e[3A\e7\e_Ga=T,f=32,s=100,v=60,c=10,r=3,q=2,m=");
		text.Should().EndWith("\e\\\e8\e[3B\r");
	}

	[Test]
	public void The_image_is_scaled_down_before_it_is_sent()
	{
		var options = new TerminalImageRenderOptions { MaxColumns = 4, MaxRows = 2 };

		var text = Render(Caps(P.Kitty, cell: new CellPixelSize(10, 20)), RgbaImage.Solid(400, 400, 1, 2, 3), options);

		text.Should().Contain("s=40,v=40,c=4,r=2"); // 4x2 cells = 40x40 px, not 400x400
	}

	[Test]
	public void In_tmux_the_image_is_wrapped_but_the_cursor_handling_is_not()
	{
		var caps = Caps(P.Kitty, multiplexer: true, cell: new CellPixelSize(10, 20));
		var image = RgbaImage.Solid(10, 20, 1, 2, 3);

		var wrapped = Render(caps, image);
		wrapped.Should().Contain("\e7\ePtmux;\e\e_G").And.Contain("\e\\\e8\e[1B\r");

		var plain = Render(caps, image, new TerminalImageRenderOptions { TmuxPassthrough = false });
		plain.Should().NotContain("tmux;");
	}

	[Test]
	public void Half_blocks_are_plain_text_and_never_wrapped_even_in_tmux()
	{
		var text = Render(Caps(P.HalfBlocks, multiplexer: true), RgbaImage.Solid(2, 2, 10, 20, 30));

		text.Should().Be("\e[38;2;10;20;30m\e[48;2;10;20;30m▀▀\e[0m\n");
	}

	[Test]
	public void Half_blocks_use_256_colours_without_truecolor()
	{
		var text = Render(Caps(P.HalfBlocks, trueColor: false), RgbaImage.Solid(1, 2, 255, 0, 0));

		text.Should().Contain("\e[38;5;196m");
	}

	[Test]
	public void Nothing_is_written_when_there_is_no_protocol()
	{
		var writer = new StringWriter();

		var drawn = new TerminalImageRenderer(TerminalImageCapabilities.None("test")).Render(RgbaImage.Solid(2, 2, 0, 0, 0), writer);

		drawn.Should().BeFalse();
		writer.ToString().Should().BeEmpty();
	}

	[Test]
	public void Sixel_uses_the_terminals_colour_register_count()
	{
		var pixels = new byte[32 * 32 * 4];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = (byte)(i * 7);
			pixels[i + 1] = (byte)(i * 13);
			pixels[i + 2] = (byte)(i * 3);
			pixels[i + 3] = 255;
		}

		var text = Render(Caps(P.Sixel, cell: new CellPixelSize(8, 16), registers: 16), new RgbaImage(32, 32, pixels));

		var sixel = text[text.IndexOf("\eP", StringComparison.Ordinal)..(text.IndexOf("\e\\", StringComparison.Ordinal) + 2)];
		SixelDecoder.Decode(sixel).RegistersDefined.Should().BeLessThanOrEqualTo(16);
	}

	[Test]
	public void RenderPng_decodes_and_draws()
	{
		var png = Png.PngEncoder.Encode(RgbaImage.Solid(2, 2, 10, 20, 30));
		var writer = new StringWriter();

		new TerminalImageRenderer(Caps(P.HalfBlocks)).RenderPng(png, writer).Should().BeTrue();

		writer.ToString().Should().Contain("▀");
	}
}
