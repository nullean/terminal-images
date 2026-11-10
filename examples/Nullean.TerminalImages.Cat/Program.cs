using System.Text;
using Nullean.TerminalImages;
using Nullean.TerminalImages.Detection;

// tcat: an imgcat-style tool, and the NativeAOT smoke app for CI.
//   tcat photo.png                 draw a PNG with the best protocol the terminal has
//   tcat --diagnostics             show what was detected and why, without drawing
//   tcat --protocol sixel a.png    force kitty | iterm2 | sixel | blocks | none
//   tcat --no-probe a.png          detect from the environment only (never ask the terminal)

string? file = null;
TerminalImageProtocol? forced = null;
var probe = true;
var diagnostics = false;
var upscale = false;
int? maxColumns = null, maxRows = null;

for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "-h" or "--help":
			Console.WriteLine("Usage: tcat [--protocol kitty|iterm2|sixel|blocks|none|auto] [--no-probe] [--diagnostics] [--upscale] [--max-columns N] [--max-rows N] <image.png>");
			return 0;
		case "--protocol" when i + 1 < args.Length:
			var name = args[++i];
			if (!name.Equals("auto", StringComparison.OrdinalIgnoreCase))
			{
				forced = TerminalImageDetector.ParseProtocol(name);
				if (forced is null)
					return Fail($"Unknown protocol '{name}'.");
			}
			break;
		case "--no-probe":
			probe = false;
			break;
		case "--diagnostics":
			diagnostics = true;
			break;
		case "--upscale":
			upscale = true;
			break;
		case "--max-columns" when i + 1 < args.Length && int.TryParse(args[i + 1], out var columns) && columns > 0:
			maxColumns = columns;
			i++;
			break;
		case "--max-rows" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rows) && rows > 0:
			maxRows = rows;
			i++;
			break;
		default:
			if (args[i].StartsWith('-'))
				return Fail($"Unknown option '{args[i]}'.");
			file = args[i];
			break;
	}
}

if (file is null && !diagnostics)
	return Fail("No image given. Try --help.");

var capabilities = await TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions
{
	Protocol = forced,
	Channel = probe ? null : TerminalImageDetectionOptions.NoProbing,
});

if (diagnostics)
{
	Console.WriteLine($"protocol:    {capabilities.Preferred}");
	Console.WriteLine($"supported:   {string.Join(", ", capabilities.Supported)}");
	Console.WriteLine($"terminal:    {capabilities.Brand ?? "unknown"}{(capabilities.BrandVersion is { } v ? " " + v : string.Empty)}");
	Console.WriteLine($"confidence:  {capabilities.Confidence}");
	Console.WriteLine($"cell size:   {(capabilities.CellSize is { } c ? $"{c.Width}x{c.Height}px" : "unknown")}");
	Console.WriteLine($"sixel regs:  {capabilities.SixelColorRegisters?.ToString() ?? "unknown"}");
	Console.WriteLine($"truecolor:   {capabilities.TrueColor}");
	Console.WriteLine($"multiplexer: {capabilities.IsMultiplexer}   ssh: {capabilities.IsRemote}");
	foreach (var line in capabilities.Diagnostics)
		Console.WriteLine($"  - {line}");
	if (file is null)
		return 0;
}

byte[] bytes;
try
{
	bytes = File.ReadAllBytes(file!);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
	return Fail($"Cannot read {file}: {exception.Message}");
}

RgbaImage image;
try
{
	image = Nullean.TerminalImages.Png.PngDecoder.Decode(bytes);
}
catch (InvalidDataException exception)
{
	return Fail($"{file}: {exception.Message} (only PNG files are supported).");
}

var width = 80;
var height = 24;
try
{
	width = Console.WindowWidth;
	height = Console.WindowHeight;
}
catch (IOException)
{
	// not a terminal: keep the defaults
}

var renderer = new TerminalImageRenderer(capabilities, new TerminalImageRenderOptions
{
	MaxColumns = maxColumns ?? Math.Max(1, width),
	MaxRows = maxRows ?? Math.Max(1, height - 2),
	AllowUpscale = upscale,
});

using var stdout = Console.OpenStandardOutput();
using var writer = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 64 * 1024);
if (!renderer.Render(image, writer))
{
	writer.Flush();
	return Fail("This terminal cannot show images (protocol: none). Try --protocol blocks.");
}

writer.Flush();
return 0;

static int Fail(string message)
{
	Console.Error.WriteLine($"tcat: {message}");
	return 1;
}
