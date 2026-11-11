---
name: terminal-images-xenoatom
description: >-
  Use when a XenoAtom.Terminal app needs to show an image. Nullean.TerminalImages.XenoAtom adds WriteImage, WritePng and
  DetectImagesAsync to TerminalInstance, with Kitty, iTerm2, Sixel or half blocks; no SkiaSharp.
---

# Nullean.TerminalImages.XenoAtom

Extension methods on XenoAtom's `TerminalInstance`. They need only the core `XenoAtom.Terminal` package, not
`XenoAtom.Terminal.Graphics`. Images are written atomically and scaled to the terminal window unless you pass options.

## The calls you need

```csharp
using Nullean.TerminalImages;
using Nullean.TerminalImages.XenoAtom;
using XenoAtom.Terminal;

var terminal = Terminal.Instance;

// probe: true asks the terminal (macOS/Linux). Without it, detection reads the environment only.
var capabilities = await terminal.DetectImagesAsync(probe: true);

if (!terminal.WritePng(File.ReadAllBytes("logo.png"), capabilities))
    terminal.WriteLine("(no image support)");
```

Size limits:

```csharp
terminal.WriteImage(RgbaImage.Solid(64, 32, 255, 128, 0), capabilities,
    new TerminalImageRenderOptions { MaxColumns = 40, MaxRows = 10 });
```

## Gotchas

- Probe before XenoAtom's input loop starts (before the first key read or `StartInput`): XenoAtom does not hand
  terminal replies to the application, so the probe reads `/dev/tty` itself.
- Omitting `capabilities` detects from the environment on every call; detect once and pass the result.
- XenoAtom's colour level, not `COLORTERM`, decides whether half blocks use 24-bit colour.
- Returns false, writing nothing, when output is redirected or ANSI is off.

Docs: https://terminal-images.nullean.net
