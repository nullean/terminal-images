# Integrations

Each integration is a separate package that depends on the core and on its host. The core never references a host.

## Spectre.Console: `Nullean.TerminalImages.Spectre`

```csharp
using Nullean.TerminalImages.Spectre;
using Spectre.Console;

TerminalImage.DefaultCapabilities = await TerminalImageDetector.DetectAsync();   // optional: probe once at start-up

AnsiConsole.Write(new TerminalImage(image) { MaxWidth = 60, AltText = "[photo]" });
AnsiConsole.Write(TerminalImage.FromPng(File.ReadAllBytes("photo.png")));
```

`TerminalImage` is a `Renderable`. With a graphics protocol it writes one zero-width control segment that draws the image from
the cursor and puts the cursor back, then one line break per row, so Spectre's layout and `Live` displays count the rows. Without
one it draws half blocks as Spectre `Segment`s with Spectre `Style` colours, so Spectre downgrades them to the console's colour
system. Without ANSI it writes `AltText`.

Every line ends with a line break, like Spectre's own widgets: the next write starts below the image.

**Limit:** a `Panel` or `Table` pads each line with spaces, and those spaces erase a Sixel or iTerm2 image drawn beneath them.
Kitty images are on a separate layer and survive. Put images directly on the console, or use Kitty, inside such a widget.

Spectre cannot read terminal replies, so detection runs before rendering with the standalone channel
(`TerminalImageDetector.DetectAsync`), not through Spectre.

## Terminal.Gui v2: `Nullean.TerminalImages.TerminalGui`

```csharp
using Nullean.TerminalImages.TerminalGui;

using var app = Application.Create();
app.Init();

var capabilities = await TerminalImageDetector.DetectAsync();     // before the input loop runs
app.Driver!.ApplyDetection(capabilities);                         // replace what Terminal.Gui worked out itself

var view = new TerminalImageView { Width = Dim.Fill(), Height = Dim.Fill(), Image = image };
```

Terminal.Gui has its own Sixel and Kitty pipeline: it clips an image to its view, orders it with text, and repaints it on resize.
`TerminalImageView` feeds that pipeline scaled pixels and the destination cells; Terminal.Gui does the encoding. Without Sixel or
Kitty it draws half blocks into the view's cells with the view's own colours. Terminal.Gui's pipeline has no iTerm2 protocol, so
an iTerm2-only terminal gets half blocks.

`ApplyDetection` exists because Terminal.Gui's start-up handshake does not see through tmux or SSH, and it detects Kitty from
environment variables only. A probe can do better, and `ApplyDetection` hands the answer (support, cell size, palette size) to the
driver. Run detection before Terminal.Gui starts reading input: two readers on one stream steal each other's bytes.

Tested against Terminal.Gui 2.5.0.

## XenoAtom.Terminal: `Nullean.TerminalImages.XenoAtom`

```csharp
using Nullean.TerminalImages.XenoAtom;

var terminal = Terminal.Instance;
var capabilities = await terminal.DetectImagesAsync(probe: true);   // before the first key read
terminal.WriteImage(image, capabilities);
terminal.WritePng(File.ReadAllBytes("photo.png"), capabilities);
```

Independent of `XenoAtom.Terminal.Graphics`: no SkiaSharp, and only the core `XenoAtom.Terminal` package. `WriteImage` fits the
image to the terminal's size, renders to a string, and writes it with `WriteAtomic`, so no other output lands in the middle of it.
`DetectImagesAsync` uses XenoAtom's colour level, redirection and ANSI flags.

XenoAtom does not hand terminal replies to the application, so `probe: true` reads `/dev/tty` itself and must run before XenoAtom's
input loop starts. Without it, detection uses the environment only.

## Writing your own

Implement `ITerminalQueryChannel` if you own terminal input and want probing; render with `TerminalImageRenderer` or the encoders and
write the text however your host writes raw output. Everything a host needs is in the core: `TerminalImageDetector`,
`ProbeReplyParser`, `TerminalImageRenderer`, `TmuxPassthrough`.
