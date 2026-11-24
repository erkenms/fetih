export const languages = ['tr', 'en'] as const
export type Language = (typeof languages)[number]

export function buildResources(found: Record<string, { default: Record<string, unknown> }>) {
  const resources: Record<string, Record<string, Record<string, unknown>>> = {}
  for (const [path, module] of Object.entries(found)) {
    const match = /locales\/([^/]+)\/([^/]+)\.json$/.exec(path)
    if (!match) continue
    const [, language, namespace] = match
    ;(resources[language] ??= {})[namespace] = module.default
  }
  return resources
}

// The admin area defaults to English and the public site to Turkish, until the user picks one
export function initialLanguage(path: string, stored: string | null): Language {
  if (stored && (languages as readonly string[]).includes(stored)) return stored as Language
  return path.startsWith('/admin') ? 'en' : 'tr'
}
