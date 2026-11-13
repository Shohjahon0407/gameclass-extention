#!/usr/bin/env python3
"""Renders privacy.html for the download page from the installer's own terms texts (one source of truth).

Usage: make-privacy.py <output.html>
"""
import html
import re
import sys
from pathlib import Path

TEXTS = Path(__file__).resolve().parents[1] / "installer" / "text"
LANGS = [("uz", "O'zbekcha"), ("ru", "Русский"), ("en", "English")]


def render(text):
    lines = text.strip().splitlines()
    out = [f"<h1>{html.escape(lines[0])}</h1>", f"<p class=\"muted\">{html.escape(lines[1])}</p>"]
    items = []

    def flush():
        if items:
            out.append("<ul>" + "".join(f"<li>{html.escape(i)}</li>" for i in items) + "</ul>")
            items.clear()

    for line in lines[2:]:
        line = line.strip()
        if not line:
            flush()
        elif re.match(r"^\d+\.\s+[^a-zа-я]+$", line):
            flush()
            out.append(f"<h2>{html.escape(line)}</h2>")
        elif line.startswith("- "):
            items.append(line[2:])
        else:
            flush()
            escaped = html.escape(line)
            escaped = re.sub(r"(https://[^\s]+)", r'<a href="\1">\1</a>', escaped)
            out.append(f"<p>{escaped}</p>")
    flush()
    return "\n".join(out)


def main():
    sections = []
    for code, _ in LANGS:
        text = (TEXTS / f"terms-{code}.txt").read_text(encoding="utf-8-sig")
        sections.append(f'<article lang="{code}" data-lang="{code}">{render(text)}</article>')
    buttons = "".join(f'<button data-set-lang="{c}">{c.upper()}</button>' for c, _ in LANGS)
    page = f"""<!doctype html>
<html lang="uz">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Manajoy GameClass — shartlar va maxfiylik</title>
<link rel="icon" type="image/png" href="favicon.png">
<style>
:root {{ --primary:#1b54e4; --text:#15181b; --text-2:#4a5560; --muted:#7b8794; --border:#e6e9ec; --bg:#f5f7f8; }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--text); font-family:"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,Roboto,sans-serif; line-height:1.6; }}
.bar {{ background:linear-gradient(150deg,#7a2be2 0%,#4324d6 26%,#1b54e4 58%,#0ea5f0 100%); color:#fff; }}
.bar .wrap {{ display:flex; align-items:center; gap:12px; padding:16px 20px; }}
.bar a {{ color:#fff; text-decoration:none; font-weight:800; }}
.bar img {{ width:34px; height:34px; }}
.spacer {{ flex:1; }}
.langs {{ display:inline-flex; padding:3px; border-radius:999px; background:rgba(0,0,0,.18); }}
.langs button {{ border:0; background:transparent; color:rgba(255,255,255,.85); font:inherit; font-weight:700; font-size:13px; padding:5px 12px; border-radius:999px; cursor:pointer; }}
.langs button.on {{ background:#fff; color:var(--primary); }}
.wrap {{ max-width:820px; margin:0 auto; }}
main {{ padding:28px 20px 60px; }}
article {{ background:#fff; border:1px solid var(--border); border-radius:20px; padding:28px; }}
article[hidden] {{ display:none; }}
h1 {{ font-size:26px; line-height:1.25; margin:0 0 6px; }}
h2 {{ font-size:17px; margin:24px 0 6px; }}
p, li {{ color:var(--text-2); }}
.muted {{ color:var(--muted); margin-top:0; }}
a {{ color:var(--primary); }}
</style>
</head>
<body>
<div class="bar"><div class="wrap"><img src="logo-white.png" alt=""><a href="./">Manajoy GameClass</a><span class="spacer"></span><div class="langs">{buttons}</div></div></div>
<main class="wrap">
{''.join(sections)}
</main>
<script>
(function () {{
  function apply(lang) {{
    document.querySelectorAll('article').forEach(function (a) {{ a.hidden = a.dataset.lang !== lang; }});
    document.querySelectorAll('[data-set-lang]').forEach(function (b) {{ b.classList.toggle('on', b.dataset.setLang === lang); }});
    document.documentElement.lang = lang;
    try {{ localStorage.setItem('mj.lang', lang); }} catch (e) {{}}
  }}
  var saved = null;
  try {{ saved = localStorage.getItem('mj.lang'); }} catch (e) {{}}
  var b = (navigator.language || 'uz').slice(0, 2);
  apply(saved || (b === 'ru' ? 'ru' : b === 'en' ? 'en' : 'uz'));
  document.querySelectorAll('[data-set-lang]').forEach(function (x) {{ x.addEventListener('click', function () {{ apply(x.dataset.setLang); }}); }});
}})();
</script>
</body>
</html>
"""
    Path(sys.argv[1]).write_text(page, encoding="utf-8")


if __name__ == "__main__":
    main()
