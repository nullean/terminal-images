"""Builds docs/terminal-images-landing.html from the template: the animated lockup, the two header icons, a real
`tcat --diagnostics` output and a real half-block render, so the page shows what the library does.

    python3 -I tools/landing/build_landing.py <diagnostics.txt> <half-blocks.ansi>

diagnostics.txt  the plain text `tcat --diagnostics` printed (see tests/pty/probe_test.py for how to script a terminal)
half-blocks.ansi the output of `tcat --protocol blocks --max-columns 64 --max-rows 16 sunset.png`
"""
import html, os, re, subprocess, sys

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
brand = os.path.join(root, "docs", "images", "brand")
diag_path, ansi_path = sys.argv[1:3]


def svg(name, **attrs):
    text = re.sub(r"<\?xml[^>]*>\s*", "", open(os.path.join(brand, name), encoding="utf-8").read()).strip()
    return text


lockup = svg("lockup-animated.svg")
lockup = re.sub(r"<svg\b", '<svg class="nav-lockup" aria-hidden="true"', lockup, count=1)
lockup = lockup.replace('width="356"', 'width="178"', 1).replace('height="96"', 'height="48"', 1)
# the lockup's own width/height come after viewBox: set the displayed size and keep the viewBox
lockup = re.sub(r'(<svg class="nav-lockup"[^>]*?)\s+viewBox="([^"]+)"\s+width="178"\s+height="48"', r'\1 viewBox="\2" width="178" height="48"', lockup)


def colour(line):
    if line.startswith("  - "):
        return f'<span class="t-mut">{html.escape(line)}</span>'
    m = re.match(r"^([a-z ]+:)(\s+)(.*)$", line)
    if not m:
        return html.escape(line)
    key, gap, value = m.groups()
    cls = "t-del" if key == "protocol:" else "t-hi"
    return f'<span class="t-dim">{html.escape(key)}</span>{gap}<span class="{cls}">{html.escape(value)}</span>'


# The .NET runtime writes ESC = (keypad mode) when a terminal is attached; it is not part of the output.
diag_text = re.sub(r"\x1b[=>]", "", open(diag_path, encoding="utf-8").read())
diag = "\n".join(colour(l) for l in diag_text.strip("\n").splitlines())
demo = subprocess.run([sys.executable, "-I", os.path.join(root, "tools", "ansi_to_html.py"), ansi_path], capture_output=True, text=True, check=True).stdout.rstrip("\n")

tpl = open(os.path.join(root, "tools", "landing", "terminal-images-landing.template.html"), encoding="utf-8").read()
out = (tpl.replace("{{LOCKUP}}", lockup).replace("{{DIAG}}", diag).replace("{{DEMO}}", demo)
          .replace("{{ICON_DOCS}}", svg("docs.svg")).replace("{{ICON_GITHUB}}", svg("github.svg")))
assert "{{" not in out, "an unfilled placeholder is left"
open(os.path.join(root, "docs", "terminal-images-landing.html"), "w", encoding="utf-8").write(out)
print("docs/terminal-images-landing.html:", len(out), "bytes")
