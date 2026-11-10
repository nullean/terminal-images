using System.Globalization;

namespace Nullean.TerminalImages.Detection;

/// <summary>Settings for <see cref="TerminalImageDetector"/>.</summary>
public sealed class TerminalImageDetectionOptions
{
	/// <summary>Skip detection and use this protocol. The <c>NULLEAN_TERMINAL_IMAGES</c> variable does the same without code.</summary>
	public TerminalImageProtocol? Protocol { get; init; }

	/// <summary>Whether standard output goes to a file or a pipe. Nothing is drawn then. Defaults to <see cref="Console.IsOutputRedirected"/>.</summary>
	public bool? IsOutputRedirected { get; init; }

	/// <summary>The environment to read. Defaults to the process environment.</summary>
	public TerminalEnvironment? Environment { get; init; }

	/// <summary>
	/// How the terminal is asked. Null uses <see cref="StandaloneQueryChannel"/> when the process has a terminal. Pass
	/// <see cref="NoProbing"/> to detect from the environment only.
	/// </summary>
	public ITerminalQueryChannel? Channel { get; init; }

	/// <summary>The longest to wait for a probe reply. Null picks 250 ms, or 1.5 s over SSH where the round trip is slower.</summary>
	public TimeSpan? ProbeTimeout { get; init; }

	/// <summary>A channel that is never asked: detection stays environment-only.</summary>
	public static ITerminalQueryChannel NoProbing { get; } = new NoProbingChannel();

	private sealed class NoProbingChannel : ITerminalQueryChannel
	{
		public ValueTask<string> QueryAsync(string request, Func<string, bool> isComplete, TimeSpan timeout, CancellationToken cancellationToken) => ValueTask.FromResult(string.Empty);
	}
}

/// <summary>
/// Works out which image protocol the terminal speaks, cheapest evidence first: an explicit override, then the environment,
/// then one batched query to the terminal that always ends with a Device Attributes request, which every terminal answers,
/// so the wait ends as soon as the terminal has said all it will say.
/// </summary>
public static class TerminalImageDetector
{
	private static readonly TerminalImageProtocol[] DefaultOrder = [TerminalImageProtocol.Kitty, TerminalImageProtocol.ITerm2, TerminalImageProtocol.Sixel];

	/// <summary>Detects from the environment only: no I/O, no waiting.</summary>
	public static TerminalImageCapabilities DetectFromEnvironment(TerminalEnvironment environment, bool isOutputRedirected, TerminalImageProtocol? forced = null)
	{
		ArgumentNullException.ThrowIfNull(environment);
		return FromEnvironment(environment, isOutputRedirected, forced).Capabilities;
	}

	/// <summary>Detects with the process environment and, when there is a terminal to ask, a probe.</summary>
	public static ValueTask<TerminalImageCapabilities> DetectAsync(CancellationToken cancellationToken = default) =>
		DetectAsync(new TerminalImageDetectionOptions(), cancellationToken);

