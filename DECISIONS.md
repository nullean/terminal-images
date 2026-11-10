# Decisions

The non-obvious choices, and why. Add to this file in the same change that makes one.

## Zero dependencies in the core

The one existing .NET option (`XenoAtom.Terminal.Graphics`) depends on SkiaSharp and its native assets for every OS. That makes a
NativeAOT binary large, ties the package to one terminal library, and is more than an image-to-escape-sequence converter needs.
The core has no `PackageReference`s: a PNG codec on the BCL's `ZLibStream`, a hand-written CRC-32, a resampler, and four encoders.
There is no JPEG, GIF or WebP decoder; callers decode those themselves. Host integrations are separate packages.

## Detection: cheapest first, one batched probe, DA1 last

An explicit override, then gates, then the environment, then a single write of Kitty query + XTVERSION + `CSI 16 t` + DA1.
Every terminal answers DA1 and answers in order, so the DA1 reply means "done": a terminal that ignores the other queries costs
one round trip, not the whole timeout. This is what Kitty's own protocol documentation recommends, and what yazi and ratatui-image do.
DA1 reporting attribute 4 means Sixel; only then is a second query sent for the colour register count.

## `VMIN=0, VTIME=1` instead of `poll`

The first version used `poll(2)` for the read timeout. On macOS `poll` reports `POLLNVAL` at once for `/dev/tty`, which looked
like "ready", and the following `read` blocked forever whenever the terminal said nothing. The unit tests could not see this
(they use a fake channel); a test that runs the published binary in a pseudo-terminal did. The read timeout now comes from the
terminal itself, so a read returns after a tenth of a second with nothing. `tests/pty/probe_test.py` runs in CI.

`struct termios` differs between macOS and Linux, so it is a block of bytes with the few offsets needed. The macOS layout was
verified by that test on a Mac, and the Linux layout by the same test on a Linux runner in CI.

## No standalone probe on Windows

Console input cannot be read with a timeout without risking a hang when only non-key events arrive, and ConPTY does not reliably
deliver the replies. Windows uses the environment, or a host-supplied `ITerminalQueryChannel`.

## No `TIOCGWINSZ` pixel size

`ioctl` is variadic, and on Apple Silicon variadic arguments are passed differently from fixed ones, so a P/Invoke to it is
unsafe there. `CSI 16 t` gives the cell size instead.

## Hosts own terminal input, so probing goes through `ITerminalQueryChannel`

Two readers on one input stream steal each other's bytes. A TUI framework that owns input supplies a channel; everyone else gets
`StandaloneQueryChannel` before their own input loop starts. Detection logic is pure (`ProbeReplyParser`, `FromEnvironment`) and
tested without a terminal.

## Brand quirks are data

`TerminalBrands` is a dictionary of ordered protocol lists, not code. The order encodes quirks (WezTerm prefers iTerm2 images over
its rougher Kitty and Sixel). The entries come from other tools' behaviour and terminal documentation, and are not all verified on
real terminals, which is why they are easy to change.

## tmux: wrap, never configure

Passthrough wraps each escape sequence in its own `DCS tmux;` piece with every ESC doubled, so a long Kitty image is many small
sequences. The whole probe is wrapped, DA1 included, because tmux would answer a bare DA1 itself. A missing Kitty reply inside tmux
rules nothing out. The package never runs `tmux set`: changing a user's multiplexer settings is not a library's call.

## Cursor handling: reserve, save, draw, restore, step below

Terminals disagree about where the cursor ends up after an image. The renderer prints the newlines first (so the screen scrolls if
needed), moves up, saves the cursor (`ESC 7`), draws, restores (`ESC 8`), and moves down past the image.

## Encoders write to a `TextWriter`

Every sequence is text (base64, ASCII escape sequences, or half-block characters), so `TextWriter` is enough, and it is easy to
test with a `StringWriter` and to wrap for tmux. An `IBufferWriter<byte>` would have bought nothing.

## Names

`TerminalImageCapabilities`, not `TerminalGraphicsCapabilities`: XenoAtom.Terminal already has a type of that name, and anyone using
both packages would have to alias one. `CellSize` (cells) and `CellPixelSize` (pixels) are different types on purpose. The project
is `terminal-images` (`terminal-images.nullean.net`), chosen over shorter names for being exactly what it does.

## Spectre: every line ends with a line break

Spectre does not add a trailing line break after `Write(renderable)`; its own widgets end each line themselves. The image widget
does the same, one line break per row, so the next write starts below the image. A test caught this.

## Terminal.Gui: feed its pipeline, do not bypass it

Terminal.Gui 2.5 clips raster images to their view, orders them with text, and repaints them. `TerminalImageView` hands it scaled
pixels and the destination cells, and lets it encode. The cost is that iTerm2 is not available there (it falls back to half blocks);
bypassing the pipeline would fight the screen buffer on every frame. `ApplyDetection` exists because its start-up handshake cannot
see through tmux or SSH.

## The site follows the other Nullean projects

The documentation is a `docs/` folder built by `elastic/docs-builder`, then finished by `scripts/docs-finish.sh` (the theme, the
landing page, the fonts), as in snapshots and curb. The theme is theirs with a pink and mint palette; the brand kit follows their
construction (a gradient tile with a hard two-colour split, a dark fill, a mark, a JetBrains Mono wordmark). The mark is an 8x4-cell
sunset in half blocks: a tiny image drawn the way this library draws images. `tools/brand/make_brand.py` regenerates the kit, and
`tools/landing/build_landing.py` the landing page, with a real `tcat` render and a real `--diagnostics` result in it.

## Provenance

The design draws on the Kitty, iTerm2 and Sixel specifications and on permissively licensed tools (yazi, ratatui-image, viuer,
rasterm and go-termimg under MIT; notcurses under Apache-2.0; term-image and blessed under MIT; Terminal.Gui and Spectre.Console
under MIT). Copyleft tools (chafa, timg) were read through their documentation only. No code was copied.

While scoping this project, the detector, probe and encoder files of `XenoAtom.Terminal.Graphics` (BSD-2-Clause) were read once,
and then no further. The implementation here was written independently and shares no code with it; where behaviour is similar
(the Kitty chunk size, the Sixel header), it follows the protocol specifications. XenoAtom's own source was not read for the
integration: only its public API documentation.

Anything ported from another project must be recorded in `THIRD-PARTY-NOTICES.md` with its licence.
