import { SegmentedControl } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { languages, setLanguage, type Language } from './i18n'

export function LanguageSwitcher() {
  const { i18n } = useTranslation()
  return (
    <SegmentedControl
      size="xs"
      value={i18n.resolvedLanguage ?? i18n.language}
      onChange={(value) => setLanguage(value as Language)}
      data={languages.map((l) => ({ value: l, label: l.toUpperCase() }))}
    />
  )
}
