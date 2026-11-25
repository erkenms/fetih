export type LevelFilter = 'errors' | 'warnings' | 'all'

export interface IntegrityProblemLike {
  file: string
  category: string
  level: string
  entry?: number | null
  line?: number | null
  entityId?: string | null
  message: string
  editorPath: string
  editorKind: string
}

export interface IntegrityFilterOptions {
  level: LevelFilter
  file: string | null
  category: string | null
  search: string
}

export function filterProblems<T extends IntegrityProblemLike>(
  problems: readonly T[],
  options: IntegrityFilterOptions,
): T[] {
  const query = options.search.trim().toLowerCase()
  return problems.filter((p) => {
    if (options.level === 'errors' && p.level !== 'error') return false
    if (options.level === 'warnings' && p.level !== 'warning') return false
    if (options.file && p.file !== options.file) return false
    if (options.category && p.category !== options.category) return false
    if (query) {
      const haystack = `${p.file} ${p.entityId ?? ''} ${p.message} ${p.category}`.toLowerCase()
      if (!haystack.includes(query)) return false
    }
    return true
  })
}

export function distinctCategories(problems: readonly IntegrityProblemLike[]): string[] {
  const seen = new Set<string>()
  for (const p of problems) seen.add(p.category)
  return [...seen].sort()
}
