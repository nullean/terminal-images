"""Builds the terminal-images brand kit in docs/images/brand: SVG (static, animated, light) and the PNGs rendered from them.

The construction follows the other Nullean projects (snapshots, curb, cdp): a gradient-bordered tile with a hard two-colour
split, a dark radial fill, a mark, and a JetBrains Mono wordmark with accent characters. The mark is a tiny image drawn the way
this library draws images: an 8x4-cell sunset in half blocks (two pixels per cell).

    python3 -I tools/brand/make_brand.py            writes the SVGs, renders PNGs with headless Chrome (macOS)
    python3 -I tools/brand/make_brand.py --svg-only writes the SVGs only

PNG rendering needs Chrome and the fonts JetBrains Mono (installed) and Space Grotesk (docs/fonts, from the docs theme).
"""
import os, shutil, subprocess, sys, tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "docs", "images", "brand")
FONT_SG = os.path.join(ROOT, "docs", "fonts", "space-grotesk-latin-wght-normal.woff2")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

PINK, MINT = "#ff7ab8", "#5ee6c6"            # the tile border, on dark
PINK_L, MINT_L = "#e2398f", "#12a583"        # the same, darker, for light pages
MONO = "JetBrains Mono, ui-monospace, SFMono-Regular, Menlo, monospace"

# The mark: an 8x8 pixel sunset (so 8 columns by 4 terminal cells), one letter per pixel.
PALETTE = {"V": "#3a2a94", "P": "#7a45c4", "M": "#d2489b", "O": "#ff7a59", "Y": "#ffd96b", "A": "#ffb03d", "S": "#14224f", "R": "#2b3f86"}
SUNSET = ["VVVVVVVV", "PPPPPPPP", "MMYYYYMM", "OYYYYYYO", "OYYYYYYO", "SRAAAARS", "SSRAARSS", "SSSRRSSS"]
SUNSET_SMALL = ["VVVV", "MYYM", "OYYO", "SAAS"]  # for favicons: bigger pixels survive 32px


def glyph(rows, size=64, sun_class=None):
    """The pixel art as rects, filling a size x size square. Adjacent runs of one colour merge into one rect."""
    n = len(rows)
    px = size / n
    out = []
    for y, row in enumerate(rows):
        x = 0
        while x < len(row):
            c = row[x]
            run = 1
            while x + run < len(row) and row[x + run] == c:
                run += 1
            cls = f' class="{sun_class}"' if sun_class and c in "YA" else ""
            out.append(f'<rect{cls} x="{x * px:g}" y="{y * px:g}" width="{run * px + 0.02:g}" height="{px + 0.02:g}" fill="{PALETTE[c]}"/>')
            x += run
    return "".join(out)


def defs(p, pink, mint, width, animated):
    sheen = (
        f'<linearGradient id="{p}s" x1="0" y1="0" x2="1" y2="0"><stop offset=".35" stop-color="#fff" stop-opacity="0"/>'
        f'<stop offset=".5" stop-color="#fff" stop-opacity=".8"/><stop offset=".65" stop-color="#fff" stop-opacity="0"/></linearGradient>'
        f'<mask id="{p}m"><rect width="{width}" height="96" fill="#fff"/><rect x="5" y="5" width="{width - 10}" height="86" fill="#000"/></mask>'
    )
    return (
        f'<defs><linearGradient id="{p}f" x1="0" y1=".2" x2="1" y2=".8"><stop offset=".44" stop-color="{pink}"/><stop offset=".56" stop-color="{mint}"/></linearGradient>'
        f'<radialGradient id="{p}g" cx=".18" cy=".12" r="1.2"><stop offset="0" stop-color="#1b2126"/><stop offset=".55" stop-color="#0d1013"/><stop offset="1" stop-color="#08090b"/></radialGradient>'
        f"{sheen}</defs>"
    )


def style(p, width, animated):
    base = f".{p}sh{{animation:{p}sw 4.6s ease-in-out infinite}}@keyframes {p}sw{{0%,64%{{transform:translateX(0)}}92%,100%{{transform:translateX({width * 1.4:g}px)}}}}"
    if animated:
        base += f".{p}sun{{animation:{p}pu 4.6s ease-in-out infinite}}@keyframes {p}pu{{0%,60%,100%{{opacity:1}}78%{{opacity:.72}}}}.{p}cur{{animation:{p}bl 1.1s steps(1) infinite}}@keyframes {p}bl{{50%{{opacity:0}}}}"
    return f"<style>{base}</style>"


def tile(p, width, animated):
    t = f'<rect width="{width}" height="96" rx="10" fill="url(#{p}f)"/>'
    if animated:
        t += f'<g mask="url(#{p}m)"><rect class="{p}sh" x="-120" y="-20" width="120" height="136" fill="url(#{p}s)" transform="skewX(-18)"/></g>'
    return t + f'<rect x="5" y="5" width="{width - 10}" height="86" rx="5" fill="url(#{p}g)"/>'


def mark(kind, small=False):
    animated, light = kind == "animated", kind == "light"
    p = {"static": "t1", "animated": "t2", "light": "t3"}[kind] + ("s" if small else "")
    pink, mint = (PINK_L, MINT_L) if light else (PINK, MINT)
    art = glyph(SUNSET_SMALL if small else SUNSET, 64, f"{p}sun" if animated else None)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" width="96" height="96" role="img" aria-label="terminal-images">'
        f"{style(p, 96, animated)}{defs(p, pink, mint, 96, animated)}{tile(p, 96, animated)}"
        f'<g transform="translate(16 16)">{art}</g></svg>'
    )


