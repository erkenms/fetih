import L from 'leaflet'
import type { LookPicture } from '../../components/looks'
import { FAR_LOOK_SCALE, lookMarkerHtml, lookScale } from './lookMarkerHtml'
import './lookMarkers.css'

export { escapeHtml, lookNameHtml } from './lookMarkerHtml'

export function lookIcon(
  picture: LookPicture | null,
  fallback: string,
  options: { below?: string; className?: string } = {},
): L.DivIcon {
  return L.divIcon({
    className: 'fetih-look-marker',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: lookMarkerHtml(picture, fallback, options),
  })
}

// Keeps the pictures of `lmap` sized to its zoom, and swaps a picture that fails to load for its
// fallback. Call once the map has a view.
export function trackLookScale(lmap: L.Map, maxZoom: number): void {
  const container = lmap.getContainer()
  const apply = (zoom: number) => {
    if (!Number.isFinite(zoom)) return
    const scale = lookScale(zoom, maxZoom)
    container.style.setProperty('--fetih-look-scale', String(scale))
    container.classList.toggle('fetih-looks-far', scale < FAR_LOOK_SCALE)
  }
  const onError = (e: Event) => {
    const target = e.target
    if (target instanceof HTMLImageElement && target.classList.contains('fetih-look-img'))
      target.closest('.fetih-look')?.classList.add('fetih-look-failed')
  }
  lmap.on('zoomanim', (e) => apply((e as L.ZoomAnimEvent).zoom))
  lmap.on('zoom', () => apply(lmap.getZoom()))
  container.addEventListener('error', onError, true)
  lmap.on('unload', () => {
    container.removeEventListener('error', onError, true)
    container.classList.remove('fetih-looks-far')
  })
  apply(lmap.getZoom())
}
