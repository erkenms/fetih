import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import { buildResources, initialLanguage, languages, type Language } from './language'

export { languages, type Language }

const storageKey = 'fetih.language'

// Every locales/<language>/<namespace>.json is picked up here, so a feature only adds its
// two files. One namespace per feature, named like its folder.
const files = import.meta.glob<{ default: Record<string, unknown> }>('../locales/*/*.json', {
  eager: true,
})

function storedLanguage(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

export function setLanguage(language: Language) {
  try {
    localStorage.setItem(storageKey, language)
  } catch {
    // Private windows may refuse; the choice then lasts until reload
  }
  void i18n.changeLanguage(language)
}

void i18n.use(initReactI18next).init({
  resources: buildResources(files),
  lng: initialLanguage(window.location.pathname, storedLanguage()),
  fallbackLng: 'en',
  defaultNS: 'common',
  ns: ['common'],
  interpolation: { escapeValue: false },
})

i18n.on('languageChanged', (language) => {
  document.documentElement.lang = language
})
document.documentElement.lang = i18n.language

export default i18n
