# Nullean.TerminalImages.TerminalGui

![terminal-images_](https://raw.githubusercontent.com/nullean/terminal-images/main/docs/images/brand/png/lockup-light.png)

Show images in Terminal.Gui v2: a `TerminalImageView` that feeds Terminal.Gui's Sixel and Kitty pipeline, a half-block fallback, and detection that Terminal.Gui's own handshake cannot do (probing, SSH, tmux).

[![NuGet](https://img.shields.io/nuget/v/Nullean.TerminalImages.TerminalGui?style=flat-square)](https://www.nuget.org/packages/Nullean.TerminalImages.TerminalGui)

## Install

```sh
dotnet add package Nullean.TerminalImages.TerminalGui
```

## Quick start

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

- With Sixel or Kitty, Terminal.Gui encodes, clips and repaints the image; without them the view draws half blocks.
  Setting `Image`, `Resample` or `AllowUpscale` redraws the view.
- Detect before `app.Run`: once Terminal.Gui reads input, a probe's replies get eaten.
- Terminal.Gui has no iTerm2 pipeline, so an iTerm2-only terminal gets half blocks.
- Built against Terminal.Gui 2.x; not for Terminal.Gui v1.

## Learn more

- Documentation: https://terminal-images.nullean.net
- Integrations: https://terminal-images.nullean.net/integrations/
- Source and issues: https://github.com/nullean/terminal-images
