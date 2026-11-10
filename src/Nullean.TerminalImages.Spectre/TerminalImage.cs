using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Encoders;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Nullean.TerminalImages.Spectre;

/// <summary>
/// An image as a Spectre.Console renderable. Terminals with Kitty, iTerm2 or Sixel get the real image; anything else gets
/// half blocks drawn with Spectre's own colours, so Spectre's colour-depth handling applies. Without ANSI, <see cref="AltText"/> is written.
/// </summary>
/// <remarks>
/// Graphics protocols draw over the cells they cover, and Spectre widgets that pad their lines with spaces (a <see cref="Panel"/>,
/// a <see cref="Table"/>) write those spaces over the image on every protocol except Kitty. Put images directly on the console, or
/// use Kitty, when they sit inside such a widget.
/// </remarks>
public sealed class TerminalImage : Renderable
{
	private static TerminalImageCapabilities? CachedDefaultCapabilities;

	private readonly RgbaImage _image;

	/// <summary>Creates a renderable for the image.</summary>
	/// <param name="image">The image.</param>
	/// <param name="capabilities">What the terminal can show. Null uses <see cref="DefaultCapabilities"/>.</param>
	public TerminalImage(RgbaImage image, TerminalImageCapabilities? capabilities = null)
	{
		ArgumentNullException.ThrowIfNull(image);
		_image = image;
		Capabilities = capabilities;
	}

	/// <summary>Creates a renderable from a PNG file's bytes.</summary>
	public static TerminalImage FromPng(ReadOnlySpan<byte> png, TerminalImageCapabilities? capabilities = null) =>
		new(Png.PngDecoder.Decode(png), capabilities);

	/// <summary>
	/// What the terminal can show, when not set per image. Starts as an environment-only detection (no I/O); set it from
	/// <see cref="TerminalImageDetector.DetectAsync(CancellationToken)"/> to use a probed result.
	/// </summary>
	public static TerminalImageCapabilities DefaultCapabilities
	{
		get => CachedDefaultCapabilities ??= TerminalImageDetector.DetectFromEnvironment(TerminalEnvironment.Current, Console.IsOutputRedirected);
		set => CachedDefaultCapabilities = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>What the terminal can show for this image. Null uses <see cref="DefaultCapabilities"/>.</summary>
	public TerminalImageCapabilities? Capabilities { get; set; }

	/// <summary>The widest the image may be, in cells. Defaults to the width Spectre offers.</summary>
	public int? MaxWidth { get; set; }

	/// <summary>The tallest the image may be, in cells. Defaults to the console height, or 24 when that is not known.</summary>
	public int? MaxHeight { get; set; }

	/// <summary>Whether a small image may be enlarged to fill the space.</summary>
	public bool AllowUpscale { get; set; }

	/// <summary>How to resample.</summary>
	public ResampleMode Resample { get; set; }

	/// <summary>Written instead of the image when the console has no ANSI support.</summary>
	public string? AltText { get; set; }

	/// <inheritdoc />
	protected override Measurement Measure(RenderOptions options, int maxWidth)
	{
		var layout = CreateRenderer(options, maxWidth, out _).Layout(_image);
		return new Measurement(layout.Cells.Columns, layout.Cells.Columns);
	}

	/// <inheritdoc />
	protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
	{
		if (!options.Ansi)
		{
			if (!string.IsNullOrEmpty(AltText))
			{
				yield return new Segment(AltText);
				yield return Segment.LineBreak;
			}

			yield break;
		}

		var renderer = CreateRenderer(options, maxWidth, out var capabilities);
		if (renderer.Protocol == TerminalImageProtocol.None)
		{
			if (!string.IsNullOrEmpty(AltText))
			{
				yield return new Segment(AltText);
				yield return Segment.LineBreak;
			}

			yield break;
		}

		var layout = renderer.Layout(_image);
		var scaled = Resampler.Resize(_image, layout.PixelWidth, layout.PixelHeight, Resample);

		if (renderer.Protocol == TerminalImageProtocol.HalfBlocks)
		{
			foreach (var segment in HalfBlocks(scaled))
				yield return segment;
			yield break;
		}

		// One zero-width control segment draws the image from the cursor and puts the cursor back; the line breaks that follow
		// are what reserve the rows, so Spectre's layout and Live displays count them. Nothing is written over the image.
		// Like Spectre's own widgets, every line ends with a line break, so the next write starts below the image.
		var body = new StringWriter();
		CreateEncoder(renderer.Protocol, capabilities).Encode(scaled, layout.Cells, body);
		var sequence = body.ToString();
		if (capabilities.IsMultiplexer)
			sequence = TmuxPassthrough.Wrap(sequence);

		yield return Segment.Control("\e7" + sequence + "\e8");
		for (var row = 0; row < layout.Cells.Rows; row++)
			yield return Segment.LineBreak;
	}

	private TerminalImageRenderer CreateRenderer(RenderOptions options, int maxWidth, out TerminalImageCapabilities capabilities)
	{
		capabilities = Capabilities ?? DefaultCapabilities;
		var height = MaxHeight ?? options.Height ?? options.ConsoleSize.Height;
		return new TerminalImageRenderer(capabilities, new TerminalImageRenderOptions
		{
			MaxColumns = Math.Max(1, Math.Min(MaxWidth ?? maxWidth, maxWidth)),
			MaxRows = Math.Max(1, height > 0 ? height : 24),
			AllowUpscale = AllowUpscale,
			Resample = Resample,
		});
	}

	private static ITerminalImageEncoder CreateEncoder(TerminalImageProtocol protocol, TerminalImageCapabilities capabilities) => protocol switch
	{
		TerminalImageProtocol.Kitty => new KittyEncoder(),
		TerminalImageProtocol.ITerm2 => new ITerm2Encoder(),
		_ => new SixelEncoder { MaxColors = Math.Clamp(capabilities.SixelColorRegisters ?? 256, 2, 256) },
	};

	/// <summary>Two pixels per cell, as Spectre colours: Spectre then downgrades them to what the console supports.</summary>
	private static IEnumerable<Segment> HalfBlocks(RgbaImage image)
	{
		var pixels = image.Pixels;
		for (var y = 0; y < image.Height; y += 2)
		{
			for (var x = 0; x < image.Width; x++)
			{
				var top = Pixel(pixels.Span, image.Width, x, y);
				var bottom = y + 1 < image.Height ? Pixel(pixels.Span, image.Width, x, y + 1) : null;
				yield return (top, bottom) switch
				{
					({ } t, { } b) => new Segment("▀", new Style(t, b)),
					({ } t, null) => new Segment("▀", new Style(t)),
					(null, { } b) => new Segment("▄", new Style(b)),
					_ => new Segment(" "),
				};
			}

			yield return Segment.LineBreak;
		}
	}

	private static Color? Pixel(ReadOnlySpan<byte> pixels, int stride, int x, int y)
	{
		var at = ((y * stride) + x) * 4;
		return pixels[at + 3] < 128 ? null : new Color(pixels[at], pixels[at + 1], pixels[at + 2]);
	}
}
