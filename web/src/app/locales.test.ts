import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { buildResources, initialLanguage, languages } from './language'

const root = join(import.meta.dirname, '..', 'locales')

function keys(value: unknown, prefix = ''): string[] {
  if (value && typeof value === 'object')
    return Object.entries(value).flatMap(([k, v]) => keys(v, prefix ? `${prefix}.${k}` : k))
  return [prefix]
}

// i18next plural forms differ by language (Turkish has only "other"), so compare base keys
const base = (key: string) => key.replace(/_(zero|one|two|few|many|other)$/, '')

describe('locales', () => {
  const namespaces = new Set(languages.flatMap((l) => readdirSync(join(root, l))))

  for (const file of namespaces)
    it(`${file} has the same keys in every language`, () => {
      const [first, ...rest] = languages.map((l) => {
        const text = readFileSync(join(root, l, file), 'utf8')
        return new Set(keys(JSON.parse(text)).map(base))
      })
      for (const other of rest) expect([...other].sort()).toEqual([...first].sort())
    })
})

describe('buildResources', () => {
  it('maps files to language and namespace', () => {
    const resources = buildResources({ '../locales/tr/audit.json': { default: { title: 'x' } } })
    expect(resources).toEqual({ tr: { audit: { title: 'x' } } })
  })
})

describe('initialLanguage', () => {
  it('defaults to Turkish on the public site and English in the admin panel', () => {
    expect(initialLanguage('/', null)).toBe('tr')
    expect(initialLanguage('/admin/audit', null)).toBe('en')
  })

  it('keeps a stored choice', () => {
    expect(initialLanguage('/admin', 'tr')).toBe('tr')
    expect(initialLanguage('/', 'de')).toBe('tr')
  })
})
