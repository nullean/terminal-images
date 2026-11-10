# Troubleshooting

Start with `tcat --diagnostics`: it prints the protocol, the terminal it thinks it is, how sure it is, and one line for every decision.

```sh
dotnet run --project examples/Nullean.TerminalImages.Cat -- --diagnostics
```

From code: `capabilities.Diagnostics`, `capabilities.Confidence`.

## What to expect

This is the table the detection works from, not a list of tested terminals. See [Status](https://github.com/nullean/terminal-images#status): output on real
terminals has not been verified yet. Corrections are welcome as issues, and `TerminalBrands` is plain data.

| Terminal | Protocols, best first | Source |
|---|---|---|
| Kitty, Ghostty | Kitty | Their documentation |
| WezTerm | iTerm2, Sixel | Other tools prefer its iTerm2 images over its Kitty and Sixel |
| iTerm2 | iTerm2 | Its documentation |
| Konsole | iTerm2 | Its Sixel is reported buggy |
| foot, Windows Terminal, mlterm, Contour | Sixel | Their documentation |
| mintty, VS Code, Rio, Warp | iTerm2 (and Sixel where listed) | Other tools' tables |
| xterm | Whatever DA1 reports | Sixel only with `-ti vt340` |
| Apple Terminal | Half blocks | No image protocol |

Anything else gets half blocks, plus whatever the probe proves.

## Nothing is drawn

- **Output is redirected.** The renderer draws nothing when stdout is a file or a pipe. `Render` returns false.
- **`TERM=dumb`.**
- **`NULLEAN_TERMINAL_IMAGES=none`** is set.

Force a protocol to rule detection out: `NULLEAN_TERMINAL_IMAGES=sixel tcat photo.png`.

## The image is squashed, or covers too many rows

The cell size is wrong. Kitty and iTerm2 scale to a cell box and only drift in aspect; Sixel is drawn at its pixel size, so it
covers the wrong number of rows. Set it: `NULLEAN_TERMINAL_CELL_SIZE=9x18`, or `TerminalImageRenderOptions.FallbackCellSize`. A probe
finds it automatically on macOS and Linux.

## tmux

- Needs tmux 3.3 or newer and `set -g allow-passthrough on`. Without it the sequences are swallowed and nothing appears. The package
  never changes tmux settings.
- Images drawn through passthrough can land in the wrong place or disappear on redraw: tmux does not know the image is there.
  Kitty's behaviour is better than Sixel's.
- Environment variables describe the terminal that started tmux, which may not be the one attached now. Set
  `NULLEAN_TERMINAL_IMAGES` to be sure.
- GNU screen is not supported.

## SSH

Environment variables are often not forwarded. `LC_TERMINAL` (iTerm2) usually is. The probe asks the real terminal and works over
SSH; its timeout is 1.5 s there instead of 250 ms. Large images are slow: use `KittyEncoder.Compress` or a smaller
`MaxColumns`/`MaxRows`.

## Windows

There is no standalone probe on Windows (see [Detection](../detection/index.md#who-reads-the-terminals-answer)). Windows Terminal is
recognised from `WT_SESSION` and gets Sixel. Set `NULLEAN_TERMINAL_IMAGES` if that is wrong for your terminal, or pass your own
`ITerminalQueryChannel` from a host that reads console input.

## Spectre: the image disappears inside a Panel or Table

Those widgets pad each line with spaces, which erase Sixel and iTerm2 images. Draw the image directly on the console, or use a Kitty
terminal.

## A stale `KITTY_WINDOW_ID`

A leftover variable from a parent shell claims Kitty in a terminal that does not have it. The probe catches this (DA1 arrives with no
Kitty reply). With `Protocol` forced or the probe off, you get what the variable says.

## Reporting a problem

Include the output of `tcat --diagnostics`, your terminal and version, and whether tmux or SSH is involved. For a wrong or missing
brand entry, `TerminalBrands.cs` is the file to change.
