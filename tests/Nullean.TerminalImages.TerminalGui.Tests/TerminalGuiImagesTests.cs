using AwesomeAssertions;
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.TerminalGui;
using Terminal.Gui.Drivers;
using P = Nullean.TerminalImages.TerminalImageProtocol;

namespace Nullean.TerminalImages.TerminalGui.Tests;

public class TerminalGuiImagesTests
{
	private static TerminalImageCapabilities Caps(CellPixelSize? cell = null, int? registers = null, params P[] supported) =>
		new() { Supported = supported, CellSize = cell, SixelColorRegisters = registers };

	[Test]
	public void Sixel_support_carries_the_detected_resolution_and_palette_size()
	{
		var result = TerminalGuiImages.ToSixelSupport(Caps(new CellPixelSize(9, 18), 1024, P.Sixel, P.HalfBlocks));

		result.IsSupported.Should().BeTrue();
		result.Resolution.Should().Be(new System.Drawing.Size(9, 18));
		result.MaxPaletteColors.Should().Be(256); // clamped: Terminal.Gui's encoder takes at most 256
		result.SupportsTransparency.Should().BeTrue();
	}

	[Test]
	public void Sixel_is_not_reported_for_a_terminal_without_it_and_the_default_cell_size_is_10_by_20()
	{
		var result = TerminalGuiImages.ToSixelSupport(Caps(supported: P.Kitty));

		result.IsSupported.Should().BeFalse();
		result.SupportsTransparency.Should().BeFalse();
		result.Resolution.Should().Be(new System.Drawing.Size(10, 20));
	}

	[Test]
	public void Kitty_support_follows_the_detection()
	{
		TerminalGuiImages.ToKittySupport(Caps(new CellPixelSize(8, 16), null, P.Kitty)).Should().Match<Terminal.Gui.Drawing.KittyGraphicsSupportResult>(
			r => r.IsSupported && r.Resolution == new System.Drawing.Size(8, 16));
		TerminalGuiImages.ToKittySupport(Caps(supported: P.Sixel)).IsSupported.Should().BeFalse();
	}

	[Test]
	public void An_invalid_cell_size_falls_back_to_the_default()
	{
		TerminalGuiImages.ToKittySupport(Caps(new CellPixelSize(0, 0), null, P.Kitty)).Resolution.Should().Be(new System.Drawing.Size(10, 20));
	}
}

public class TerminalImageViewTests
{
	private static TerminalImageCapabilities Caps(params P[] supported) =>
		new() { Supported = supported, CellSize = new CellPixelSize(10, 20) };

	/// <summary>Terminal.Gui consumes raster commands when it flushes a frame, so the command is captured as the view adds it.</summary>
	internal static CapturingView DrawView(P[] supported, RgbaImage image, int width = 20, int height = 5) => Draw(supported, image, width, height);

	private static CapturingView Draw(P[] supported, RgbaImage image, int width = 20, int height = 5)
	{
		using var app = Terminal.Gui.App.Application.Create();
		app.Init(DriverRegistry.Names.ANSI);
		var driver = app.Driver!;
		driver.SetScreenSize(80, 25); // a CI runner can have no console, so the screen would be 0x0 and nothing would draw
		driver.ApplyDetection(Caps(supported));
		var view = new CapturingView { Width = width, Height = height, Image = image };
		var window = new Terminal.Gui.Views.Window { Width = Terminal.Gui.ViewBase.Dim.Fill(), Height = Terminal.Gui.ViewBase.Dim.Fill() };
		window.Add(view);

		var token = app.Begin(window)!;
		app.LayoutAndDraw(true);
		app.End(token);
		return view;
	}

	[Test]
	[Timeout(60_000)]
	public void The_view_hands_a_scaled_image_to_terminal_guis_raster_pipeline_when_sixel_is_supported(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var view = Draw([P.Sixel, P.HalfBlocks], RgbaImage.Solid(400, 400, 1, 2, 3));
		var captured = view.Captured;

		captured.Should().NotBeNull(view.Seen);
		var command = captured!;
		var pixels = command.Pixels!;
		// 20x5 cells at 10x20 px is 200x100 px: the square image fits at 100x100, which is 10 columns by 5 rows.
		pixels.GetLength(0).Should().Be(100); // [x, y]
		pixels.GetLength(1).Should().Be(100);
		command.DestinationCells.Width.Should().Be(10);
		command.DestinationCells.Height.Should().Be(5);
		pixels[0, 0].R.Should().Be(1);
	}

