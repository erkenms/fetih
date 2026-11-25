import { Stack, Tabs, Text, Title } from '@mantine/core'
import { IconMessageDots, IconShieldCode } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { ChatLogTab } from './ChatLogTab'
import { GmCommandsTab } from './GmCommandsTab'

export default function ChatPage() {
  const { t } = useTranslation('chat')
  const [params, setParams] = useSearchParams()
  const activeTab = params.get('tab') === 'gm' ? 'gm' : 'chat'

  const handleTabChange = (value: string | null) => {
    const next = new URLSearchParams(params)
    if (value === 'gm') next.set('tab', 'gm')
    else next.delete('tab')
    setParams(next, { replace: true })
  }

  return (
    <Stack>
      <Title order={2}>{t('title')}</Title>
      <Text c="dimmed">{t('description')}</Text>
      <Tabs value={activeTab} onChange={handleTabChange}>
        <Tabs.List mb="md">
          <Tabs.Tab value="chat" leftSection={<IconMessageDots size={16} />}>
            {t('tabs.chat')}
          </Tabs.Tab>
          <Tabs.Tab value="gm" leftSection={<IconShieldCode size={16} />}>
            {t('tabs.gmCommands')}
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="chat">
          <ChatLogTab />
        </Tabs.Panel>

        <Tabs.Panel value="gm">
          <GmCommandsTab />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  )
}
