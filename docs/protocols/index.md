# The protocols

| Protocol | Sent as | Scaling | Transparency | Best for |
|---|---|---|---|---|
| Kitty | Raw RGBA (or PNG) in 4096-byte base64 APC chunks | The terminal scales it to the cells | Yes | Kitty, Ghostty |
| iTerm2 | A PNG file in one OSC 1337 sequence | The terminal scales it to the cells | Yes | iTerm2, WezTerm, Konsole, mintty |
| Sixel | 6-pixel-tall bands, run-length encoded, with a palette | None: drawn at its pixel size | Pixels left undrawn | foot, Windows Terminal, xterm, WezTerm |
| Half blocks | `▀` with a foreground and background colour | None | Terminal background shows | Anything with colour |

## Kitty (`KittyEncoder`)

Sends `a=T,f=32,s=W,v=H` with the pixels base64-encoded in chunks of at most 4096 bytes (`m=1` on all but the last). Responses are
suppressed (`q=2`), so the terminal never writes replies into your input. Options: `Compress` (zlib, `o=z`), `ImageId` (so the
image can be replaced or deleted), and the cell box (`c`, `r`). `EncodePng` sends a PNG file as-is (`f=100`) with no re-encoding.
`KittyEncoder.Delete(id)` frees an image.

## iTerm2 (`ITerm2Encoder`)

`ESC ] 1337 ; File=inline=1;size=N;width=C;height=R;preserveAspectRatio=1 : base64 BEL`. The terminal needs a whole image file,
so the pixels are encoded as a PNG with the package's own encoder. `EncodeFile` passes an existing PNG, JPEG or GIF untouched.

## Sixel (`SixelEncoder`)

A DEC format from the 1980s that is still the widest-supported way to draw pixels.

- **Palette:** `SixelPalette.MedianCut` (default) builds up to `MaxColors` colours from the image; `Rgb332` is a fixed 256-colour
  palette that is faster and stable between frames of an animation.
- **MaxColors:** 2 to 256. The renderer sets it from the terminal's colour register count when the probe found it.
- **Dither:** Floyd–Steinberg error diffusion. Looks better with few colours; costs time.
- **Transparency:** pixels with alpha below 128 are not drawn, and the header asks the terminal to keep its background.
- **No scaling:** resize to the cell area first. `TerminalImageRenderer` does.

## Half blocks (`HalfBlockEncoder`)

Each character cell shows two vertically stacked pixels: the top as foreground, the bottom as background. `ColorDepth` is
`TrueColor` or `Palette256` (the xterm palette). Colour codes are repeated only when the colour changes.

## Cursor handling (`TerminalImageRenderer`)

Terminals disagree about where the cursor ends up after an image. For the graphics protocols the renderer prints newlines to make
room, moves back up, saves the cursor (`ESC 7`), draws, restores it (`ESC 8`), and moves down past the image. Half blocks are plain
text and need none of this.

## Cell sizes

A cell is usually about 10×20 pixels, but it depends on the font. Kitty and iTerm2 scale to a cell box, so a wrong guess only
changes the aspect slightly. Sixel does not scale, so a wrong guess means the image covers more or fewer rows than the cursor
moved. Set `NULLEAN_TERMINAL_CELL_SIZE` or let the probe find it.