	[Test]
	[Timeout(60_000)]
	public void Pixels_are_indexed_x_first_and_keep_their_place(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		// 400x200: the left half red, the right half blue, so a swapped index or a mirrored copy shows up.
		var pixels = new byte[400 * 200 * 4];
		for (var y = 0; y < 200; y++)
		{
			for (var x = 0; x < 400; x++)
			{
				var at = ((y * 400) + x) * 4;
				pixels[at] = (byte)(x < 200 ? 255 : 0);
				pixels[at + 2] = (byte)(x < 200 ? 0 : 255);
				pixels[at + 3] = 255;
			}
		}

		var view = Draw([P.Sixel], new RgbaImage(400, 200, pixels), width: 40, height: 10);
		view.Captured.Should().NotBeNull(view.Seen);
		var command = view.Captured!;
		var drawn = command.Pixels!;

		// 40x10 cells at 10x20 px is 400x200 px: the image keeps its size.
		drawn.GetLength(0).Should().Be(400);
		drawn.GetLength(1).Should().Be(200);
		drawn[0, 0].R.Should().Be(255);
		drawn[399, 0].B.Should().Be(255);
		drawn[399, 199].B.Should().Be(255);
		drawn[0, 199].R.Should().Be(255);
	}

	[Test]
	[Timeout(60_000)]
	public void Kitty_also_goes_through_the_raster_pipeline(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var view = Draw([P.Kitty, P.HalfBlocks], RgbaImage.Solid(400, 400, 1, 2, 3));
		view.Captured.Should().NotBeNull(view.Seen);
	}

	[Test]
	[Timeout(60_000)]
	public void Without_sixel_or_kitty_nothing_is_added_to_the_raster_pipeline(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var view = Draw([P.HalfBlocks], RgbaImage.Solid(400, 400, 1, 2, 3));
		view.Captured.Should().BeNull(view.Seen);
	}
}

public class TerminalImageViewHalfBlockTests
{
	[Test]
	[Timeout(60_000)]
	public void Without_graphics_the_view_draws_half_blocks_in_its_cells(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		// Two pixels tall, so exactly one cell: red on top, blue below. The cell is an upper half block, red in front, blue behind.
		var pixels = new byte[4 * 2 * 4];
		for (var y = 0; y < 2; y++)
		{
			for (var x = 0; x < 4; x++)
			{
				var at = ((y * 4) + x) * 4;
				pixels[at] = (byte)(y == 0 ? 255 : 0);
				pixels[at + 2] = (byte)(y == 0 ? 0 : 255);
				pixels[at + 3] = 255;
			}
		}

		var view = TerminalImageViewTests.DrawView([P.HalfBlocks], new RgbaImage(4, 2, pixels), width: 4, height: 1);

		view.FirstCell.Should().NotBeNull(view.Seen);
		view.FirstCell!.Value.Grapheme.Should().Be("▀");
		view.FirstCell.Value.Foreground!.Value.R.Should().Be(255);
		view.FirstCell.Value.Background!.Value.B.Should().Be(255);
	}
}

internal sealed class CapturingView : TerminalImageView
{
	public RasterImageCommand? Captured { get; private set; }

	/// <summary>What the view saw when it was drawn, for the assertion messages.</summary>
	public string Seen { get; private set; } = "never drawn";

	public (string? Grapheme, Terminal.Gui.Drawing.Color? Foreground, Terminal.Gui.Drawing.Color? Background)? FirstCell { get; private set; }

	protected override bool OnDrawingContent(Terminal.Gui.ViewBase.DrawContext? context)
	{
		var result = base.OnDrawingContent(context);
		Seen = $"drawn with viewport {Viewport}, driver {(App?.Driver is null ? "missing" : "present")}, sixel {App?.Driver?.SixelSupport?.IsSupported}, kitty {App?.Driver?.KittyGraphicsSupport?.IsSupported}";
		var images = App?.Driver?.GetOutputBuffer().GetRasterImages();
		Captured = images is { Count: > 0 } ? images[0] : null;
		var origin = ViewportToScreen(System.Drawing.Point.Empty);
		if (App?.Driver?.Contents is { } contents)
		{
			var cell = contents[origin.Y, origin.X];
			FirstCell = (cell.Grapheme, cell.Attribute?.Foreground, cell.Attribute?.Background);
		}

		return result;
	}
}
