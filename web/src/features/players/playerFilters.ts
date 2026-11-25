// The players page keeps its filters in the URL, so going back from a player returns to the
// same list. With no "online" parameter the page shows online players only.

export const CLASS_LINES = [1, 2, 4, 5, 6, 7, 10] as const
export const SORTS = ['level', 'newest', 'name', 'silver', 'cp', 'pk'] as const

export interface PlayerFilters {
  q: string
  ip: string
  online: boolean
  classLine: number | null
  mapId: number | null
  minLevel: number | null
  maxLevel: number | null
  status: number | null
  authority: number | null
  sort: string | null
  page: number
  pageSize: number
}

const num = (v: string | null) => (v == null || v === '' || Number.isNaN(Number(v)) ? null : Number(v))

export function filtersFromParams(params: URLSearchParams): PlayerFilters {
  return {
    q: params.get('q') ?? '',
    ip: params.get('ip') ?? '',
    online: params.get('online') !== '0',
    classLine: num(params.get('class')),
    mapId: num(params.get('map')),
    minLevel: num(params.get('minLevel')),
    maxLevel: num(params.get('maxLevel')),
    status: num(params.get('status')),
    authority: num(params.get('authority')),
    sort: params.get('sort'),
    page: num(params.get('page')) ?? 1,
    pageSize: num(params.get('pageSize')) ?? 50,
  }
}

export function queryFromFilters(f: PlayerFilters): Record<string, string> {
  const out: Record<string, string> = {}
  if (f.q) out.q = f.q
  if (f.ip) out.ip = f.ip
  if (!f.online) out.online = '0'
  if (f.classLine != null) out.class = String(f.classLine)
  if (f.mapId != null) out.map = String(f.mapId)
  if (f.minLevel != null) out.minLevel = String(f.minLevel)
  if (f.maxLevel != null) out.maxLevel = String(f.maxLevel)
  if (f.status != null) out.status = String(f.status)
  if (f.authority != null) out.authority = String(f.authority)
  if (f.sort) out.sort = f.sort
  if (f.page > 1) out.page = String(f.page)
  if (f.pageSize !== 50) out.pageSize = String(f.pageSize)
  return out
}
