# Nullean.TerminalImages

Show images in the terminal with the Kitty, iTerm2 and Sixel protocols, with protocol detection and a half-block
fallback. No dependencies, NativeAOT-safe.

```csharp
var capabilities = await TerminalImageDetector.DetectAsync();          // what does this terminal speak?
var renderer = new TerminalImageRenderer(capabilities);
renderer.RenderPng(File.ReadAllBytes("photo.png"), Console.Out);       // best protocol, scaled to fit
```

Site: <https://terminal-images.nullean.net>

## Why

- **Zero dependencies.** The core package has no `PackageReference`s: the PNG codec, resampler and all four encoders are
  managed code. No SkiaSharp, no ImageSharp, no native assets, so it publishes with NativeAOT (a 1.7 MB `tcat` binary).
- **Detection that does not hang.** It reads the environment first, then asks the terminal once with a batch of queries that always
  ends with a Device Attributes request, which every terminal answers. The wait ends when the terminal has said everything it will say.
- **Hosts, not just a library.** Integrations for Spectre.Console, Terminal.Gui v2 and XenoAtom.Terminal.

## Packages

| Package | For |
|---|---|
| `Nullean.TerminalImages` | The core: image model, PNG codec, resampler, encoders, detection, renderer. No package dependencies. |
| `Nullean.TerminalImages.Spectre` | A `TerminalImage` renderable for Spectre.Console |
| `Nullean.TerminalImages.TerminalGui` | A `TerminalImageView` for Terminal.Gui v2, and detection that feeds Terminal.Gui's own pipeline |
| `Nullean.TerminalImages.XenoAtom` | `WriteImage` for XenoAtom.Terminal (no SkiaSharp) |

## Documentation

- [Getting started](docs/getting-started/index.md)
- [Detection: how the protocol is chosen](docs/detection/index.md)
- [The protocols and their trade-offs](docs/protocols/index.md)
- [Spectre.Console, Terminal.Gui and XenoAtom](docs/integrations/index.md)
- [Troubleshooting: tmux, SSH, Windows, support matrix](docs/troubleshooting/index.md)
- [DECISIONS.md](DECISIONS.md): why it is built the way it is

## Status

An early release. What has been verified, and how:

| Verified | How |
|---|---|
| Encoders, PNG codec, resampler, detection logic, renderer | Unit tests, with mutation checks: each rule was broken on purpose to confirm a test fails |
| Probing a terminal (raw mode, timeout, restoring the terminal) | The published NativeAOT binary run in a pseudo-terminal with a scripted terminal, on macOS and on Linux (in CI) |
| Terminal.Gui view | A live Terminal.Gui application with its ANSI driver, in tests |
| XenoAtom | Its in-memory backend |
| NativeAOT | Published with warnings as errors on macOS arm64, and in CI on Linux, macOS and Windows, where the published binary also runs |

**Not yet verified:** output on real terminals (Kitty, Ghostty, iTerm2, WezTerm, foot, Windows Terminal, xterm), and tmux or SSH. The terminal-specific choices in
[`TerminalBrands`](src/Nullean.TerminalImages/Detection/TerminalBrands.cs) come from how other terminal image tools and the
terminals' documentation treat them. If a terminal misbehaves, `tcat --diagnostics` shows what was detected and why, and
`NULLEAN_TERMINAL_IMAGES=kitty|iterm2|sixel|blocks|none` overrides it.

## Try it

```sh
dotnet run --project examples/Nullean.TerminalImages.Cat -- --diagnostics               # what was detected, and why
dotnet run --project examples/Nullean.TerminalImages.Cat -- examples/sunset.png         # draw an image
dotnet run --project examples/Nullean.TerminalImages.Cat -- --protocol sixel examples/sunset.png
```

`tcat` reads PNG files only: the library has a PNG decoder, and no JPEG or GIF decoder.

## License

MIT
