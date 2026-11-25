import { Alert, Anchor, Button, Group, Loader, SegmentedControl, Stack, Table, Text } from '@mantine/core'
import { IconAlertTriangle, IconRefresh } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { Panel } from '../../theme/Panel'
import { MoneyFlowChart, SupplyTrendChart } from './EconomyCharts'
import classes from './EconomyPage.module.css'
import { formatCompact, type EconomyRange } from './economyUtils'

export default function EconomyPage() {
  const { t, i18n } = useTranslation('economy')
  const [range, setRange] = useState<EconomyRange>('24h')

  const query = useQuery({
    queryKey: ['economy', range],
    queryFn: () =>
      unwrap(
        api.GET('/api/economy', {
          params: { query: { range } },
        }),
      ),
    refetchInterval: 60_000,
  })

  const locale = i18n.resolvedLanguage ?? i18n.language
  const formatNum = (n: number) => new Intl.NumberFormat(locale).format(n)
  const formatTime = (iso: string) => {
    try {
      return new Intl.DateTimeFormat(locale, {
        month: range === '7d' || range === '30d' ? 'short' : undefined,
        day: range === '7d' || range === '30d' ? 'numeric' : undefined,
        hour: '2-digit',
        minute: '2-digit',
      }).format(new Date(iso))
    } catch {
      return iso
    }
  }

  const data = query.data

  return (
    <Stack gap="sm">
      <Panel
        title={t('title')}
        icon="icons/items/724426.png"
        aside={
          <Group gap="xs">
            <SegmentedControl
              size="xs"
              value={range}
              onChange={(v) => setRange(v as EconomyRange)}
              data={[
                { value: '1h', label: t('range.1h') },
                { value: '24h', label: t('range.24h') },
                { value: '7d', label: t('range.7d') },
                { value: '30d', label: t('range.30d') },
              ]}
            />
            <Button
              size="xs"
              variant="default"
              leftSection={<IconRefresh size={13} />}
              loading={query.isFetching}
              onClick={() => void query.refetch()}
            >
              {t('refresh')}
            </Button>
          </Group>
        }
      >
        {query.isLoading && (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="xs">{t('loading')}</Text>
          </Group>
        )}
        {query.error && <ErrorAlert error={query.error} />}
        {data && (
          <div className={classes.kpiGrid}>
            <div className={classes.kpiCard}>
              <div className={classes.kpiLabel}>{t('totals.silver')}</div>
              <div className={classes.kpiValue} title={formatNum(data.totals.totalSilver)}>
                {formatCompact(data.totals.totalSilver)}
              </div>
              <div className={classes.kpiSub}>
                {t('totals.silverBreakdown', {
                  pocket: formatCompact(data.totals.pocketSilver),
                  stored: formatCompact(data.totals.storedSilver),
                })}
              </div>
            </div>

            <div className={classes.kpiCard}>
              <div className={classes.kpiLabel}>{t('totals.cp')}</div>
              <div className={classes.kpiValue} title={formatNum(data.totals.totalConquerPoints)}>
                {formatCompact(data.totals.totalConquerPoints)}
              </div>
              <div className={classes.kpiSub}>
                {t('totals.characters', { count: data.totals.characterCount })}
              </div>
            </div>

            <div className={classes.kpiCard}>
              <div className={classes.kpiLabel}>{t('totals.guildFunds')}</div>
              <div className={classes.kpiValue}>{formatCompact(data.totals.guildSilverFund)}</div>
              <div className={classes.kpiSub}>
                {t('totals.guildCp', { cp: formatCompact(data.totals.guildConquerPointsFund) })}
              </div>
            </div>

            <div className={classes.kpiCard}>
              <div className={classes.kpiLabel}>{t('flow.netLabel', { range })}</div>
              <div
                className={classes.kpiValue}
                style={{ color: data.flow.netFlow >= 0 ? '#1f6f2b' : '#9e1b16' }}
              >
                {data.flow.lokiAvailable
                  ? `${data.flow.netFlow >= 0 ? '+' : ''}${formatCompact(data.flow.netFlow)}`
                  : '—'}
              </div>
              <div className={classes.kpiSub}>
                {data.flow.lokiAvailable
                  ? t('flow.inOutSummary', {
                      inflow: formatCompact(data.flow.totalEarnedFromNpcs + data.flow.totalPickedUpSilver),
                      outflow: formatCompact(data.flow.totalSpentAtNpcs),
                    })
                  : t('flow.lokiUnavailable')}
              </div>
            </div>
          </div>
        )}
      </Panel>

      {data?.flow.spikeDetected && (
        <Alert
          color="red"
          icon={<IconAlertTriangle size={16} />}
          title={t('flow.spikeTitle')}
        >
          <Text size="xs">
            {t('flow.spikeBody', { ratio: data.flow.spikeRatio ?? 0 })}
          </Text>
        </Alert>
      )}

      {data && (
        <>
          <Panel
            title={t('flow.title')}
            icon="icons/items/610120.png"
            aside={
              data.flow.lokiAvailable ? (
                <Text size="xs" c="#f5dfa3">
                  {t('flow.spent')}: {formatCompact(data.flow.totalSpentAtNpcs)} · {t('flow.npcSales')}:{' '}
                  {formatCompact(data.flow.totalEarnedFromNpcs)} · {t('flow.pickups')}:{' '}
                  {formatCompact(data.flow.totalPickedUpSilver)}
                </Text>
              ) : undefined
            }
          >
            {!data.flow.lokiAvailable ? (
              <Text size="xs" c="dimmed">
                {t('flow.lokiUnavailable')}
              </Text>
            ) : (
              <MoneyFlowChart buckets={data.flow.buckets} formatTime={formatTime} />
            )}
          </Panel>

          <div className={classes.twoCol}>
            <Panel title={t('trends.silverTitle')} icon="icons/items/711394.png">
              {!data.trends.prometheusAvailable ? (
                <Text size="xs" c="dimmed">
                  {t('trends.prometheusUnavailable')}
                </Text>
              ) : (
                <SupplyTrendChart
                  points={data.trends.silver}
                  color="#8f4f12"
                  emptyText={t('trends.noPoints')}
                  formatTime={formatTime}
                />
              )}
            </Panel>

            <Panel title={t('trends.cpTitle')} icon="icons/items/200007.png">
              {!data.trends.prometheusAvailable ? (
                <Text size="xs" c="dimmed">
                  {t('trends.prometheusUnavailable')}
                </Text>
              ) : (
                <SupplyTrendChart
                  points={data.trends.conquerPoints}
                  color="#6e1b18"
                  emptyText={t('trends.noPoints')}
                  formatTime={formatTime}
                />
              )}
            </Panel>
          </div>

          <div className={classes.twoCol}>
            <Panel title={t('holders.silverTitle')} icon="icons/items/724426.png">
              {data.topSilverHolders.length === 0 ? (
                <Text size="xs" c="dimmed">
                  {t('holders.empty')}
                </Text>
              ) : (
                <Table striped highlightOnHover verticalSpacing={4}>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th style={{ width: 32 }}>#</Table.Th>
                      <Table.Th>{t('holders.columns.character')}</Table.Th>
                      <Table.Th style={{ width: 64 }}>{t('holders.columns.level')}</Table.Th>
                      <Table.Th>{t('holders.columns.totalSilver')}</Table.Th>
                      <Table.Th>{t('holders.columns.stored')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {data.topSilverHolders.map((h, i) => (
                      <Table.Tr key={h.characterId}>
                        <Table.Td>{i + 1}</Table.Td>
                        <Table.Td>
                          <Anchor component={Link} to={`/admin/characters/${h.characterId}`} size="xs" fw={700}>
                            {h.name}
                          </Anchor>
                        </Table.Td>
                        <Table.Td>{h.level}</Table.Td>
                        <Table.Td fw={700}>{formatNum(h.totalSilver)}</Table.Td>
                        <Table.Td>{formatNum(h.storedSilver)}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Panel>

            <Panel title={t('holders.cpTitle')} icon="icons/items/200007.png">
              {data.topCpHolders.length === 0 ? (
                <Text size="xs" c="dimmed">
                  {t('holders.empty')}
                </Text>
              ) : (
                <Table striped highlightOnHover verticalSpacing={4}>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th style={{ width: 32 }}>#</Table.Th>
                      <Table.Th>{t('holders.columns.character')}</Table.Th>
                      <Table.Th style={{ width: 64 }}>{t('holders.columns.level')}</Table.Th>
                      <Table.Th>{t('holders.columns.cp')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {data.topCpHolders.map((h, i) => (
                      <Table.Tr key={h.characterId}>
                        <Table.Td>{i + 1}</Table.Td>
                        <Table.Td>
                          <Anchor component={Link} to={`/admin/characters/${h.characterId}`} size="xs" fw={700}>
                            {h.name}
                          </Anchor>
                        </Table.Td>
                        <Table.Td>{h.level}</Table.Td>
                        <Table.Td fw={700}>{formatNum(h.conquerPoints)}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              )}
            </Panel>
          </div>
        </>
      )}
    </Stack>
  )
}
