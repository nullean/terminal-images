"""Turns tcat's half-block output (24-bit SGR colours and the upper half block) into HTML spans for the landing page.

Usage: python3 -I tools/ansi_to_html.py <file.ansi>   prints a <pre>-ready HTML fragment to stdout.
"""
import html, re, sys

text = open(sys.argv[1], encoding="utf-8").read()
out, fg, bg, open_span = [], None, None, False
token = re.compile(r"\x1b\[([0-9;]*)m")
pos = 0


def span_style():
    parts = []
    if fg:
        parts.append(f"color:#{fg}")
    if bg:
        parts.append(f"background:#{bg}")
    return ";".join(parts)


def emit(chunk):
    global open_span
    if not chunk:
        return
    style = span_style()
    esc = html.escape(chunk)
    out.append(f'<span style="{style}">{esc}</span>' if style else esc)


for m in token.finditer(text):
    emit(text[pos:m.start()])
    pos = m.end()
    codes = [int(c) if c else 0 for c in m.group(1).split(";")]
    i = 0
    while i < len(codes):
        c = codes[i]
        if c == 0:
            fg = bg = None
        elif c == 39:
            fg = None
        elif c == 49:
            bg = None
        elif c in (38, 48) and i + 4 < len(codes) + 0 and codes[i + 1] == 2:
            colour = "%02x%02x%02x" % (codes[i + 2], codes[i + 3], codes[i + 4])
            if c == 38:
                fg = colour
            else:
                bg = colour
            i += 4
        i += 1
emit(text[pos:])
sys.stdout.write("".join(out))
