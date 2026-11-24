import { Badge, Group, Loader, Paper, Stack, Table, Text, Title } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'

export default function HealthPage() {
  const { t, i18n } = useTranslation('health')
  const health = useQuery({
    queryKey: ['health'],
    queryFn: () => unwrap(api.GET('/api/health')),
    refetchInterval: 5000,
  })

  const rows: [string, boolean | undefined][] = [
    [t('gameDatabase'), health.data?.gameDatabase],
    [t('accountDatabase'), health.data?.accountDatabase],
    [t('gameServer'), health.data?.gameServer],
  ]

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('title')}</Title>
        {health.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('description')}</Text>
      <ErrorAlert error={health.error} />
      <Paper withBorder>
        <Table>
          <Table.Tbody>
            {rows.map(([label, ok]) => (
              <Table.Tr key={label}>
                <Table.Td>{label}</Table.Td>
                <Table.Td>
                  {ok === undefined ? (
                    <Badge color="gray">…</Badge>
                  ) : (
                    <Badge color={ok ? 'green' : 'red'}>{ok ? t('up') : t('down')}</Badge>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
      {health.data && (
        <Text size="sm" c="dimmed">
          {t('serverTime', { time: new Date(health.data.serverTime).toLocaleString(i18n.language) })}
        </Text>
      )}
    </Stack>
  )
}
