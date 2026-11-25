import { Badge, Button, Group, Paper, SimpleGrid, Stack, Tabs, Text, Title } from '@mantine/core'
import { IconRefresh } from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { ActiveTab } from './ActiveTab'
import { DetainedTab } from './DetainedTab'
import { JailTab } from './JailTab'
import { LeaderboardTab } from './LeaderboardTab'
import { useNumber } from './shared'

const tabs = {
  jail: JailTab,
  active: ActiveTab,
  detained: DetainedTab,
  leaderboard: LeaderboardTab,
} as const
type Tab = keyof typeof tabs

function StatCard({ label, value, sub }: { label: string; value: string; sub: string }) {
  return (
    <Paper withBorder p="md" radius="md">
      <Text size="xs" c="dimmed" tt="uppercase" fw={600}>
        {label}
      </Text>
      <Text size="xl" fw={700} mt={4}>
        {value}
      </Text>
      <Text size="xs" c="dimmed" mt={4}>
        {sub}
      </Text>
    </Paper>
  )
}

export default function PkPage() {
  const { t } = useTranslation('pk')
  const number = useNumber()
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') ?? 'jail') as Tab
  const current = tab in tabs ? tab : 'jail'
  const Content = tabs[current]

  const summary = useQuery({
    queryKey: ['pk', 'summary'],
    queryFn: () => unwrap(api.GET('/api/pk/summary')),
  })

  const s = summary.data

  return (
    <Stack>
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Group gap="sm">
            <Title order={2}>{t('title')}</Title>
            {s && (
              <Badge variant="light" color={s.gameConnected ? 'green' : 'yellow'}>
                {t(s.gameConnected ? 'gameOnline' : 'gameOffline')}
              </Badge>
            )}
          </Group>
          <Text c="dimmed">{t('description')}</Text>
        </div>
        <Button
          variant="default"
          size="xs"
          leftSection={<IconRefresh size={14} />}
          loading={summary.isFetching}
          onClick={() => void queryClient.invalidateQueries({ queryKey: ['pk'] })}
        >
          {t('refresh')}
        </Button>
      </Group>

      <ErrorAlert error={summary.error} />

      {s && (
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
          <StatCard
            label={t('summary.jailed')}
            value={number(s.jailedTotal)}
            sub={t('summary.jailedSub', { online: number(s.jailedOnline) })}
          />
          <StatCard
            label={t('summary.active')}
            value={s.gameConnected ? number(s.activeTotal) : '—'}
            sub={t('summary.activeSub', {
              flashing: number(s.flashingCount),
              red: number(s.redCount),
              black: number(s.blackCount),
            })}
          />
          <StatCard
            label={t('summary.detained')}
            value={number(s.detainedRedeemable + s.detainedExpired)}
            sub={t('summary.detainedSub', {
              redeemable: number(s.detainedRedeemable),
              expired: number(s.detainedExpired),
            })}
          />
          <StatCard
            label={t('summary.ransomReady')}
            value={number(s.detainedRedeemed)}
            sub={t('summary.ransomReadySub')}
          />
        </SimpleGrid>
      )}

      <Tabs
        value={current}
        onChange={(value) => setParams(value ? { tab: value } : {})}
        keepMounted={false}
      >
        <Tabs.List>
          <Tabs.Tab value="jail">
            {t('tab.jail')}
            {s && s.jailedTotal > 0 ? ` (${s.jailedTotal})` : ''}
          </Tabs.Tab>
          <Tabs.Tab value="active">
            {t('tab.active')}
            {s && s.activeTotal > 0 ? ` (${s.activeTotal})` : ''}
          </Tabs.Tab>
          <Tabs.Tab value="detained">
            {t('tab.detained')}
            {s && s.detainedRedeemable + s.detainedExpired + s.detainedRedeemed > 0
              ? ` (${s.detainedRedeemable + s.detainedExpired + s.detainedRedeemed})`
              : ''}
          </Tabs.Tab>
          <Tabs.Tab value="leaderboard">{t('tab.leaderboard')}</Tabs.Tab>
        </Tabs.List>
      </Tabs>

      <Content />
    </Stack>
  )
}
