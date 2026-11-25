import { useQuery } from '@tanstack/react-query'
import L from 'leaflet'
import type { Schemas } from '../../api/client'
import { pixelToLatLng, type IsoMeta } from '../../lib/iso'
import { frameAt, isTerrainRole, terrainNpcSprites, type SceneSprite, type TerrainNpcCatalog } from './sceneSprites'

export { isTerrainRole }

// What the map tiles leave out because it moves or comes from the server: the animated cover
// sprites listed in meta.json, and NPCs the client draws as scenery (terrainNpcs.json).

// Tile layer z-indexes: ground 1, walkability 200, elevation 205, covers 250
const TERRAIN_Z = 240
const ANIMATED_COVER_Z = 251

function boundsOf(x: number, y: number, width: number, height: number, meta: IsoMeta): L.LatLngBoundsExpression {
  return [pixelToLatLng(x, y + height, meta), pixelToLatLng(x + width, y, meta)]
}

// Frames are fetched once and swapped from memory: /assets has no cache headers, so swapping
// URLs could ask the server again on every frame
const frameUrls = new Map<string, string>()

function preload(path: string) {
  if (frameUrls.has(path)) return
  frameUrls.set(path, `/assets/${path}`)
  fetch(`/assets/${path}`)
    .then((r) => (r.ok ? r.blob() : null))
    .then((blob) => {
      if (blob) frameUrls.set(path, URL.createObjectURL(blob))
    })
    .catch(() => undefined)
}

const reducedMotion = typeof window !== 'undefined' && typeof window.matchMedia === 'function'
  ? window.matchMedia('(prefers-reduced-motion: reduce)')
  : null

// Image overlays in the tile pane that loop their frames on one shared timer while on a map
export class SpriteLayer extends L.LayerGroup {
  private static live = new Set<SpriteLayer>()
  private static timer: number | null = null
  private sprites: { overlay: L.ImageOverlay; sprite: SceneSprite; shown: number }[] = []
  private playing = true

  constructor(sprites: SceneSprite[], meta: IsoMeta, zIndex: number) {
    super()
    for (const sprite of sprites) {
      if (sprite.frames.length > 1) sprite.frames.forEach(preload)
      const overlay = L.imageOverlay(`/assets/${sprite.frames[0]}`, boundsOf(sprite.x, sprite.y, sprite.width, sprite.height, meta), {
        pane: 'tilePane',
        zIndex,
        interactive: false,
        className: 'fetih-map-sprite',
      })
      this.sprites.push({ overlay, sprite, shown: 0 })
      this.addLayer(overlay)
    }
  }

  get animated(): boolean {
    return this.sprites.some((s) => s.sprite.frames.length > 1)
  }

  setPlaying(playing: boolean): this {
    this.playing = playing
    this.tick(performance.now())
    return this
  }

  onAdd(map: L.Map): this {
    super.onAdd(map)
    if (this.animated) {
      SpriteLayer.live.add(this)
      SpriteLayer.timer ??= window.setInterval(() => {
        const now = performance.now()
        for (const layer of SpriteLayer.live) layer.tick(now)
      }, 50)
    }
    return this
  }

  onRemove(map: L.Map): this {
    SpriteLayer.live.delete(this)
    if (SpriteLayer.live.size === 0 && SpriteLayer.timer != null) {
      window.clearInterval(SpriteLayer.timer)
      SpriteLayer.timer = null
    }
    super.onRemove(map)
    return this
  }

  private tick(now: number) {
    const still = !this.playing || reducedMotion?.matches === true
    for (const s of this.sprites) {
      const frame = still ? 0 : frameAt(now, s.sprite.interval, s.sprite.frames.length)
      if (frame !== s.shown) {
        s.shown = frame
        s.overlay.setUrl(frameUrls.get(s.sprite.frames[frame]) ?? `/assets/${s.sprite.frames[frame]}`)
      }
    }
  }
}

// The covers tiles with the map's animated sprites over them, as one layer to toggle
export function coversLayer(
  meta: Schemas['MapTileMeta'],
  bounds: L.LatLngBoundsExpression,
): { layer: L.LayerGroup; sprites: SpriteLayer } {
  const tiles = L.tileLayer(`/assets/${meta.covers}`, {
    tileSize: meta.tileSize,
    minZoom: 0,
    maxNativeZoom: meta.maxZoom,
    maxZoom: meta.maxZoom + 2,
    noWrap: true,
    bounds,
    zIndex: 250,
  })
  const sprites = new SpriteLayer((meta.animations ?? []) as SceneSprite[], meta, ANIMATED_COVER_Z)
  return { layer: L.layerGroup([tiles, sprites]), sprites }
}

// The scenery of every terrain NPC in `npcs`
export function terrainNpcLayer(
  catalog: TerrainNpcCatalog | null | undefined,
  npcs: readonly { lookface: number; role: number; x: number; y: number }[],
  meta: IsoMeta,
): SpriteLayer {
  const sprites = catalog
    ? npcs.filter((n) => isTerrainRole(n.role)).flatMap((n) => terrainNpcSprites(catalog, n.lookface, n.x, n.y, meta))
    : []
  return new SpriteLayer(sprites, meta, TERRAIN_Z)
}

export function useTerrainNpcs() {
  return useQuery({
    queryKey: ['assets', 'terrainNpcs'],
    queryFn: async (): Promise<TerrainNpcCatalog | null> => {
      const response = await fetch('/assets/terrainNpcs.json')
      if (!response.ok) return null
      try {
        return (await response.json()) as TerrainNpcCatalog
      } catch {
        return null
      }
    },
    staleTime: Infinity,
  })
}
