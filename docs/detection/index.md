# Detection

`TerminalImageDetector` works from the cheapest evidence to the most expensive, and stops as soon as it has an answer it can trust.

| Step | Cost | What it does |
|---|---|---|
| 1. Overrides | none | `NULLEAN_TERMINAL_IMAGES`, or `TerminalImageDetectionOptions.Protocol`. The result has confidence `Forced` and is never probed |
| 2. Gates | none | Output redirected, or `TERM=dumb`: nothing is drawn |
| 3. Environment | none | Recognises the terminal from `KITTY_WINDOW_ID`, `TERM`, `TERM_PROGRAM`, `LC_TERMINAL`, `WEZTERM_EXECUTABLE`, `GHOSTTY_RESOURCES_DIR`, `ITERM_SESSION_ID`, `KONSOLE_VERSION`, `WT_SESSION`. Confidence `Environment` |
| 4. Probe | one round trip | Asks the terminal. Confidence `Probed` |

The result is a `TerminalImageCapabilities`: the supported protocols best first (half blocks last), the terminal's name and
version, cell size in pixels, Sixel colour registers, whether tmux or SSH is involved, the confidence, and `Diagnostics`, one line
per decision. `tcat --diagnostics` prints all of it.

## The probe

One write, with the Device Attributes request last:

```
ESC _ G i=31,s=1,v=1,a=q,t=d,f=24;AAAA ESC \    Kitty graphics query
ESC [ > 0 q                                      XTVERSION: the terminal's name and version
ESC [ 16 t                                       the size of a cell in pixels
ESC [ c                                          DA1: the sentinel
```

Every terminal answers DA1, and a terminal answers queries in order. So when the DA1 reply arrives, the terminal is done: there
is no waiting for a timeout on a terminal that ignored the other queries. If DA1 reports attribute 4, the terminal speaks Sixel,
and a second query (`ESC [ ? 1 ; 1 ; 0 S`, XTSMGRAPHICS) asks how many colour registers it has.

| Reply | Meaning |
|---|---|
| `ESC _ G i=31;OK ESC \` | Kitty graphics work |
| DA1 arrived but no Kitty reply | Kitty graphics are not available. This catches a stale `KITTY_WINDOW_ID` |
| `ESC P > \| name(version) ESC \` | The terminal's own name: it wins over the environment |
| DA1 with `;4;` | Sixel |
| `ESC [ 6 ; height ; width t` | Cell size in pixels |

The timeout applies only when the terminal says nothing: 250 ms, or 1.5 s when `SSH_*` is set. It is
`TerminalImageDetectionOptions.ProbeTimeout`.

### Brand order

A terminal can support a protocol and still be better off with another. `TerminalBrands` is plain data with the order that
encodes this: WezTerm gets iTerm2 images before its Kitty and Sixel; Konsole avoids Sixel; Kitty and Ghostty get Kitty; foot and
Windows Terminal get Sixel. The probe only adds protocols the brand's list does not have; it does not reorder them.

### Who reads the terminal's answer

Two readers on one input stream steal bytes from each other, so there are two ways to probe:

- `StandaloneQueryChannel` (the default) opens `/dev/tty` and reads the answer itself: raw mode, a read timeout from the terminal
  (`VMIN=0, VTIME=1`), and the terminal restored afterwards. macOS and Linux. Use it before your own input loop starts.
- Your own `ITerminalQueryChannel`, if you already own terminal input (a TUI framework). It writes the request, collects the
  reply, and calls the `isComplete` callback until it returns true or the time runs out.

```csharp
var options = new TerminalImageDetectionOptions { Channel = TerminalImageDetectionOptions.NoProbing }; // environment only
```

**Windows:** there is no standalone probe. Console input cannot be read with a timeout without risking a hang, and ConPTY does not
reliably deliver replies. Detection uses the environment (Windows Terminal sets `WT_SESSION`), or the host passes its own channel.

## tmux and screen

With `TMUX` set, the whole probe (DA1 included) is wrapped in tmux's DCS passthrough, so it reaches the outer terminal: tmux would
answer DA1 itself and end the wait before the real terminal replied. In that case a missing Kitty reply proves nothing, so Kitty is
not ruled out. Images are wrapped the same way when drawn (`TerminalImageRenderOptions.TmuxPassthrough`, on by default), one
piece per escape sequence, so a long Kitty image becomes many small passthrough sequences.

This needs tmux 3.3 or newer with `set -g allow-passthrough on`. This package never runs `tmux set` for you. GNU screen is not
supported.

## Testing detection

```csharp
var environment = TerminalEnvironment.From(new Dictionary<string, string> { ["KITTY_WINDOW_ID"] = "1" });
var capabilities = TerminalImageDetector.DetectFromEnvironment(environment, isOutputRedirected: false);
```

`ProbeReplyParser.Parse` is a pure function over the text a terminal sent, so recorded replies make good test fixtures.
