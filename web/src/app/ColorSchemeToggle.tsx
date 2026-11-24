import { ActionIcon, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { IconMoon, IconSun } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'

export function ColorSchemeToggle() {
  const { t } = useTranslation()
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('light')
  return (
    <ActionIcon
      variant="default"
      size="lg"
      aria-label={t('shell.toggleColorScheme')}
      onClick={() => setColorScheme(scheme === 'dark' ? 'light' : 'dark')}
    >
      {scheme === 'dark' ? <IconSun size={18} /> : <IconMoon size={18} />}
    </ActionIcon>
  )
}
