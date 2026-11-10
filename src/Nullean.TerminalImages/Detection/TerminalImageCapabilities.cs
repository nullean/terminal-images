namespace Nullean.TerminalImages.Detection;

/// <summary>How sure the detection is.</summary>
public enum DetectionConfidence
{
	/// <summary>Nothing is known: the answer is a safe default.</summary>
	Default,

	/// <summary>Inferred from environment variables, which can be stale, lost over SSH or hidden by a multiplexer.</summary>
	Environment,

	/// <summary>Confirmed by asking the terminal.</summary>
	Probed,

	/// <summary>Chosen by the caller or by <c>NULLEAN_TERMINAL_IMAGES</c>: not detected at all.</summary>
	Forced,
}

/// <summary>The size of one terminal cell in pixels.</summary>
public readonly record struct CellPixelSize(int Width, int Height)
{
	/// <summary>Whether both dimensions are positive.</summary>
	public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>What the terminal can show, and how that was worked out.</summary>
public sealed record TerminalImageCapabilities
{
	/// <summary>The protocol to use: the first of <see cref="Supported"/>.</summary>
	public TerminalImageProtocol Preferred => Supported.Count > 0 ? Supported[0] : TerminalImageProtocol.None;

	/// <summary>Every protocol that should work, best first. Half blocks come last when they are possible.</summary>
	public IReadOnlyList<TerminalImageProtocol> Supported { get; init; } = [];

	/// <summary>The terminal's name as detected, lower case (<c>kitty</c>, <c>wezterm</c>, <c>iterm2</c>...), or null.</summary>
	public string? Brand { get; init; }

	/// <summary>The terminal's version, when it told us.</summary>
	public string? BrandVersion { get; init; }

	/// <summary>The cell size in pixels, from an override or a probe. Needed to size images exactly.</summary>
	public CellPixelSize? CellSize { get; init; }

	/// <summary>How many colour registers the terminal has for Sixel, when asked.</summary>
	public int? SixelColorRegisters { get; init; }

	/// <summary>Whether 24-bit colour is available. Half blocks fall back to 256 colours without it.</summary>
	public bool TrueColor { get; init; }

	/// <summary>Whether tmux or screen sits between the application and the terminal.</summary>
	public bool IsMultiplexer { get; init; }

	/// <summary>Whether this is an SSH session.</summary>
	public bool IsRemote { get; init; }

	/// <summary>How sure the detection is.</summary>
	public DetectionConfidence Confidence { get; init; }

	/// <summary>Why the result is what it is, one line per decision.</summary>
	public IReadOnlyList<string> Diagnostics { get; init; } = [];

	/// <summary>Whether <paramref name="protocol"/> is in <see cref="Supported"/>.</summary>
	public bool Supports(TerminalImageProtocol protocol) => Supported.Contains(protocol);

	/// <summary>A result that draws nothing.</summary>
	public static TerminalImageCapabilities None(string reason) => new() { Diagnostics = [reason] };
}
