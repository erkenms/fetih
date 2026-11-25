# Web style

The look of the whole web app: the public site and the admin panel. This sets a direction,
not a pixel spec. Improve on it freely, as long as it stays on this course.

The approved sketch (home, rankings, admin dashboard) is the Design canvas
https://claude.ai/artifact/7ryAXVaeExNZ2WGb64cNCy, copied into [web-style/](web-style/) so
any agent can read it: `main.html`, `rankings.html` and `admin.html`. They're in the canvas's
component format (the markup inside `<x-dc>`, sample data in the script at the end), with
images pointing at `/assets/...` and the textures in `web/src/theme/textures/`. Read them for
the exact borders, gradients and sizes; the React app is built from `web/src/theme/`.

## The direction

Fetih should look like an MMORPG site from around 2010–2015 that kept its style, not like
a site made this year. Good examples:

- İstanbul Kıyamet Vakti (istanbuloyun.com)
- Tibia's site
- The original Fetih Online site before it closed in 2013:
  https://web.archive.org/web/20131029002255/http://fetih.91.com/index/

The game itself is meant to feel a little nostalgic, and the site should match. But keep it
in proportion: it should read as a well-made site of that era, not a parody. That means no
animated GIFs, no scrolling marquees and no visitor counters.

## What makes it that era

- **Layout:** A fixed-width column (about 1000px) centered on a dark textured background.
  The public pages use side columns of boxes around the main content.
- **Header:** A large illustration from the game's art, with the logo on it. Below it, a
  textured menu band with bevelled dividers between the items.
- **Boxes:** Content sits in boxes with a textured title bar (red leather in the sketch) and
  a small game item icon. Box bodies are parchment, with an inner light edge and a soft dark
  shadow inside.
- **Density:** Pages are dense and tight. Expect small gaps, thin borders and lots of
  content per screen.
- **Type:** Body text is small system type (Verdana or Tahoma, 11–12px). Titles use
  Georgia or Trebuchet, bold, with a dark text shadow. A decorative display face (Cinzel)
  is used only for the logo.
- **Buttons:** Glossy and bevelled, with a highlight on the top edge and a split gradient.
  There's a big gold download button and a big red register button, and small red buttons
  for forms.
- **Tables:** Alternating row shades, dotted dividers, bold links, "Devamını oku »" style
  links and "« Önceki 1 2 3 … Sonraki »" paging.
- **Real game art:** Item icons, character faces and launcher illustrations from the 5619
  client (built by `tools/assets`). These carry the look more than any CSS does.
- **Admin panel:** Same materials (title bars, parchment, tables), arranged like the admin
  panel of an old forum or game site. It's fluid width, with a left menu box grouped by
  section.

## What to avoid

These are what make a page look like a modern AI-generated design, and they're what the
first attempt got wrong:

- Wide letter-spaced uppercase labels and headings
- Inter or other modern UI fonts
- Thin gold hairline frames
- Lots of empty space and big rounded cards
- Pill-shaped tabs and buttons
- Glowing "live" dots
- Gradient washes, glassmorphism and large centered hero text with a call to action over
  it
- Slide dots on an image carousel

The app uses Mantine, themed in `web/src/theme/` (`theme.ts`, `theme.module.css`) so its
components follow the rules above; don't let Mantine's defaults show through. A new
component the theme doesn't cover gets the same treatment there. `Panel` (the title bar box)
and `BigButton` are in `web/src/theme/Panel.tsx`, and the layouts in `web/src/app/` put every
page on parchment. The illustrations come from `tools/assets` (`/assets/site/`).

## Starting values from the sketch

| Role | Value |
| --- | --- |
| Page background | `#0e0906`, with a dark noise texture |
| Title bars and menu band | about `#4a1210`–`#6e1b18`, with a leather texture |
| Parchment | about `#d9c393`–`#eddcb5`, with a texture |
| Text on parchment | `#3b2614` |
| Links on parchment | `#7a1a12` |
| Light text on dark | `#f5dfa3` |
| Frame lines | `#000`, with `#4a3019`–`#6b4a26` around them |

The textures in the sketch are noise images made with ImageMagick: fractal plasma mixed
with fine noise, then colorized and mirrored so they tile. Better textures, or ones taken
from the client's own UI art, are welcome.
