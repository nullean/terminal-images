using System.Drawing;
using System.Runtime.CompilerServices;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace Nullean.TerminalImages.TerminalGui;

/// <summary>
/// A view that shows an image. Where the driver supports Sixel or Kitty, the image goes through Terminal.Gui's own raster
/// pipeline (which clips it to the view, orders it with text, and repaints it on resize). Elsewhere it is drawn as half blocks
/// in the view's cells. The iTerm2 protocol is not part of Terminal.Gui's pipeline, so on an iTerm2-only terminal this falls back to half blocks.
/// </summary>
/// <remarks>
/// Use <see cref="TerminalGuiImages.ApplyDetection"/> first when the terminal is behind tmux or SSH, or when probing finds
/// more than Terminal.Gui's start-up handshake does.
/// </remarks>
public class TerminalImageView : View
{
	private RgbaImage? _image;
	private ResampleMode _resample;
	private bool _allowUpscale;

	/// <summary>The image to show. Null shows nothing.</summary>
	public RgbaImage? Image
	{
		get => _image;
		set
		{
			_image = value;
			SetNeedsDraw();
		}
	}

	/// <summary>How the image is resampled to fit the view.</summary>
	public ResampleMode Resample
	{
		get => _resample;
		set
		{
			_resample = value;
			SetNeedsDraw();
		}
	}

	/// <summary>Whether a small image is enlarged to fill the view.</summary>
	public bool AllowUpscale
	{
		get => _allowUpscale;
		set
		{
			_allowUpscale = value;
			SetNeedsDraw();
		}
	}

	/// <inheritdoc />
	protected override bool OnDrawingContent(DrawContext? context)
	{
		var image = _image;
		var driver = App?.Driver;
		var columns = Viewport.Width;
		var rows = Viewport.Height;
		if (image is null || driver is null || columns <= 0 || rows <= 0)
			return true;

		// Either result can be missing before the driver has finished its start-up handshake.
		var kitty = driver.KittyGraphicsSupport;
		var sixel = driver.SixelSupport;
		if (kitty?.IsSupported == true)
			DrawRaster(driver, image, columns, rows, kitty.Resolution);
		else if (sixel?.IsSupported == true)
			DrawRaster(driver, image, columns, rows, sixel.Resolution);
		else
			DrawHalfBlocks(image, columns, rows);
		return true;
	}

	private void DrawRaster(IDriver driver, RgbaImage image, int columns, int rows, Size cell)
	{
		var cellWidth = Math.Max(1, cell.Width);
		var cellHeight = Math.Max(1, cell.Height);
		var (width, height) = Resampler.FitWithin(image.Width, image.Height, columns * cellWidth, rows * cellHeight, _allowUpscale);
		var scaled = Resampler.Resize(image, width, height, _resample);

		var pixels = new Color[width, height]; // [x, y], as Terminal.Gui expects
		var source = scaled.Pixels.Span;
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var at = ((y * width) + x) * 4;
				pixels[x, y] = new Color(source[at], source[at + 1], source[at + 2], source[at + 3]);
			}
		}

		var destination = ViewportToScreen(
			new Rectangle(0, 0, Math.Clamp((width + cellWidth - 1) / cellWidth, 1, columns), Math.Clamp((height + cellHeight - 1) / cellHeight, 1, rows))
		);
		driver.GetOutputBuffer().AddRasterImage(new RasterImageCommand
		{
			Id = $"nullean-terminal-image-{RuntimeHelpers.GetHashCode(this)}",
			Pixels = pixels,
			DestinationCells = destination,
		});
	}

	private void DrawHalfBlocks(RgbaImage image, int columns, int rows)
	{
		var (width, height) = Resampler.FitWithin(image.Width, image.Height, columns, rows * 2, _allowUpscale);
		var background = GetAttributeForRole(VisualRole.Normal).Background;
		var scaled = Resampler.Resize(image, width, height, _resample).CompositeOver(background.R, background.G, background.B);
		var pixels = scaled.Pixels.Span;

		for (var y = 0; y < height; y += 2)
		{
			Move(0, y / 2);
			for (var x = 0; x < width; x++)
			{
				var top = ColorAt(pixels, width, x, y);
				var bottom = y + 1 < height ? ColorAt(pixels, width, x, y + 1) : background;
				SetAttribute(new Attribute(top, bottom));
				AddStr("▀");
			}
		}
	}

	private static Color ColorAt(ReadOnlySpan<byte> pixels, int stride, int x, int y)
	{
		var at = ((y * stride) + x) * 4;
		return new Color(pixels[at], pixels[at + 1], pixels[at + 2]);
	}
}