	/// <summary>Detects: override, gates, environment, then a probe when it can add something.</summary>
	public static async ValueTask<TerminalImageCapabilities> DetectAsync(TerminalImageDetectionOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		var environment = options.Environment ?? TerminalEnvironment.Current;
		var redirected = options.IsOutputRedirected ?? Console.IsOutputRedirected;
		var (capabilities, final) = FromEnvironment(environment, redirected, options.Protocol);
		if (final)
			return capabilities;

		var channel = options.Channel;
		var opened = channel is null ? StandaloneQueryChannel.TryOpen() : null; // closed here: only a channel we opened ourselves
		try
		{
			channel ??= opened;
			if (channel is null || ReferenceEquals(channel, TerminalImageDetectionOptions.NoProbing))
				return capabilities with { Diagnostics = [.. capabilities.Diagnostics, "No terminal to ask: using the environment only."] };

			var timeout = options.ProbeTimeout ?? (capabilities.IsRemote ? TimeSpan.FromMilliseconds(1500) : TimeSpan.FromMilliseconds(250));
			return await Probe(capabilities, channel, timeout, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			opened?.Dispose();
		}
	}

	/// <summary>The query that is sent, exposed for tests and diagnostics. DA1 goes last: it is the sentinel.</summary>
	public static string BuildProbeRequest(bool includeKitty)
	{
		var kitty = includeKitty ? $"\e_Gi={ProbeReplyParser.KittyQueryId.ToString(CultureInfo.InvariantCulture)},s=1,v=1,a=q,t=d,f=24;AAAA\e\\" : string.Empty;
		return kitty + "\e[>0q" + "\e[16t" + "\e[c";
	}

	private const string SixelRegistersRequest = "\e[?1;1;0S\e[c";

	private static async ValueTask<TerminalImageCapabilities> Probe(TerminalImageCapabilities fromEnvironment, ITerminalQueryChannel channel, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var diagnostics = new List<string>(fromEnvironment.Diagnostics);
		var includeKitty = !OperatingSystem.IsWindows() && !TerminalBrands.SkipsKittyProbe(fromEnvironment.Brand);

		// In a multiplexer the whole batch, DA1 included, goes to the outer terminal: the multiplexer would answer DA1 itself and
		// end the wait before the real terminal replied.
		var request = BuildProbeRequest(includeKitty);
		if (fromEnvironment.IsMultiplexer)
			request = TmuxPassthrough.Wrap(request);

		string text;
		try
		{
			text = await channel.QueryAsync(request, ProbeReplyParser.IsComplete, timeout, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
		{
			diagnostics.Add($"The probe failed ({exception.GetType().Name}): using the environment only.");
			return fromEnvironment with { Diagnostics = diagnostics };
		}

		var replies = ProbeReplyParser.Parse(text);
		if (!replies.HasDeviceAttributes)
		{
			diagnostics.Add(text.Length == 0
				? "The terminal did not answer the probe: using the environment only."
				: "The probe reply was incomplete: using the environment only.");
			return fromEnvironment with { Diagnostics = diagnostics };
		}

		var brand = fromEnvironment.Brand;
		var version = fromEnvironment.BrandVersion;
		if (replies.TerminalName is { } name && TerminalBrands.FromVersionName(name) is { } probedBrand)
		{
			if (brand is not null && brand != probedBrand)
				diagnostics.Add($"The terminal says it is {probedBrand}, the environment said {brand}: believing the terminal.");
			brand = probedBrand;
			version = replies.TerminalVersionNumber;
		}
		else if (replies.TerminalName is { } unknown)
		{
			diagnostics.Add($"The terminal calls itself {unknown}, which is not in the brand table.");
		}

		var registers = replies.SixelColorRegisters;
		if (replies.SupportsSixel && registers is null)
		{
			try
			{
				var request2 = fromEnvironment.IsMultiplexer ? TmuxPassthrough.Wrap(SixelRegistersRequest) : SixelRegistersRequest;
				var more = ProbeReplyParser.Parse(await channel.QueryAsync(request2, ProbeReplyParser.IsComplete, timeout, cancellationToken).ConfigureAwait(false));
				registers = more.SixelColorRegisters;
			}
			catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
			{
				diagnostics.Add($"The Sixel colour register query failed ({exception.GetType().Name}).");
			}
		}

		// What is confirmed: the brand's own list, plus anything the terminal proved by answering.
		var confirmed = new List<TerminalImageProtocol>();
		if (TerminalBrands.TryGetPreferences(brand, out var preferred))
			confirmed.AddRange(preferred);
		if (replies.KittyGraphics == true)
			Add(confirmed, TerminalImageProtocol.Kitty);
		if (replies.SupportsSixel)
			Add(confirmed, TerminalImageProtocol.Sixel);

		// A terminal that answered everything except the Kitty query, directly (not through a multiplexer), does not speak it:
		// that is how Kitty's own documentation says to read the DA1 sentinel. This catches a stale KITTY_WINDOW_ID.
		if (includeKitty && replies.KittyGraphics is null && !fromEnvironment.IsMultiplexer && confirmed.Remove(TerminalImageProtocol.Kitty))
			diagnostics.Add("The terminal answered the probe but not the Kitty query: Kitty graphics are not available.");
		if (replies.KittyGraphics == false)
		{
			confirmed.Remove(TerminalImageProtocol.Kitty);
			diagnostics.Add("The terminal answered the Kitty query with an error: Kitty graphics are not available.");
		}

		// Brand quirks decide the order; protocols only the probe found come after the brand's own.
		var supported = new List<TerminalImageProtocol>();
		foreach (var protocol in confirmed)
			Add(supported, protocol);
		if (!TerminalBrands.TryGetPreferences(brand, out _))
			supported = [.. DefaultOrder.Where(supported.Contains)];

		if (fromEnvironment.Supports(TerminalImageProtocol.HalfBlocks) || supported.Count > 0)
			supported.Add(TerminalImageProtocol.HalfBlocks);

		diagnostics.Add($"Probe answered: preferred protocol {(supported.Count > 0 ? supported[0] : TerminalImageProtocol.None)}"
			+ (replies.SupportsSixel ? ", Sixel listed by the terminal" : string.Empty)
			+ (replies.CellSize is { } cell ? $", cell {cell.Width}x{cell.Height}px" : string.Empty)
			+ ".");

		return fromEnvironment with
		{
			Supported = supported,
			Brand = brand,
			BrandVersion = version,
			CellSize = replies.CellSize ?? fromEnvironment.CellSize,
			SixelColorRegisters = registers,
			Confidence = DetectionConfidence.Probed,
			Diagnostics = diagnostics,
		};
	}

	private static void Add(List<TerminalImageProtocol> list, TerminalImageProtocol protocol)
	{
		if (!list.Contains(protocol))
			list.Add(protocol);
	}

	/// <summary>The environment step. The second value is true when the answer is final and no probe should follow.</summary>
	private static (TerminalImageCapabilities Capabilities, bool Final) FromEnvironment(TerminalEnvironment environment, bool isOutputRedirected, TerminalImageProtocol? forced)
	{
		var diagnostics = new List<string>();
		var multiplexer = environment.Has("TMUX") || environment.Has("STY");
		var remote = environment.Has("SSH_TTY") || environment.Has("SSH_CONNECTION") || environment.Has("SSH_CLIENT");
		var cellSize = ParseCellSize(environment, diagnostics);

		// 1. Overrides.
		var overrideText = environment.Get("NULLEAN_TERMINAL_IMAGES");
		if (forced is null && overrideText is not null && !overrideText.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			forced = ParseProtocol(overrideText);
			if (forced is null)
				diagnostics.Add($"NULLEAN_TERMINAL_IMAGES={overrideText} is not one of kitty, iterm2, sixel, blocks, none or auto: ignored.");
			else
				diagnostics.Add($"Protocol forced by NULLEAN_TERMINAL_IMAGES={overrideText}.");
		}
		else if (forced is not null)
		{
			diagnostics.Add($"Protocol forced by the caller: {forced}.");
		}

		if (forced is { } protocol)
		{
			var supported = protocol == TerminalImageProtocol.None ? [] : new List<TerminalImageProtocol> { protocol };
			return (new TerminalImageCapabilities
			{
				Supported = supported,
				CellSize = cellSize,
				TrueColor = true,
				IsMultiplexer = multiplexer,
				IsRemote = remote,
				Confidence = DetectionConfidence.Forced,
				Diagnostics = diagnostics,
			}, true);
		}

		// 2. Gates.
		if (isOutputRedirected)
		{
			return (TerminalImageCapabilities.None("Standard output is redirected: nothing is drawn.") with { IsMultiplexer = multiplexer, IsRemote = remote }, true);
		}

		if (environment.Is("TERM", "dumb"))
		{
			diagnostics.Add("TERM=dumb: nothing is drawn.");
			return (new TerminalImageCapabilities { Diagnostics = diagnostics, IsMultiplexer = multiplexer, IsRemote = remote }, true);
		}

		// 3. The environment.
		var brand = BrandFromEnvironment(environment);
		var trueColor = environment.Is("COLORTERM", "truecolor") || environment.Is("COLORTERM", "24bit") || TerminalBrands.HasTrueColor(brand);
		var list = new List<TerminalImageProtocol>();
		if (brand is null)
		{
			diagnostics.Add("No terminal recognised from the environment.");
		}
		else if (TerminalBrands.TryGetPreferences(brand, out var preferences))
		{
			list.AddRange(preferences);
			diagnostics.Add($"Terminal looks like {brand}: {(preferences.Count == 0 ? "no image protocol" : string.Join(", ", preferences))}.");
		}

		if (multiplexer)
			diagnostics.Add("Inside tmux or screen: images need tmux 3.3+ with 'allow-passthrough on'; the environment may describe the wrong terminal.");
		if (remote)
			diagnostics.Add("SSH session: environment variables may not have been forwarded.");
		if (!trueColor)
			diagnostics.Add("No 24-bit colour announced (COLORTERM): half blocks use 256 colours.");

		list.Add(TerminalImageProtocol.HalfBlocks);
		return (new TerminalImageCapabilities
		{
			Supported = list,
			Brand = brand,
			CellSize = cellSize,
			TrueColor = trueColor,
			IsMultiplexer = multiplexer,
			IsRemote = remote,
			Confidence = brand is null ? DetectionConfidence.Default : DetectionConfidence.Environment,
			Diagnostics = diagnostics,
		}, false);
	}

	private static string? BrandFromEnvironment(TerminalEnvironment e)
	{
		if (e.Has("KITTY_WINDOW_ID") || e.StartsWith("TERM", "xterm-kitty"))
			return "kitty";
		if (e.StartsWith("TERM", "xterm-ghostty") || e.Is("TERM_PROGRAM", "ghostty") || e.Has("GHOSTTY_RESOURCES_DIR"))
			return "ghostty";
		if (e.Is("TERM_PROGRAM", "WezTerm") || e.Has("WEZTERM_EXECUTABLE"))
			return "wezterm";
		if (e.Is("TERM_PROGRAM", "iTerm.app") || e.Has("ITERM_SESSION_ID") || e.Is("LC_TERMINAL", "iTerm2"))
			return "iterm2";
		if (e.Has("KONSOLE_VERSION"))
			return "konsole";
		if (e.Has("WT_SESSION"))
			return "windows-terminal";
		if (e.Is("TERM_PROGRAM", "WarpTerminal"))
			return "warp";
		if (e.Is("TERM_PROGRAM", "vscode"))
			return "vscode";
		if (e.Is("TERM_PROGRAM", "mintty") || e.Is("TERM", "mintty"))
			return "mintty";
		if (e.Is("TERM_PROGRAM", "rio") || e.Is("TERM", "rio"))
			return "rio";
		if (e.StartsWith("TERM", "foot"))
			return "foot";
		if (e.Is("TERM", "mlterm"))
			return "mlterm";
		if (e.Is("TERM_PROGRAM", "Apple_Terminal"))
			return "apple-terminal";
		return null;
	}

	private static CellPixelSize? ParseCellSize(TerminalEnvironment environment, List<string> diagnostics)
	{
		var text = environment.Get("NULLEAN_TERMINAL_CELL_SIZE");
		if (text is null)
			return null;
		var parts = text.Split('x', 'X');
		if (parts.Length == 2
			&& int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
			&& int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
			&& width > 0 && height > 0)
		{
			diagnostics.Add($"Cell size set by NULLEAN_TERMINAL_CELL_SIZE: {width}x{height}px.");
			return new CellPixelSize(width, height);
		}

		diagnostics.Add($"NULLEAN_TERMINAL_CELL_SIZE={text} is not WIDTHxHEIGHT: ignored.");
		return null;
	}

	/// <summary>Parses a protocol name: kitty, iterm2, sixel, blocks or none. Null for anything else.</summary>
	public static TerminalImageProtocol? ParseProtocol(string text) => text.Trim().ToLowerInvariant() switch
	{
		"kitty" => TerminalImageProtocol.Kitty,
		"iterm2" or "iterm" => TerminalImageProtocol.ITerm2,
		"sixel" => TerminalImageProtocol.Sixel,
		"blocks" or "halfblocks" or "half-blocks" => TerminalImageProtocol.HalfBlocks,
		"none" or "off" or "0" => TerminalImageProtocol.None,
		_ => null,
	};
}
