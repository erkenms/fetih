# Web style

An MMORPG site from 2010–2015 that kept its style. Well made, not a parody.
References: İstanbul Kıyamet Vakti, Tibia's site, the old
[Fetih Online](https://web.archive.org/web/20131029002255/http://fetih.91.com/index/).

## Do

- Fixed ~1000px column on a dark textured background; side columns of boxes.
- Big game illustration header with the logo; textured menu band, bevelled dividers.
- Boxes: leather title bar with a small item icon, parchment body.
- Dense: small gaps, thin borders, lots per screen.
- Verdana/Tahoma 11–12px body; Georgia/Trebuchet bold titles with shadow; Cinzel only for the logo.
- Glossy bevelled buttons: big gold (download), big red (register), small red (forms).
- Tables: alternating rows, dotted dividers, "Devamını oku »", "« Önceki 1 2 3 Sonraki »".
- Real game art (item icons, faces, illustrations from `tools/assets`) over CSS.
- Admin: same materials, fluid width, left menu grouped by section.

## Don't

Letter-spaced uppercase, Inter or modern UI fonts, thin gold hairlines, big empty space,
rounded cards, pills, glowing dots, gradient washes, glass, hero text with a call to action,
carousel dots, animated GIFs, marquees.

## Colors

| Role | Value |
|---|---|
| Background | `#0e0906` + noise |
| Title bars, menu | `#4a1210`–`#6e1b18` + leather |
| Parchment | `#d9c393`–`#eddcb5` |
| Text / links on parchment | `#3b2614` / `#7a1a12` |
| Light text on dark | `#f5dfa3` |
| Frames | `#000`, with `#4a3019`–`#6b4a26` |

Textures are generated noise; better ones from the client's UI art are welcome.

Implementation: `src/Fetih.Web/wwwroot/css/site.css`, `src/Fetih.Web/TagHelpers/`, and [web.md](web.md).
