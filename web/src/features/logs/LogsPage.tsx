import { Group, Stack, Text, TextInput, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { LogTimeline } from './LogTimeline'
import { parseOptionalId } from './timeline'

export default function LogsPage() {
  const { t } = useTranslation('logs')
  const [params, setParams] = useSearchParams()

  const charRaw = params.get('characterId') ?? ''
  const accRaw = params.get('accountId') ?? ''

  const updateParam = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value.trim()) next.set(key, value.trim())
    else next.delete(key)
    setParams(next, { replace: true })
  }

  return (
    <Stack>
      <Title order={2}>{t('title')}</Title>
      <Text c="dimmed">{t('description')}</Text>
      <Group>
        <TextInput
          label={t('filter.characterId')}
          placeholder="1000001"
          value={charRaw}
          onChange={(e) => updateParam('characterId', e.currentTarget.value)}
          w={180}
        />
        <TextInput
          label={t('filter.accountId')}
          placeholder="1"
          value={accRaw}
          onChange={(e) => updateParam('accountId', e.currentTarget.value)}
          w={180}
        />
      </Group>
      <LogTimeline characterId={parseOptionalId(charRaw)} accountId={parseOptionalId(accRaw)} />
    </Stack>
  )
}
