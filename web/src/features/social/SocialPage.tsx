import { Stack, Tabs, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { ArenaTab } from './ArenaTab'
import { BoardTab } from './BoardTab'
import { FamiliesTab } from './FamiliesTab'
import { FlowersTab } from './FlowersTab'
import { GuildsTab } from './GuildsTab'
import { GuildWarTab } from './GuildWarTab'
import { NobilityTab } from './NobilityTab'

const tabs = {
  guilds: GuildsTab,
  families: FamiliesTab,
  nobility: NobilityTab,
  arena: ArenaTab,
  flowers: FlowersTab,
  guildwar: GuildWarTab,
  board: BoardTab,
} as const
type Tab = keyof typeof tabs

export default function SocialPage() {
  const { t } = useTranslation('social')
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') ?? 'guilds') as Tab
  const current = tab in tabs ? tab : 'guilds'
  const Content = tabs[current]

  return (
    <Stack>
      <div>
        <Title order={2}>{t('title')}</Title>
        <Text c="dimmed">{t('description')}</Text>
      </div>
      <Tabs value={current} onChange={(value) => setParams(value ? { tab: value } : {})} keepMounted={false}>
        <Tabs.List>
          {Object.keys(tabs).map((key) => (
            <Tabs.Tab key={key} value={key}>
              {t(`tab.${key}`)}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>
      <Content />
    </Stack>
  )
}
