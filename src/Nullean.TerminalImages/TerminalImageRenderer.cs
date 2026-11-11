using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Encoders;
using Nullean.TerminalImages.Png;

namespace Nullean.TerminalImages;

/// <summary>Where an image lands: its pixel size after scaling and the cells it covers.</summary>
public readonly record struct TerminalImageLayout(int PixelWidth, int PixelHeight, CellSize Cells);

/// <summary>Settings for <see cref="TerminalImageRenderer"/>.</summary>
public sealed class TerminalImageRenderOptions
{
	/// <summary>The widest the image may be, in cells. Defaults to 80.</summary>
	public int MaxColumns { get; init; } = 80;

	/// <summary>The tallest the image may be, in cells. Defaults to 24.</summary>
	public int MaxRows { get; init; } = 24;

	/// <summary>Whether a small image may be enlarged to fill the box. Off by default.</summary>
	public bool AllowUpscale { get; init; }

	/// <summary>How to resample. <see cref="ResampleMode.Nearest"/> suits pixel art.</summary>
	public ResampleMode Resample { get; init; }

	/// <summary>The cell size in pixels to assume when the terminal's is unknown. Defaults to 10x20, close to a typical terminal font.</summary>
	public CellPixelSize FallbackCellSize { get; init; } = new(10, 20);

	/// <summary>Whether to wrap the image in tmux's DCS passthrough when a multiplexer was detected. On by default.</summary>
	public bool TmuxPassthrough { get; init; } = true;

	/// <summary>The Sixel settings (palette, dithering, colour limit). The colour limit defaults to the terminal's register count when known.</summary>
	public SixelEncoder? Sixel { get; init; }

	/// <summary>Whether Kitty images are deflated on the wire.</summary>
	public bool CompressKitty { get; init; }
}

/// <summary>
/// Draws images with the best protocol the terminal has: scales them to fit, chooses the encoder, wraps the output for tmux
/// when needed, and leaves the cursor below the image.
/// </summary>
public sealed class TerminalImageRenderer
{
	private readonly TerminalImageCapabilities _capabilities;
	private readonly TerminalImageRenderOptions _options;

	/// <summary>Creates a renderer for the detected capabilities.</summary>
	public TerminalImageRenderer(TerminalImageCapabilities capabilities, TerminalImageRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(capabilities);
		_capabilities = capabilities;
		_options = options ?? new TerminalImageRenderOptions();
	}

	/// <summary>The protocol that will be used.</summary>
	public TerminalImageProtocol Protocol => _capabilities.Preferred;

	/// <summary>Works out the size an image will have, without drawing it.</summary>
	public TerminalImageLayout Layout(RgbaImage image)
	{
		ArgumentNullException.ThrowIfNull(image);
		var cell = Protocol == TerminalImageProtocol.HalfBlocks
			? new CellPixelSize(1, 2) // two pixels per cell, one above the other
			: _capabilities.CellSize is { IsValid: true } known ? known : _options.FallbackCellSize;

		var maxColumns = Math.Max(1, _options.MaxColumns);
		var maxRows = Math.Max(1, _options.MaxRows);
		var (width, height) = Resampler.FitWithin(image.Width, image.Height, maxColumns * cell.Width, maxRows * cell.Height, _options.AllowUpscale);
		var columns = Math.Clamp((width + cell.Width - 1) / cell.Width, 1, maxColumns);
		var rows = Math.Clamp((height + cell.Height - 1) / cell.Height, 1, maxRows);
		return new TerminalImageLayout(width, height, new CellSize(columns, rows));
	}

	/// <summary>Decodes a PNG file and draws it.</summary>
	/// <returns>False when nothing was drawn because the terminal has no way to show images.</returns>
	public bool RenderPng(ReadOnlySpan<byte> png, TextWriter output) => Render(PngDecoder.Decode(png), output);

	/// <summary>Draws the image.</summary>
	/// <returns>False when nothing was drawn because the terminal has no way to show images.</returns>
	public bool Render(RgbaImage image, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentNullException.ThrowIfNull(output);
		if (Protocol == TerminalImageProtocol.None)
			return false;

		var layout = Layout(image);
		var scaled = Resampler.Resize(image, layout.PixelWidth, layout.PixelHeight, _options.Resample);

		if (Protocol == TerminalImageProtocol.HalfBlocks)
		{
			// Plain text: no cursor handling, and never wrapped for tmux.
			new HalfBlockEncoder { ColorDepth = _capabilities.TrueColor ? HalfBlockColorDepth.TrueColor : HalfBlockColorDepth.Palette256 }.Encode(
				scaled,
				layout.Cells,
				output
			);
			return true;
		}

		var body = new StringWriter();
		CreateEncoder().Encode(scaled, layout.Cells, body);
		var sequence = body.ToString();
		if (_capabilities.IsMultiplexer && _options.TmuxPassthrough)
			sequence = Nullean.TerminalImages.TmuxPassthrough.Wrap(sequence);

		// Make room first (the newlines scroll the screen if the image would run off the bottom), go back up, and remember the
		// spot. Terminals disagree about where the cursor ends up after an image, so restore it and then step below the image.
		var rows = layout.Cells.Rows;
		output.Write(new string('\n', rows));
		output.Write($"\e[{rows}A\e7");
		output.Write(sequence);
		output.Write($"\e8\e[{rows}B\r");
		return true;
	}

	private ITerminalImageEncoder CreateEncoder() => Protocol switch
	{
		TerminalImageProtocol.Kitty => new KittyEncoder { Compress = _options.CompressKitty },
		TerminalImageProtocol.ITerm2 => new ITerm2Encoder(),
		_ => _options.Sixel ?? new SixelEncoder { MaxColors = Math.Clamp(_capabilities.SixelColorRegisters ?? 256, 2, 256) },
	};
}
