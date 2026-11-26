// Interactive Leaflet map for /admin/maps/{id}: tiles, grid overlays, look sprites,
// drag-to-move, click-to-place, and htmx side-panel sync.
(() => {
  const dataEl = document.getElementById('map-data')
  const canvasEl = document.getElementById('map-canvas')
  const panelEl = document.getElementById('map-panel')
  const statusEl = document.getElementById('map-status')
  if (!dataEl || !canvasEl || !panelEl || typeof L === 'undefined') return

  const ROLE_SCALE = 0.75
  const FAR_LOOK_SCALE = 0.2
  const TERRAIN_ROLES = new Set([25, 26, 39, 41, 42, 102, 103])

  let state = JSON.parse(dataEl.textContent || '{}')
  let selected = null // { type: 'npc'|'goal'|'spawn'|'portal', id: number }
  let placeMode = null // 'npc' | 'spawn' | 'portal' | null
  let decodedGrid = null
  let gridPromise = null
  let terrainCatalog = null

  // --- Isometric projection ---

  function fallbackMeta(mapW, mapH, tileSize = 256) {
    const w = Math.max(1, Math.round(mapW || 500))
    const h = Math.max(1, Math.round(mapH || 500))
    const width = 32 * (w + h)
    const height = 16 * (w + h) + 32
    let maxZoom = 0
    while ((tileSize << maxZoom) < Math.max(width, height)) maxZoom++
    return {
      width, height, tileSize, maxZoom,
      cell: { width: 64, height: 32 },
      mapSize: { width: w, height: h },
      origin: { x: 32 * h, y: 16 },
      background: { x: 0, y: 0 },
    }
  }

  const meta = state.meta || fallbackMeta(state.width, state.height)
  const crsScale = 2 ** meta.maxZoom

  function cellToPixel(x, y) {
    const hw = meta.cell.width / 2
    const hh = meta.cell.height / 2
    return {
      px: hw * (x - y) + meta.origin.x - meta.background.x,
      py: hh * (x + y) + meta.origin.y - meta.background.y,
    }
  }

  function pixelToCellInt(px, py) {
    const hw = meta.cell.width / 2
    const hh = meta.cell.height / 2
    const dx = (px - (meta.origin.x - meta.background.x)) / hw
    const dy = (py - (meta.origin.y - meta.background.y)) / hh
    return { x: Math.round((dx + dy) / 2), y: Math.round((dy - dx) / 2) }
  }

  function pixelToLatLng(px, py) { return [-py / crsScale, px / crsScale] }
  function latLngToPixel(lat, lng) { return { px: lng * crsScale, py: -lat * crsScale } }

  function cellToLatLng(x, y) {
    const { px, py } = cellToPixel(x, y)
    return pixelToLatLng(px, py)
  }

  function latLngToCellInt(lat, lng) {
    const { px, py } = latLngToPixel(lat, lng)
    return pixelToCellInt(px, py)
  }

  function spawnCircleLatLng(x, y, spread, segments = 24) {
    const r = Math.max(0.5, spread)
    const pts = []
    for (let i = 0; i < segments; i++) {
      const a = (i / segments) * Math.PI * 2
      pts.push(cellToLatLng(x + r * Math.cos(a), y + r * Math.sin(a)))
    }
    return pts
  }

  const bounds = [[-meta.height / crsScale, 0], [0, meta.width / crsScale]]

  // --- Grid decoding & canvas layer ---

  function decodeBase64(b64) {
    const bin = atob(b64)
    const out = new Uint8Array(bin.length)
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i)
    return out
  }

  function isCellWalkable(grid, x, y) {
    if (x < 0 || y < 0 || x >= grid.width || y >= grid.height) return false
    const idx = y * grid.width + x
    return ((grid.walkable[idx >> 3] || 0) & (1 << (idx & 7))) !== 0
  }

  function cellElevation(grid, x, y) {
    if (x < 0 || y < 0 || x >= grid.width || y >= grid.height) return null
    const raw = grid.elevation[y * grid.width + x] || 0
    if (grid.maxElevation <= grid.minElevation) return grid.minElevation
    return Math.round(grid.minElevation + (raw / 255) * (grid.maxElevation - grid.minElevation))
  }

  function elevationColor(v) {
    const t = Math.max(0, Math.min(255, v)) / 255
    if (t < 0.25) return [30, Math.round(80 + 175 * (t / 0.25)), 220]
    if (t < 0.5) return [Math.round(30 + 20 * ((t - 0.25) / 0.25)), 220, Math.round(220 - 160 * ((t - 0.25) / 0.25))]
    if (t < 0.75) return [Math.round(50 + 200 * ((t - 0.5) / 0.25)), 220, 60]
    return [245, Math.round(220 - 170 * ((t - 0.75) / 0.25)), 45]
  }

  const GridCanvasLayer = L.GridLayer.extend({
    initialize(grid, mode, options) {
      this._grid = grid
      this._mode = mode
      L.setOptions(this, Object.assign({ tileSize: meta.tileSize, noWrap: true }, options))
    },
    createTile(coords) {
      const size = this.getTileSize()
      const canvas = document.createElement('canvas')
      canvas.width = size.x
      canvas.height = size.y
      const ctx = canvas.getContext('2d')
      if (!ctx) return canvas

      const scale = 2 ** (meta.maxZoom - coords.z)
      const ox = coords.x * size.x * scale
      const oy = coords.y * size.y * scale
      if (ox >= meta.width || oy >= meta.height || ox + size.x * scale <= 0 || oy + size.y * scale <= 0)
        return canvas

      const step = scale > 4 ? 2 : 1
      const img = ctx.createImageData(size.x, size.y)
      const pxData = img.data
      const g = this._grid

      for (let ty = 0; ty < size.y; ty += step) {
        const py = oy + (ty + step * 0.5) * scale
        for (let tx = 0; tx < size.x; tx += step) {
          const px = ox + (tx + step * 0.5) * scale
          const c = pixelToCellInt(px, py)
          if (c.x < 0 || c.y < 0 || c.x >= g.width || c.y >= g.height) continue

          let r, gr, b, a
          if (this._mode === 'walkability') {
            if (isCellWalkable(g, c.x, c.y)) { r = 34; gr = 197; b = 94; a = 45 }
            else { r = 239; gr = 68; b = 68; a = 115 }
          } else {
            ;[r, gr, b] = elevationColor(g.elevation[c.y * g.width + c.x] || 0)
            a = 135
          }

          for (let dy = 0; dy < step && ty + dy < size.y; dy++) {
            for (let dx = 0; dx < step && tx + dx < size.x; dx++) {
              const off = ((ty + dy) * size.x + (tx + dx)) * 4
              pxData[off] = r; pxData[off + 1] = gr; pxData[off + 2] = b; pxData[off + 3] = a
            }
          }
        }
      }

      ctx.putImageData(img, 0, 0)
      return canvas
    },
  })

  function ensureGrid() {
    if (decodedGrid) return Promise.resolve(decodedGrid)
    if (!state.hasGrid || !state.urls?.grid) return Promise.resolve(null)
    gridPromise ??= fetch(state.urls.grid)
      .then((r) => (r.ok ? r.json() : null))
      .then((raw) => {
        if (!raw) return null
        decodedGrid = {
          width: raw.width,
          height: raw.height,
          walkable: decodeBase64(raw.walkableBase64),
          elevation: decodeBase64(raw.elevationBase64),
          minElevation: raw.minElevation,
          maxElevation: raw.maxElevation,
        }
        return decodedGrid
      })
      .catch(() => null)
    return gridPromise
  }

  // --- HTML helpers for markers ---

  function esc(text) {
    return String(text ?? '')
      .replaceAll('&', '&amp;').replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;').replaceAll('"', '&quot;')
  }

  function badgeHtml(label, color, isSelected) {
    return `<div class="map-badge${isSelected ? ' selected' : ''}" style="--badge-color:${color}"><span class="dot"></span><span>${esc(label)}</span></div>`
  }

  function lookIcon(look, fallbackHtml, belowText, isSelected) {
    const imgUrl = look?.image ? `/assets/${look.image}` : null
    const cls = ['fetih-look', imgUrl ? '' : 'fetih-look-none', isSelected ? 'fetih-look-selected' : '']
      .filter(Boolean).join(' ')
    const img = imgUrl
      ? `<img class="fetih-look-img" src="${esc(imgUrl)}" alt="" draggable="false" style="left:${-look.x}px;top:${-look.y}px;transform-origin:${look.x}px ${look.y}px;--look-zoom:${look.zoom || 1};">`
      : ''
    const below = imgUrl && belowText
      ? `<div class="fetih-look-below"><span class="fetih-look-name">${esc(belowText)}</span></div>`
      : ''
    return L.divIcon({
      className: 'fetih-look-icon',
      iconSize: [0, 0],
      iconAnchor: [0, 0],
      html: `<div class="${cls}">${img}<div class="fetih-look-fallback">${fallbackHtml}</div>${below}</div>`,
    })
  }

  function badgeIcon(label, color, isSelected) {
    return L.divIcon({
      className: 'fetih-map-badge',
      iconSize: [0, 0],
      iconAnchor: [0, 0],
      html: badgeHtml(label, color, isSelected),
    })
  }

  // --- Animated sprites (covers + terrain NPCs) ---

  const frameCache = new Map()
  const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)')

  function preloadFrame(path) {
    if (frameCache.has(path)) return
    frameCache.set(path, `/assets/${path}`)
    fetch(`/assets/${path}`)
      .then((r) => (r.ok ? r.blob() : null))
      .then((blob) => { if (blob) frameCache.set(path, URL.createObjectURL(blob)) })
      .catch(() => undefined)
  }

  function buildSpriteGroup(sprites, zIndex) {
    const group = L.layerGroup()
    const entries = []
    for (const s of sprites) {
      if (!s.frames || s.frames.length === 0) continue
      if (s.frames.length > 1) s.frames.forEach(preloadFrame)
      const b = [pixelToLatLng(s.x, s.y + s.height), pixelToLatLng(s.x + s.width, s.y)]
      const overlay = L.imageOverlay(`/assets/${s.frames[0]}`, b, {
        pane: 'tilePane', zIndex, interactive: false, className: 'fetih-map-sprite',
      })
      entries.push({ overlay, sprite: s, shown: 0 })
      group.addLayer(overlay)
    }
    group._spriteEntries = entries
    return group
  }

  // --- Leaflet map creation ---

  const map = L.map(canvasEl, {
    crs: L.CRS.Simple,
    minZoom: 0,
    maxZoom: meta.maxZoom + 2,
    zoomSnap: 0.5,
    attributionControl: false,
  })

  if (meta.ground) {
    L.tileLayer(`/assets/${meta.ground}`, {
      tileSize: meta.tileSize, minZoom: 0, maxNativeZoom: meta.maxZoom,
      maxZoom: meta.maxZoom + 2, noWrap: true, bounds, zIndex: 1,
    }).addTo(map)
  }

  const coversGroup = L.layerGroup()
  if (meta.covers) {
    L.tileLayer(`/assets/${meta.covers}`, {
      tileSize: meta.tileSize, minZoom: 0, maxNativeZoom: meta.maxZoom,
      maxZoom: meta.maxZoom + 2, noWrap: true, bounds, zIndex: 250,
    }).addTo(coversGroup)
  }
  const animatedCoverSprites = buildSpriteGroup(meta.animations || [], 251)
  animatedCoverSprites.addTo(coversGroup)
  coversGroup.addTo(map)

  const terrainNpcGroup = L.layerGroup().addTo(map)
  const npcsLayer = L.layerGroup().addTo(map)
  const spawnsLayer = L.layerGroup().addTo(map)
  const portalsLayer = L.layerGroup().addTo(map)
  let walkLayer = null
  let elevLayer = null

  map.fitBounds(bounds)

  // Scale look sprites with zoom
  function updateLookScale() {
    const scale = ROLE_SCALE * (2 ** (map.getZoom() - meta.maxZoom))
    canvasEl.style.setProperty('--fetih-look-scale', String(scale))
    canvasEl.classList.toggle('fetih-looks-far', scale < FAR_LOOK_SCALE)
  }
  map.on('zoomend zoom', updateLookScale)
  updateLookScale()

  // Fallback when a look sprite image fails to load
  canvasEl.addEventListener('error', (e) => {
    const img = e.target
    if (img instanceof HTMLImageElement && img.classList.contains('fetih-look-img')) {
      img.closest('.fetih-look')?.classList.add('fetih-look-failed')
    }
  }, true)

  // Animate cover/terrain sprites
  window.setInterval(() => {
    if (reducedMotion?.matches) return
    const now = performance.now()
    for (const group of [animatedCoverSprites, terrainNpcGroup]) {
      for (const item of group._spriteEntries || []) {
        const len = item.sprite.frames.length
        if (len <= 1 || !item.sprite.interval) continue
        const frame = Math.floor(now / item.sprite.interval) % len
        if (frame !== item.shown) {
          item.shown = frame
          const path = item.sprite.frames[frame]
          item.overlay.setUrl(frameCache.get(path) || `/assets/${path}`)
        }
      }
    }
  }, 50)

  // --- Anti-forgery & panel loading ---

  function antiForgeryToken() {
    return document.querySelector('#map-antiforgery input[name="__RequestVerificationToken"]')?.value || ''
  }

  function loadPanel(type, entityId, extra = {}) {
    const params = new URLSearchParams()
    if (type) params.set('type', type)
    if (entityId != null) params.set('entityId', String(entityId))
    for (const [k, v] of Object.entries(extra)) {
      if (v != null) params.set(k, String(v))
    }
    const sep = state.urls.panel.includes('?') ? '&' : '?'
    const url = params.toString() ? `${state.urls.panel}${sep}${params}` : state.urls.panel
    if (window.htmx) {
      window.htmx.ajax('GET', url, { target: '#map-panel', swap: 'innerHTML' })
    }
  }

  async function postMove(kind, entityId, x, y) {
    const body = new URLSearchParams({
      kind,
      entityId: String(entityId ?? 0),
      x: String(x),
      y: String(y),
      __RequestVerificationToken: antiForgeryToken(),
    })
    const resp = await fetch(state.urls.move, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body,
    })
    if (!resp.ok) {
      return { saved: false, error: resp.status === 403 ? 'Düzenleme yetkin yok.' : `Sunucu hatası (${resp.status}).` }
    }
    return await resp.json()
  }

  function firstProblemText(result) {
    if (result.error) return result.error
    const err = (result.problems || []).find((p) => p.level === 'error')
    return err ? err.message : 'Kayıt doğrulanamadı.'
  }

  // --- Marker rendering ---

  function renderTerrainNpcs() {
    terrainNpcGroup.clearLayers()
    terrainNpcGroup._spriteEntries = []
    if (!terrainCatalog) return
    const sprites = []
    for (const n of state.npcs || []) {
      if (!TERRAIN_ROLES.has(n.role)) continue
      const sceneName = terrainCatalog.looks?.[String(n.lookface)]
      const parts = sceneName ? terrainCatalog.scenes?.[sceneName] : null
      if (!parts) continue
      const cell = cellToPixel(n.x, n.y)
      for (const p of parts) {
        if (!p.frames || p.frames.length === 0) continue
        sprites.push({
          x: cell.px + (p.x || 0), y: cell.py + (p.y || 0),
          width: p.width || 0, height: p.height || 0,
          interval: p.interval || 0, frames: p.frames,
        })
      }
    }
    const built = buildSpriteGroup(sprites, 240)
    built.eachLayer((l) => terrainNpcGroup.addLayer(l))
    terrainNpcGroup._spriteEntries = built._spriteEntries
  }

  function renderEntities() {
    npcsLayer.clearLayers()
    spawnsLayer.clearLayers()
    portalsLayer.clearLayers()

    // 1. NPCs
    for (const npc of state.npcs || []) {
      const isSel = selected && selected.type === (npc.isGoal ? 'goal' : 'npc') && selected.id === npc.id
      const color = npc.isGoal ? '#94a3b8' : npc.kind === 'teleporter' ? '#a855f7' : '#38bdf8'
      const icon = lookIcon(npc.look, badgeHtml(npc.name, color, isSel), npc.name, isSel)
      const marker = L.marker(cellToLatLng(npc.x, npc.y), {
        icon,
        draggable: Boolean(state.canEdit && !npc.isGoal),
        zIndexOffset: isSel ? 500 : 100,
      })

      marker.on('click', (e) => {
        L.DomEvent.stopPropagation(e)
        if (placeMode) return
        selected = { type: npc.isGoal ? 'goal' : 'npc', id: npc.id }
        renderEntities()
        loadPanel(selected.type, npc.id)
      })

      if (state.canEdit && !npc.isGoal) {
        marker.on('dragend', async () => {
          const oldX = npc.x, oldY = npc.y
          const ll = marker.getLatLng()
          const cell = latLngToCellInt(ll.lat, ll.lng)
          const clampedX = Math.max(0, Math.min(65535, cell.x))
          const clampedY = Math.max(0, Math.min(65535, cell.y))
          const res = await postMove('npc', npc.id, clampedX, clampedY)
          if (res.saved) {
            npc.x = clampedX
            npc.y = clampedY
            marker.setLatLng(cellToLatLng(npc.x, npc.y))
            renderTerrainNpcs()
            if (selected?.type === 'npc' && selected.id === npc.id)
              loadPanel('npc', npc.id)
          } else {
            marker.setLatLng(cellToLatLng(oldX, oldY))
            selected = { type: 'npc', id: npc.id }
            loadPanel('npc', npc.id, { error: firstProblemText(res) })
          }
        })
      }

      marker.addTo(npcsLayer)
    }

    // 2. Spawns
    for (const sp of state.spawns || []) {
      const isSel = selected && selected.type === 'spawn' && selected.id === sp.id
      const color = sp.monsterBoss ? '#ef4444' : '#f59e0b'

      if (sp.singleFigure) {
        const icon = lookIcon(sp.look, badgeHtml(sp.monsterName, color, isSel), sp.monsterName, isSel)
        const marker = L.marker(cellToLatLng(sp.x, sp.y), {
          icon,
          draggable: Boolean(state.canEdit),
          zIndexOffset: isSel ? 500 : 110,
        })

        marker.on('click', (e) => {
          L.DomEvent.stopPropagation(e)
          if (placeMode) return
          selected = { type: 'spawn', id: sp.id }
          renderEntities()
          loadPanel('spawn', sp.id)
        })

        if (state.canEdit) {
          marker.on('dragend', async () => {
            const oldX = sp.x, oldY = sp.y
            const cell = latLngToCellInt(marker.getLatLng().lat, marker.getLatLng().lng)
            const nx = Math.max(0, Math.min(65535, cell.x))
            const ny = Math.max(0, Math.min(65535, cell.y))
            const res = await postMove('spawn', sp.id, nx, ny)
            if (res.saved) {
              sp.x = nx; sp.y = ny
              marker.setLatLng(cellToLatLng(nx, ny))
              if (selected?.type === 'spawn' && selected.id === sp.id)
                loadPanel('spawn', sp.id)
            } else {
              marker.setLatLng(cellToLatLng(oldX, oldY))
              selected = { type: 'spawn', id: sp.id }
              loadPanel('spawn', sp.id, { error: firstProblemText(res) })
            }
          })
        }

        marker.addTo(spawnsLayer)
      } else {
        const poly = L.polygon(spawnCircleLatLng(sp.x, sp.y, sp.spread), {
          color: isSel ? '#facc15' : color,
          weight: isSel ? 2.5 : 1.5,
          fillColor: color,
          fillOpacity: isSel ? 0.28 : 0.14,
        }).addTo(spawnsLayer)

        const label = `${sp.monsterName} ×${sp.amount}`
        const center = L.marker(cellToLatLng(sp.x, sp.y), {
          icon: badgeIcon(label, color, isSel),
          draggable: Boolean(state.canEdit),
          zIndexOffset: isSel ? 450 : 80,
        }).addTo(spawnsLayer)

        const onSelect = (e) => {
          L.DomEvent.stopPropagation(e)
          if (placeMode) return
          selected = { type: 'spawn', id: sp.id }
          renderEntities()
          loadPanel('spawn', sp.id)
        }
        poly.on('click', onSelect)
        center.on('click', onSelect)

        if (state.canEdit) {
          center.on('drag', () => {
            const c = latLngToCellInt(center.getLatLng().lat, center.getLatLng().lng)
            poly.setLatLngs(spawnCircleLatLng(c.x, c.y, sp.spread))
          })
          center.on('dragend', async () => {
            const oldX = sp.x, oldY = sp.y
            const c = latLngToCellInt(center.getLatLng().lat, center.getLatLng().lng)
            const nx = Math.max(0, Math.min(65535, c.x))
            const ny = Math.max(0, Math.min(65535, c.y))
            const res = await postMove('spawn', sp.id, nx, ny)
            if (res.saved) {
              sp.x = nx; sp.y = ny
              center.setLatLng(cellToLatLng(nx, ny))
              poly.setLatLngs(spawnCircleLatLng(nx, ny, sp.spread))
              if (selected?.type === 'spawn' && selected.id === sp.id)
                loadPanel('spawn', sp.id)
            } else {
              center.setLatLng(cellToLatLng(oldX, oldY))
              poly.setLatLngs(spawnCircleLatLng(oldX, oldY, sp.spread))
              selected = { type: 'spawn', id: sp.id }
              loadPanel('spawn', sp.id, { error: firstProblemText(res) })
            }
          })
        }
      }
    }

    // 3. Portals (DMap cells) + Map entry point
    for (const p of state.portals || []) {
      if (p.x == null || p.y == null) continue
      const isSel = selected && selected.type === 'portal' && selected.id === p.index
      const label = p.toMapName ? `P#${p.index} → ${p.toMapName}` : `P#${p.index}`
      const marker = L.marker(cellToLatLng(p.x, p.y), {
        icon: badgeIcon(label, p.toMap != null ? '#c084fc' : '#64748b', isSel),
        zIndexOffset: isSel ? 480 : 120,
      }).addTo(portalsLayer)

      marker.on('click', (e) => {
        L.DomEvent.stopPropagation(e)
        if (placeMode) return
        selected = { type: 'portal', id: p.index }
        renderEntities()
        loadPanel('portal', p.index)
      })
    }

    if (state.entryPoint) {
      const ep = state.entryPoint
      const epMarker = L.marker(cellToLatLng(ep.x, ep.y), {
        icon: badgeIcon('Giriş', '#22c55e', false),
        draggable: Boolean(state.canEdit),
        zIndexOffset: 130,
      }).addTo(portalsLayer)

      epMarker.on('click', (e) => {
        L.DomEvent.stopPropagation(e)
        if (placeMode) return
        selected = null
        renderEntities()
        loadPanel('map', null)
      })

      if (state.canEdit) {
        epMarker.on('dragend', async () => {
          const oldX = ep.x, oldY = ep.y
          const c = latLngToCellInt(epMarker.getLatLng().lat, epMarker.getLatLng().lng)
          const nx = Math.max(0, Math.min(65535, c.x))
          const ny = Math.max(0, Math.min(65535, c.y))
          const res = await postMove('entry', 0, nx, ny)
          if (res.saved) {
            ep.x = nx; ep.y = ny
            epMarker.setLatLng(cellToLatLng(nx, ny))
            if (!selected) loadPanel('map', null)
          } else {
            epMarker.setLatLng(cellToLatLng(oldX, oldY))
            loadPanel('map', null, { error: firstProblemText(res) })
          }
        })
      }
    }
  }

  renderEntities()

  // Load terrain NPC catalog (optional asset)
  fetch('/assets/terrainNpcs.json')
    .then((r) => (r.ok ? r.json() : null))
    .then((cat) => { if (cat) { terrainCatalog = cat; renderTerrainNpcs() } })
    .catch(() => undefined)

  // Eagerly fetch grid for hover status readout
  ensureGrid()

  // --- Map mouse events ---

  map.on('mousemove', (e) => {
    const c = latLngToCellInt(e.latlng.lat, e.latlng.lng)
    let text = `Hücre: ${c.x}, ${c.y}`
    if (decodedGrid && c.x >= 0 && c.y >= 0 && c.x < decodedGrid.width && c.y < decodedGrid.height) {
      const walk = isCellWalkable(decodedGrid, c.x, c.y) ? 'Yürünebilir' : 'Kapalı'
      const elev = cellElevation(decodedGrid, c.x, c.y)
      text += ` · ${walk}`
      if (elev != null) text += ` · Yükseklik: ${elev}`
    }
    if (statusEl) statusEl.textContent = text
  })

  map.on('click', (e) => {
    const c = latLngToCellInt(e.latlng.lat, e.latlng.lng)
    const nx = Math.max(0, Math.min(65535, c.x))
    const ny = Math.max(0, Math.min(65535, c.y))

    if (placeMode) {
      const mode = placeMode
      setPlaceMode(null)
      selected = null
      renderEntities()
      if (mode === 'portal') {
        const near = (state.portals || []).find((p) => p.x != null && p.y != null && Math.hypot(p.x - nx, p.y - ny) <= 3)
        if (near) {
          selected = { type: 'portal', id: near.index }
          renderEntities()
          loadPanel('portal', near.index)
          return
        }
      }
      loadPanel(`new-${mode}`, null, { x: nx, y: ny })
      return
    }

    if (selected) {
      selected = null
      renderEntities()
      loadPanel('map', null)
    }
  })

  // --- Layer toggles & fit button ---

  document.querySelectorAll('[data-layer]').forEach((input) => {
    input.addEventListener('change', async () => {
      const name = input.dataset.layer
      const on = input.checked
      if (name === 'covers') on ? coversGroup.addTo(map) : map.removeLayer(coversGroup)
      else if (name === 'npcs') on ? npcsLayer.addTo(map) : map.removeLayer(npcsLayer)
      else if (name === 'spawns') on ? spawnsLayer.addTo(map) : map.removeLayer(spawnsLayer)
      else if (name === 'portals') on ? portalsLayer.addTo(map) : map.removeLayer(portalsLayer)
      else if (name === 'walkability') {
        if (on) {
          const g = await ensureGrid()
          if (g) {
            walkLayer ??= new GridCanvasLayer(g, 'walkability', { zIndex: 200 })
            walkLayer.addTo(map)
          }
        } else if (walkLayer) {
          map.removeLayer(walkLayer)
        }
      } else if (name === 'elevation') {
        if (on) {
          const g = await ensureGrid()
          if (g) {
            elevLayer ??= new GridCanvasLayer(g, 'elevation', { zIndex: 205 })
            elevLayer.addTo(map)
          }
        } else if (elevLayer) {
          map.removeLayer(elevLayer)
        }
      }
    })
  })

  document.getElementById('map-fit')?.addEventListener('click', () => map.fitBounds(bounds))

  // --- Placement mode controls ---

  const placeBanner = document.getElementById('map-place-banner')
  const placeText = document.getElementById('map-place-text')
  const placeLabels = {
    npc: 'Yeni NPC yerleştirmek için haritada bir hücreye tıkla.',
    spawn: 'Yeni doğma noktası yerleştirmek için haritada bir hücreye tıkla.',
    portal: 'Yeni geçit eklemek için haritada bir hücreye (veya DMap geçidine) tıkla.',
  }

  function setPlaceMode(mode) {
    placeMode = mode
    canvasEl.classList.toggle('placing', Boolean(mode))
    document.querySelectorAll('[data-place]').forEach((b) => b.classList.toggle('active', b.dataset.place === mode))
    if (placeBanner && placeText) {
      placeBanner.hidden = !mode
      placeText.textContent = mode ? placeLabels[mode] : ''
    }
  }

  document.querySelectorAll('[data-place]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const m = btn.dataset.place
      setPlaceMode(placeMode === m ? null : m)
    })
  })
  document.getElementById('map-place-cancel')?.addEventListener('click', () => setPlaceMode(null))
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && placeMode) setPlaceMode(null)
  })

  // --- Map switcher & panel delegation ---

  document.addEventListener('click', (e) => {
    const swOpt = e.target.closest('.map-switcher .picker-list button[data-value]')
    if (swOpt) {
      window.location.href = `/admin/maps/${swOpt.dataset.value}`
      return
    }
    if (e.target.closest('[data-select-map]')) {
      selected = null
      renderEntities()
      loadPanel('map', null)
    }
  })

  // --- Sync markers after htmx panel save/delete ---

  panelEl.addEventListener('htmx:afterSwap', () => {
    const syncEl = panelEl.querySelector('script[data-map-sync]')
    if (!syncEl) return
    try {
      const next = JSON.parse(syncEl.textContent || '{}')
      state = next
      renderEntities()
      renderTerrainNpcs()
    } catch {
      // ignore malformed sync payload
    }
  })
})()
