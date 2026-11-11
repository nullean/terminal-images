# Nullean.TerminalImages.XenoAtom

![terminal-images_](https://raw.githubusercontent.com/nullean/terminal-images/main/docs/images/brand/png/lockup-light.png)

Show images in XenoAtom.Terminal with the Kitty, iTerm2 or Sixel protocol, with half blocks as the fallback. Needs only the core `XenoAtom.Terminal` package, not `XenoAtom.Terminal.Graphics` or SkiaSharp.

[![NuGet](https://img.shields.io/nuget/v/Nullean.TerminalImages.XenoAtom?style=flat-square)](https://www.nuget.org/packages/Nullean.TerminalImages.XenoAtom)

## Install

```sh
dotnet add package Nullean.TerminalImages.XenoAtom
```

## Quick start

```csharp
using Nullean.TerminalImages;
using Nullean.TerminalImages.XenoAtom;
using XenoAtom.Terminal;

var terminal = Terminal.Instance;

// probe: true asks the terminal (macOS/Linux). Without it, detection reads the environment only.
var capabilities = await terminal.DetectImagesAsync(probe: true);

if (!terminal.WritePng(File.ReadAllBytes("logo.png"), capabilities, new TerminalImageRenderOptions { MaxColumns = 40 }))
	terminal.WriteLine("(no image support)");
```

- Images are written atomically and scaled to the terminal window unless `TerminalImageRenderOptions` says otherwise;
  `WriteImage` takes an `RgbaImage` you already have.
- Probe before XenoAtom's input loop starts: XenoAtom does not hand terminal replies to the application, so the probe
  reads `/dev/tty` itself.
- Omitting `capabilities` detects from the environment on every call; detect once and pass the result.
- Returns false, writing nothing, when output is redirected or ANSI is off.

## Learn more

- Documentation: https://terminal-images.nullean.net
- Integrations: https://terminal-images.nullean.net/integrations/
- Source and issues: https://github.com/nullean/terminal-images
