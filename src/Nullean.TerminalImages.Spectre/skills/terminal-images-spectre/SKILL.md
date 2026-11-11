---
name: terminal-images-spectre
description: >-
  Use when a Spectre.Console app needs to show an image. Nullean.TerminalImages.Spectre gives a TerminalImage renderable
  that draws Kitty, iTerm2 or Sixel when the terminal has it, and half blocks in Spectre colours otherwise.
---

# Nullean.TerminalImages.Spectre

`TerminalImage` is a Spectre `Renderable`: write it like any other widget. It sizes itself to the width Spectre offers.
For a plain console without Spectre use `Nullean.TerminalImages` and its `TerminalImageRenderer` directly.

## The calls you need

```csharp
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Spectre;
using Spectre.Console;

// Optional: probe once at start-up. Without this, images use environment-only detection.
TerminalImage.DefaultCapabilities = await TerminalImageDetector.DetectAsync();

AnsiConsole.Write(TerminalImage.FromPng(File.ReadAllBytes("logo.png")));
```

Size, resampling and alt text per image:

```csharp
var image = TerminalImage.FromPng(File.ReadAllBytes("chart.png"));
image.MaxWidth = 40;                                  // cells; never wider than Spectre offers
image.MaxHeight = 12;
image.Resample = Nullean.TerminalImages.ResampleMode.Nearest; // pixel art
image.AltText = "[chart]";                            // written when the console has no ANSI or no image support
AnsiConsole.Write(image);
```

## Gotchas

- Inside a `Panel` or `Table`, Spectre pads lines with spaces that overwrite Sixel and iTerm2 images; only Kitty
  survives. Put images directly on the console, or accept half blocks there.
- `DefaultCapabilities` is static and cached; set `Capabilities` on one image to override it for that image.
- Probe before a `Live` display or prompt starts reading input.
- Half blocks use Spectre colours, so Spectre's colour-depth downgrade applies; a 16-colour console gets a rough image.

Docs: https://terminal-images.nullean.net
