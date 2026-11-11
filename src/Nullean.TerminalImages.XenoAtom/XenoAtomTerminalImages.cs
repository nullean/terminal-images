using Nullean.TerminalImages.Detection;
using XenoAtom.Terminal;

namespace Nullean.TerminalImages.XenoAtom;

/// <summary>Writes images to a <see cref="TerminalInstance"/>.</summary>
/// <remarks>
/// This is independent of <c>XenoAtom.Terminal.Graphics</c>: it adds no SkiaSharp and needs only the core
/// <c>XenoAtom.Terminal</c> package. XenoAtom does not hand terminal replies to the application, so detection here asks the
/// terminal itself only when you say so (<c>probe: true</c>), which reads <c>/dev/tty</c> directly and must happen before
/// XenoAtom's input loop starts (before the first key read, or <see cref="TerminalInstance.StartInput"/>).
/// </remarks>
public static class XenoAtomTerminalImages
{
	/// <summary>Detects which image protocol the terminal speaks.</summary>
	/// <param name="terminal">The terminal.</param>
	/// <param name="probe">Whether to ask the terminal (macOS and Linux only). Do this before the input loop starts.</param>
	/// <param name="cancellationToken">Cancels the probe.</param>
	public static async ValueTask<TerminalImageCapabilities> DetectImagesAsync(
		this TerminalInstance terminal,
		bool probe = false,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(terminal);
		var capabilities = await TerminalImageDetector.DetectAsync(
			new TerminalImageDetectionOptions
			{
				IsOutputRedirected = terminal.Capabilities.IsOutputRedirected || !terminal.Capabilities.AnsiEnabled,
				Channel = probe ? null : TerminalImageDetectionOptions.NoProbing,
			},
			cancellationToken
		).ConfigureAwait(false);

		// XenoAtom knows the colour level better than COLORTERM does, so it decides how half blocks are coloured.
		return capabilities.Confidence == DetectionConfidence.Forced
			? capabilities
			: capabilities with { TrueColor = terminal.Capabilities.ColorLevel == TerminalColorLevel.TrueColor };
	}

	/// <summary>Draws the image, scaled to fit the terminal.</summary>
	/// <param name="terminal">The terminal.</param>
	/// <param name="image">The image.</param>
	/// <param name="capabilities">What the terminal can show, from <see cref="DetectImagesAsync"/>. Null detects from the environment.</param>
	/// <param name="options">Size limits and other settings. Null fits the image to the terminal window.</param>
	/// <returns>False when nothing was drawn because the terminal cannot show images.</returns>
	public static bool WriteImage(
		this TerminalInstance terminal,
		RgbaImage image,
		TerminalImageCapabilities? capabilities = null,
		TerminalImageRenderOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(terminal);
		ArgumentNullException.ThrowIfNull(image);

		capabilities ??= terminal.DetectImagesAsync().AsTask().GetAwaiter().GetResult(); // environment only: completes synchronously
		var size = terminal.Size;
		options ??= new TerminalImageRenderOptions { MaxColumns = Math.Max(1, size.Columns), MaxRows = Math.Max(1, size.Rows - 1), };

		var text = new StringWriter();
		if (!new TerminalImageRenderer(capabilities, options).Render(image, text))
			return false;

		var rendered = text.ToString();
		terminal.WriteAtomic((TextWriter writer) => writer.Write(rendered)); // atomic: no other output lands in the middle of the image
		return true;
	}

	/// <summary>Decodes a PNG file and draws it.</summary>
	/// <inheritdoc cref="WriteImage(TerminalInstance, RgbaImage, TerminalImageCapabilities?, TerminalImageRenderOptions?)"/>
	public static bool WritePng(
		this TerminalInstance terminal,
		ReadOnlySpan<byte> png,
		TerminalImageCapabilities? capabilities = null,
		TerminalImageRenderOptions? options = null
	) => terminal.WriteImage(Png.PngDecoder.Decode(png), capabilities, options);
}
