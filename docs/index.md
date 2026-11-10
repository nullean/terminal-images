# Nullean.TerminalImages

Show images in the terminal with the Kitty, iTerm2 and Sixel protocols, with protocol detection and a half-block fallback.
The core package has no dependencies and publishes with NativeAOT.

```csharp
var capabilities = await TerminalImageDetector.DetectAsync();          // what does this terminal speak?
var renderer = new TerminalImageRenderer(capabilities);
renderer.RenderPng(File.ReadAllBytes("photo.png"), Console.Out);       // best protocol, scaled to fit
```

Detection reads the environment first. If that is not enough, it asks the terminal once, with a batch of queries that ends with
a Device Attributes request. Every terminal answers that, so the wait ends when the terminal has said everything it will say.

## What it is for

- **No dependencies.** The PNG codec, the resampler and all four encoders are managed code. There is no SkiaSharp, no ImageSharp
  and no native asset, so a NativeAOT `tcat` is 1.7 MB.
- **Detection that does not hang.** One batched probe, a sentinel that ends the wait, and a timeout that applies only when the
  terminal says nothing.
- **Hosts, not only a library.** Integrations for Spectre.Console, Terminal.Gui v2 and XenoAtom.Terminal.
- **An override for every guess.** `NULLEAN_TERMINAL_IMAGES` forces a protocol, and `tcat --diagnostics` shows why detection chose
  what it did.

## Packages

| Package | For |
|---|---|
| `Nullean.TerminalImages` | The core: image model, PNG codec, resampler, encoders, detection, renderer. No package dependencies |
| `Nullean.TerminalImages.Spectre` | A `TerminalImage` renderable for Spectre.Console |
| `Nullean.TerminalImages.TerminalGui` | A `TerminalImageView` for Terminal.Gui v2, and detection that feeds Terminal.Gui's own pipeline |
| `Nullean.TerminalImages.XenoAtom` | `WriteImage` for XenoAtom.Terminal, without SkiaSharp |

## Status

An early release. The encoders, PNG codec, resampler, detection logic and renderer have mutation-checked unit tests. The terminal
probe has run against a scripted terminal in a pseudo-terminal, on macOS and on Linux. Output on real terminals (Kitty, Ghostty, iTerm2,
WezTerm, foot, Windows Terminal, xterm), and tmux and SSH, is **not yet verified**: see [troubleshooting](troubleshooting/index.md)
for what detection assumes about each.

Start with [getting started](getting-started/index.md).
