---
name: terminal-images
description: >-
  Use when a .NET console app or CLI needs to show an image (a PNG, a chart, a logo) in the terminal. Nullean.TerminalImages
  detects whether the terminal speaks Kitty, iTerm2 or Sixel, falls back to half blocks, and draws; no dependencies, NativeAOT-safe.
---

# Nullean.TerminalImages

Detect once, then draw with `TerminalImageRenderer`. Only PNG is decoded; for other formats build an `RgbaImage` from RGBA
bytes yourself. For Spectre.Console, Terminal.Gui or XenoAtom.Terminal use the `Nullean.TerminalImages.<Host>` package instead.

## The calls you need

```csharp
using Nullean.TerminalImages;
using Nullean.TerminalImages.Detection;

// Reads the environment, then probes the terminal (macOS/Linux; one batched query, 250 ms timeout, 1.5 s over SSH).
var capabilities = await TerminalImageDetector.DetectAsync();

var renderer = new TerminalImageRenderer(capabilities, new TerminalImageRenderOptions { MaxColumns = 60, MaxRows = 20 });
if (!renderer.RenderPng(File.ReadAllBytes("logo.png"), Console.Out))
    Console.WriteLine("(this terminal cannot show images)");
```

Environment only, or a forced protocol (no I/O, no waiting):

```csharp
var fromEnv = await TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions
{
    Channel = TerminalImageDetectionOptions.NoProbing,      // never ask the terminal
    Protocol = TerminalImageProtocol.HalfBlocks,             // or leave null to detect
});
var sync = TerminalImageDetector.DetectFromEnvironment(TerminalEnvironment.Current, Console.IsOutputRedirected);
```

Pixels you already have, and why detection chose what it did:

```csharp
var image = new RgbaImage(width: 2, height: 1, pixels: new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }); // RGBA, row-major
var layout = renderer.Layout(image);                         // cells it will cover, without drawing
foreach (var line in capabilities.Diagnostics)
    Console.Error.WriteLine(line);
```

## Gotchas

- Redirected output (a pipe or file) detects as `None`, and `Render` returns false: nothing is written. Force a protocol
  to write escape sequences into a file anyway.
- `NULLEAN_TERMINAL_IMAGES=kitty|iterm2|sixel|blocks|none|auto` overrides detection without code; check it before
  assuming detection is wrong.
- Probe before anything else reads stdin (an input loop, a prompt): two readers on the terminal steal each other's bytes.
- Detection sees through tmux and wraps output in its passthrough; tmux itself must have `allow-passthrough on`, and
  this library never changes that setting for the user.
- `RenderPng` throws `InvalidDataException` for anything that is not a PNG.

Docs: https://terminal-images.nullean.net
