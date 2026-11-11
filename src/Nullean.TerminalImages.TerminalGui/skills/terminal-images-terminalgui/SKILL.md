---
name: terminal-images-terminalgui
description: >-
  Use when a Terminal.Gui v2 app needs to show an image in a view. Nullean.TerminalImages.TerminalGui gives
  TerminalImageView, which feeds Terminal.Gui's own Sixel/Kitty pipeline, and ApplyDetection for tmux, SSH and probed results.
---

# Nullean.TerminalImages.TerminalGui

`TerminalImageView` is a `View` that shows an `RgbaImage`. With Sixel or Kitty, Terminal.Gui encodes, clips and repaints
it; without them the view draws half blocks. Built against Terminal.Gui 2.x; not for Terminal.Gui v1.

## The calls you need

```csharp
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Png;
using Nullean.TerminalImages.TerminalGui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using var app = Application.Create();
app.Init();

var capabilities = await TerminalImageDetector.DetectAsync(); // before the input loop runs
app.Driver!.ApplyDetection(capabilities);                    // replaces Terminal.Gui's own handshake result

var view = new TerminalImageView
{
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    Image = PngDecoder.Decode(File.ReadAllBytes("logo.png")),
};
var window = new Window();
window.Add(view);
app.Run(window);
```

Setting `Image`, `Resample` or `AllowUpscale` redraws the view.

## Gotchas

- Terminal.Gui's handshake does not see through tmux or SSH and detects Kitty from environment variables only; call
  `ApplyDetection` when that matters.
- Detect before `app.Run`: once Terminal.Gui reads input, a probe's replies get eaten.
- Terminal.Gui has no iTerm2 pipeline, so an iTerm2-only terminal gets half blocks.
- `ToSixelSupport` / `ToKittySupport` give the driver results without applying them, if you need to combine them yourself.

Docs: https://terminal-images.nullean.net
