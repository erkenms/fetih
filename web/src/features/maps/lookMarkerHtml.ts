import type { LookPicture } from '../../components/looks'
import { assetUrl } from '../../components/pickers'

// NPCs and monsters drawn with their rendered looks. A picture has one model unit to a pixel,
// the client draws roles at ROLE_SCALE of that, and the map's max zoom is one background pixel
// to a screen pixel, so a picture is scaled by ROLE_SCALE * 2^(zoom - maxZoom) through
// --fetih-look-scale on the map container (and by ZoomPercent through --look-zoom). Each marker
// carries its old dot or badge as a fallback, shown when zoomed out too far to make out a
// picture, when the role has no rendered look, or when the picture doesn't load.

// g_fRoleScale in the client
export const ROLE_SCALE = 0.75

// Below this scale a picture is only a few pixels tall
export const FAR_LOOK_SCALE = 0.2

export function lookScale(zoom: number, maxZoom: number): number {
  return ROLE_SCALE * 2 ** (zoom - maxZoom)
}

export function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}

// `below` (a name or a health bar) hangs under the feet, only while the picture shows
export function lookMarkerHtml(
  picture: LookPicture | null,
  fallback: string,
  options: { below?: string; className?: string } = {},
): string {
  const url = picture ? assetUrl(picture.image) : null
  const classes = ['fetih-look', url ? '' : 'fetih-look-none', options.className ?? ''].filter(Boolean).join(' ')
  const img =
    url && picture
      ? `<img class="fetih-look-img" src="${escapeHtml(url)}" alt="" draggable="false" style="left:${-picture.x}px;top:${-picture.y}px;transform-origin:${picture.x}px ${picture.y}px;--look-zoom:${picture.zoom};" />`
      : ''
  const below = url && options.below ? `<div class="fetih-look-below">${options.below}</div>` : ''
  return `<div class="${classes}">${img}<div class="fetih-look-fallback">${fallback}</div>${below}</div>`
}

export function lookNameHtml(name: string): string {
  return `<span class="fetih-look-name">${escapeHtml(name)}</span>`
}