def lockup(kind):
    animated, light = kind == "animated", kind == "light"
    p = {"static": "t4", "animated": "t5", "light": "t6"}[kind]
    pink, mint = (PINK_L, MINT_L) if light else (PINK, MINT)
    width = 356
    art = glyph(SUNSET, 64, f"{p}sun" if animated else None)
    cursor = f'<tspan class="{p}cur" fill="{mint}">_</tspan>' if animated else f'<tspan fill="{mint}">_</tspan>'
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} 96" width="{width}" height="96" role="img" aria-label="terminal-images">'
        f"{style(p, width, animated)}{defs(p, pink, mint, width, animated)}{tile(p, width, animated)}"
        f'<g transform="translate(14 16)">{art}</g>'
        f'<g font-family="{MONO}" font-weight="700"><text x="92" y="58" font-size="26" letter-spacing="-1" fill="#ffffff">terminal<tspan fill="{pink}">-</tspan>images{cursor}</text></g></svg>'
    )


def social_card():
    art = glyph(SUNSET, 64)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 640" width="1280" height="640" role="img" aria-label="Nullean.TerminalImages">'
        f'<defs><radialGradient id="p1" cx=".08" cy=".18" r=".55"><stop offset="0" stop-color="{PINK}" stop-opacity=".2"/><stop offset="1" stop-color="{PINK}" stop-opacity="0"/></radialGradient>'
        f'<radialGradient id="p2" cx=".92" cy=".06" r=".5"><stop offset="0" stop-color="{MINT}" stop-opacity=".2"/><stop offset="1" stop-color="{MINT}" stop-opacity="0"/></radialGradient>'
        f'<radialGradient id="p3" cx=".6" cy=".8" r=".55"><stop offset="0" stop-color="{PINK}" stop-opacity=".14"/><stop offset="1" stop-color="{PINK}" stop-opacity="0"/></radialGradient></defs>'
        '<rect width="1280" height="640" fill="#08090b"/><rect width="1280" height="640" fill="url(#p1)"/><rect width="1280" height="640" fill="url(#p2)"/><rect width="1280" height="640" fill="url(#p3)"/>'
        f'<g opacity=".16" transform="translate(862 24) scale(6.4)">{art}</g>'
        f'<g font-family="{MONO}" font-weight="700"><text x="80" y="268" font-size="86" letter-spacing="-3" fill="#ffffff">terminal<tspan fill="{PINK}">-</tspan>images<tspan fill="{MINT}">_</tspan></text></g>'
        '<g font-family="Space Grotesk, Inter, sans-serif" font-weight="700"><text x="80" y="352" font-size="48" letter-spacing="-1" fill="#f4f6f9">Images in the terminal. No dependencies.</text></g>'
        f'<g font-family="{MONO}" font-weight="400" font-size="26" fill="#98a1ae"><text x="80" y="428">Kitty · iTerm2 · Sixel · half blocks</text>'
        f'<text x="80" y="560" fill="{MINT}">nuget</text><text x="164" y="560" fill="#e9ecf1">Nullean.TerminalImages</text></g></svg>'
    )


def write(name, text):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(text + "\n")


def render(svg_name, png_name, width, height, scale=1.0, font_face=True):
    """SVG to PNG with headless Chrome (transparent background), then crop to the exact size. Chrome's minimum window size means
    the picture is centred in a larger window and the centre is cropped."""
    svg = open(os.path.join(OUT, svg_name), encoding="utf-8").read()
    svg = svg.replace('width="', 'data-w="', 1).replace('height="', 'data-h="', 1)
    svg = svg.replace("<svg ", f'<svg width="{width * scale:g}" height="{height * scale:g}" ', 1)
    face = f"@font-face{{font-family:'Space Grotesk';font-weight:300 700;src:url('file://{FONT_SG}') format('woff2')}}" if font_face else ""
    html = f"<!doctype html><meta charset=utf-8><style>{face}html,body{{height:100%;margin:0;background:transparent}}body{{display:grid;place-items:center}}svg{{display:block}}</style>{svg}"
    with tempfile.TemporaryDirectory() as tmp:
        page, shot = os.path.join(tmp, "p.html"), os.path.join(tmp, "s.png")
        open(page, "w", encoding="utf-8").write(html)
        subprocess.run(
            [CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
             "--window-size=1400,900", f"--screenshot={shot}", f"file://{page}"],
            check=True, capture_output=True, timeout=60)
        dest = os.path.join(OUT, "png", png_name)
        shutil.copy(shot, dest)
        # sips crops from the centre, which is where the picture is.
        subprocess.run(["sips", "--cropToHeightWidth", str(int(height * scale)), str(int(width * scale)), dest], check=True, capture_output=True)


def main():
    os.makedirs(os.path.join(OUT, "png"), exist_ok=True)
    for kind, suffix in (("static", ""), ("light", "-light"), ("animated", "-animated")):
        write(f"mark{suffix}.svg", mark(kind))
        write(f"lockup{suffix}.svg", lockup(kind))
    write("mark-small.svg", mark("static", small=True))
    write("social-card.svg", social_card())
    print("svg: written to", OUT)
    if "--svg-only" in sys.argv:
        return
    render("mark.svg", "mark-512.png", 96, 96, scale=512 / 96)
    render("mark.svg", "mark-180.png", 96, 96, scale=180 / 96)
    render("mark-small.svg", "mark-32.png", 96, 96, scale=32 / 96)
    render("lockup.svg", "lockup.png", 356, 96, scale=2)
    render("lockup-light.svg", "lockup-light.png", 356, 96, scale=2)
    render("social-card.svg", "social-card.png", 1280, 640)
    print("png: rendered to", os.path.join(OUT, "png"))


if __name__ == "__main__":
    main()
