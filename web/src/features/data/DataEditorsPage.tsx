import { Stack, Tabs, Text, Title } from '@mantine/core'
import { IconCompass, IconMessage, IconTarget, IconTrendingUp } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { StudioReadOnlyBanner } from '../../components/StudioReadOnlyBanner'
import { GoalsEditor } from './GoalsEditor'
import { LevelsEditor } from './LevelsEditor'
import { TalkersEditor } from './TalkersEditor'
import { TeleportersEditor } from './TeleportersEditor'

const VALID_TABS = ['levels', 'goals', 'teleporters', 'talkers'] as const
type TabKey = (typeof VALID_TABS)[number]

export default function DataEditorsPage() {
  const { t } = useTranslation('data')
  const [params, setParams] = useSearchParams()
  const rawTab = params.get('tab')
  const activeTab: TabKey = VALID_TABS.includes(rawTab as TabKey) ? (rawTab as TabKey) : 'levels'

  function handleTabChange(val: string | null) {
    if (!val || !VALID_TABS.includes(val as TabKey)) return
    setParams({ tab: val }, { replace: true })
  }

  return (
    <Stack gap="md">
      <div>
        <Title order={2}>{t('title')}</Title>
        <Text size="sm" c="dimmed">
          {t('subtitle')}
        </Text>
      </div>

      <StudioReadOnlyBanner />

      <Tabs value={activeTab} onChange={handleTabChange} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="levels" leftSection={<IconTrendingUp size={16} />}>
            {t('tabs.levels')}
          </Tabs.Tab>
          <Tabs.Tab value="goals" leftSection={<IconTarget size={16} />}>
            {t('tabs.goals')}
          </Tabs.Tab>
          <Tabs.Tab value="teleporters" leftSection={<IconCompass size={16} />}>
            {t('tabs.teleporters')}
          </Tabs.Tab>
          <Tabs.Tab value="talkers" leftSection={<IconMessage size={16} />}>
            {t('tabs.talkers')}
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="levels">
          <LevelsEditor />
        </Tabs.Panel>
        <Tabs.Panel value="goals">
          <GoalsEditor />
        </Tabs.Panel>
        <Tabs.Panel value="teleporters">
          <TeleportersEditor />
        </Tabs.Panel>
        <Tabs.Panel value="talkers">
          <TalkersEditor />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  )
}
