# Getting started

```sh
dotnet add package Nullean.TerminalImages
```

Requires .NET 10.

## Draw an image

```csharp
using Nullean.TerminalImages;
using Nullean.TerminalImages.Detection;

// 1. Ask what the terminal speaks. This reads the environment and, on macOS and Linux, asks the terminal once.
TerminalImageCapabilities capabilities = await TerminalImageDetector.DetectAsync();

// 2. Draw. The renderer scales the image to fit, picks the encoder, and leaves the cursor below the image.
var renderer = new TerminalImageRenderer(capabilities);
bool drawn = renderer.RenderPng(File.ReadAllBytes("photo.png"), Console.Out);
```

`Render` returns false when the terminal cannot show images at all (output redirected, `TERM=dumb`, or
`NULLEAN_TERMINAL_IMAGES=none`).

Write to a `Stream` with a UTF-8 writer if you draw half blocks: they contain `▀`.

```csharp
using var stdout = Console.OpenStandardOutput();
using var writer = new StreamWriter(stdout, new UTF8Encoding(false));
renderer.Render(image, writer);
writer.Flush();
```

## Images

The core works on `RgbaImage`: width, height and 8-bit RGBA pixels (non-premultiplied, rows top to bottom).

```csharp
var image = new RgbaImage(width, height, pixels);        // your own pixels
var decoded = PngDecoder.Decode(File.ReadAllBytes(path)); // Nullean.TerminalImages.Png: every PNG colour type, depth and Adam7 interlacing
byte[] png = PngEncoder.Encode(image);
```

There is no JPEG, GIF or WebP decoder: decode those with a library of your choice and pass the pixels in.

## Sizing

```csharp
var renderer = new TerminalImageRenderer(capabilities, new TerminalImageRenderOptions
{
    MaxColumns = Console.WindowWidth,
    MaxRows = Console.WindowHeight - 2,
    AllowUpscale = false,                       // small images keep their size
    Resample = ResampleMode.Auto,               // Nearest for pixel art
    FallbackCellSize = new CellPixelSize(10, 20), // used when the terminal's cell size is unknown
});

TerminalImageLayout layout = renderer.Layout(image);   // the pixel size and cells, without drawing
```

The cell size matters: a Sixel image is drawn at its pixel size, so the renderer needs to know how many pixels a cell is to
cover the right number of rows. A probe finds it (`CSI 16 t`); otherwise set `NULLEAN_TERMINAL_CELL_SIZE=9x18` or
`FallbackCellSize`.

## Without detection

```csharp
var capabilities = new TerminalImageCapabilities { Supported = [TerminalImageProtocol.Kitty] };
```

or use an encoder directly:

```csharp
new SixelEncoder { MaxColors = 64, Dither = true }.Encode(image, default, Console.Out);
new KittyEncoder { Compress = true }.Encode(image, new CellSize(40, 10), Console.Out);
```

## Environment variables

| Variable | Effect |
|---|---|
| `NULLEAN_TERMINAL_IMAGES` | `kitty`, `iterm2`, `sixel`, `blocks`, `none` force a protocol; `auto` detects |
| `NULLEAN_TERMINAL_CELL_SIZE` | `WIDTHxHEIGHT` in pixels, for example `9x18` |
