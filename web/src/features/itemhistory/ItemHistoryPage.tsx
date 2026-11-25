import {
  Alert,
  Anchor,
  Badge,
  Button,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { IconAlertTriangle, IconArrowRight, IconExternalLink, IconHistory, IconSearch } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { Panel } from '../../theme/Panel'
import {
  eventBadgeColor,
  parseItemIdInput,
  positionCategory,
  rangeToBounds,
  type TimeRangeKey,
} from './itemHistoryUtils'

export default function ItemHistoryPage() {
  const { itemId: routeItemId } = useParams<{ itemId?: string }>()
  const navigate = useNavigate()
  const { t, i18n } = useTranslation('itemHistory')

  const parsedRouteId = routeItemId ? parseItemIdInput(routeItemId) : null
  const [inputValue, setInputValue] = useState(routeItemId ?? '')
  const [range, setRange] = useState<TimeRangeKey>('30d')

  useEffect(() => {
    setInputValue(routeItemId ?? '')
  }, [routeItemId])

  const bounds = useMemo(() => rangeToBounds(range), [range])

  const historyQuery = useQuery({
    queryKey: ['item-history', parsedRouteId, range],
    queryFn: () =>
      unwrap(
        api.GET('/api/item-history/{itemId}', {
          params: {
            path: { itemId: parsedRouteId! },
            query: { from: bounds.from, to: bounds.to },
          },
        }),
      ),
    enabled: parsedRouteId != null,
  })

  const duplicatesQuery = useQuery({
    queryKey: ['item-history', 'duplicates', range],
    queryFn: () =>
      unwrap(
        api.GET('/api/item-history/duplicates', {
          params: { query: { from: bounds.from, to: bounds.to } },
        }),
      ),
  })

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault()
    const id = parseItemIdInput(inputValue)
    if (id != null) {
      void navigate(`/admin/item-history/${id}`)
    }
  }

  const formatTime = (iso: string) => {
    try {
      return new Intl.DateTimeFormat(i18n.resolvedLanguage ?? i18n.language, {
        dateStyle: 'short',
        timeStyle: 'medium',
      }).format(new Date(iso))
    } catch {
      return iso
    }
  }

  const data = historyQuery.data

  return (
    <Stack gap="sm">
      <Panel
        title={t('title')}
        icon="icons/items/711394.png"
        aside={
          data?.grafanaExploreUrl ? (
            <Anchor
              href={data.grafanaExploreUrl}
              target="_blank"
              rel="noreferrer"
              size="xs"
              c="#f5dfa3"
            >
              <Group gap={4} wrap="nowrap">
                <span>{t('openGrafana')}</span>
                <IconExternalLink size={12} />
              </Group>
            </Anchor>
          ) : undefined
        }
      >
        <form onSubmit={handleSearch}>
          <Group gap="sm" align="flex-end" wrap="wrap">
            <TextInput
              label={t('search.label')}
              placeholder={t('search.placeholder')}
              value={inputValue}
              onChange={(e) => setInputValue(e.currentTarget.value)}
              leftSection={<IconSearch size={14} />}
              size="xs"
              style={{ width: 220 }}
            />
            <SegmentedControl
              size="xs"
              value={range}
              onChange={(v) => setRange(v as TimeRangeKey)}
              data={[
                { value: '24h', label: t('range.24h') },
                { value: '7d', label: t('range.7d') },
                { value: '30d', label: t('range.30d') },
                { value: '90d', label: t('range.90d') },
              ]}
            />
            <Button
              type="submit"
              size="xs"
              disabled={parseItemIdInput(inputValue) == null}
              leftSection={<IconHistory size={14} />}
            >
              {t('search.submit')}
            </Button>
          </Group>
        </form>
      </Panel>

      {parsedRouteId != null && (
        <>
          {historyQuery.isLoading && (
            <Panel title={t('loadingTitle', { id: parsedRouteId })}>
              <Group gap="xs">
                <Loader size="xs" />
                <Text size="xs">{t('loading')}</Text>
              </Group>
            </Panel>
          )}

          {historyQuery.error && <ErrorAlert error={historyQuery.error} />}

          {data && (
            <>
              {data.duplicates.length > 0 && (
                <Alert
                  color="red"
                  icon={<IconAlertTriangle size={16} />}
                  title={t('duplicates.alertTitle', { count: data.duplicates.length })}
                >
                  <Stack gap={4}>
                    {data.duplicates.map((flag, idx) => (
                      <Group key={`${flag.kind}-${idx}`} gap="xs" wrap="wrap">
                        <Badge color="red" size="xs">
                          {t(`duplicates.kind.${flag.kind}` as never)}
                        </Badge>
                        <Text size="xs">{flag.reason}</Text>
                        {flag.characterIds.map((cid) => (
                          <Anchor key={cid} component={Link} to={`/admin/characters/${cid}`} size="xs">
                            #{cid}
                          </Anchor>
                        ))}
                      </Group>
                    ))}
                  </Stack>
                </Alert>
              )}

              {!data.lokiAvailable && (
                <Alert color="yellow" icon={<IconAlertTriangle size={16} />} title={t('lokiUnavailableTitle')}>
                  <Text size="xs">{t('lokiUnavailableBody')}</Text>
                </Alert>
              )}

              <Panel title={t('holder.title', { id: data.itemId })} icon="icons/items/200007.png">
                {data.currentHolders.length === 0 ? (
                  <Text size="xs" c="dimmed">
                    {t('holder.none')}
                  </Text>
                ) : (
                  <Table striped verticalSpacing={4}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('holder.columns.item')}</Table.Th>
                        <Table.Th>{t('holder.columns.owner')}</Table.Th>
                        <Table.Th>{t('holder.columns.location')}</Table.Th>
                        <Table.Th>{t('holder.columns.stats')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {data.currentHolders.map((h, idx) => {
                        const loc = positionCategory(h.position)
                        return (
                          <Table.Tr key={`${h.itemId}-${h.characterId}-${idx}`}>
                            <Table.Td>
                              <Group gap="xs" wrap="nowrap">
                                {h.itemIcon && (
                                  <img
                                    src={`/assets/${h.itemIcon}`}
                                    alt=""
                                    width={24}
                                    height={24}
                                    onError={(e) => (e.currentTarget.style.display = 'none')}
                                  />
                                )}
                                <Stack gap={0}>
                                  <Text size="xs" fw={700}>
                                    {h.itemName ?? `Type #${h.typeId}`}
                                    {h.plus > 0 ? ` +${h.plus}` : ''}
                                  </Text>
                                  <Text size="xs" c="dimmed">
                                    ID {h.itemId} · Type {h.typeId}
                                  </Text>
                                </Stack>
                              </Group>
                            </Table.Td>
                            <Table.Td>
                              <Anchor component={Link} to={`/admin/characters/${h.characterId}`} size="xs" fw={700}>
                                {h.characterName ?? `#${h.characterId}`}
                              </Anchor>
                            </Table.Td>
                            <Table.Td>
                              <Badge size="xs" variant="light">
                                {t(`position.${loc}` as never)}
                                {loc === 'warehouse' && h.package > 0 ? ` #${h.package}` : ''}
                              </Badge>
                            </Table.Td>
                            <Table.Td>
                              <Group gap={6} wrap="wrap">
                                {h.stackAmount > 1 && <Badge size="xs" color="gray">x{h.stackAmount}</Badge>}
                                {h.bless > 0 && <Badge size="xs" color="yellow">-{h.bless}%</Badge>}
                                {h.enchant > 0 && <Badge size="xs" color="teal">+{h.enchant} HP</Badge>}
                                {(h.gem1 > 0 || h.gem2 > 0) && (
                                  <Badge size="xs" color="grape">
                                    {t('holder.gems', { g1: h.gem1, g2: h.gem2 })}
                                  </Badge>
                                )}
                                {h.locked && <Badge size="xs" color="red">{t('holder.locked')}</Badge>}
                              </Group>
                            </Table.Td>
                          </Table.Tr>
                        )
                      })}
                    </Table.Tbody>
                  </Table>
                )}
              </Panel>

              <Panel
                title={t('timeline.title', { count: data.steps.length })}
                icon="icons/items/610120.png"
                aside={data.truncated ? <Badge color="yellow" size="xs">{t('timeline.truncated')}</Badge> : undefined}
              >
                {data.steps.length === 0 ? (
                  <Text size="xs" c="dimmed">
                    {t('timeline.empty')}
                  </Text>
                ) : (
                  <Table striped highlightOnHover verticalSpacing={5}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th style={{ width: 150 }}>{t('timeline.columns.time')}</Table.Th>
                        <Table.Th style={{ width: 135 }}>{t('timeline.columns.event')}</Table.Th>
                        <Table.Th style={{ width: 220 }}>{t('timeline.columns.owner')}</Table.Th>
                        <Table.Th>{t('timeline.columns.details')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {data.steps.map((step, idx) => (
                        <Table.Tr key={`${step.timestamp}-${step.event}-${idx}`}>
                          <Table.Td>
                            <Text size="xs" ff="monospace">
                              {formatTime(step.timestamp)}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Badge size="xs" color={eventBadgeColor(step.event)}>
                              {step.event}
                            </Badge>
                          </Table.Td>
                          <Table.Td>
                            <Group gap={4} wrap="nowrap">
                              {(step.fromName || step.fromCharacterId != null) && (
                                <>
                                  {step.fromCharacterId != null ? (
                                    <Anchor component={Link} to={`/admin/characters/${step.fromCharacterId}`} size="xs">
                                      {step.fromName ?? `#${step.fromCharacterId}`}
                                    </Anchor>
                                  ) : (
                                    <Text size="xs">{step.fromName}</Text>
                                  )}
                                  <IconArrowRight size={12} />
                                </>
                              )}
                              {step.ownerCharacterId != null ? (
                                <Anchor component={Link} to={`/admin/characters/${step.ownerCharacterId}`} size="xs" fw={700}>
                                  {step.ownerName ?? `#${step.ownerCharacterId}`}
                                </Anchor>
                              ) : (
                                <Text size="xs" fw={700}>
                                  {step.ownerName ?? '—'}
                                </Text>
                              )}
                            </Group>
                          </Table.Td>
                          <Table.Td>
                            <Text size="xs">{step.message}</Text>
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                )}
              </Panel>
            </>
          )}
        </>
      )}

      <Panel title={t('duplicates.scanTitle')} icon="icons/items/410036.png">
        {duplicatesQuery.isLoading && (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="xs">{t('duplicates.scanning')}</Text>
          </Group>
        )}
        {duplicatesQuery.error && <ErrorAlert error={duplicatesQuery.error} />}
        {duplicatesQuery.data && (
          <Stack gap="xs">
            {!duplicatesQuery.data.lokiAvailable && (
              <Text size="xs" c="dimmed">
                {t('duplicates.dbOnlyNote')}
              </Text>
            )}
            {duplicatesQuery.data.flags.length === 0 ? (
              <Text size="xs" c="dimmed">
                {t('duplicates.none')}
              </Text>
            ) : (
              <Table striped verticalSpacing={4}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th style={{ width: 110 }}>{t('duplicates.columns.itemId')}</Table.Th>
                    <Table.Th style={{ width: 170 }}>{t('duplicates.columns.kind')}</Table.Th>
                    <Table.Th>{t('duplicates.columns.reason')}</Table.Th>
                    <Table.Th>{t('duplicates.columns.characters')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {duplicatesQuery.data.flags.map((flag, idx) => (
                    <Table.Tr key={`${flag.itemId}-${flag.kind}-${idx}`}>
                      <Table.Td>
                        <Tooltip label={t('openHistory')}>
                          <Anchor component={Link} to={`/admin/item-history/${flag.itemId}`} size="xs" fw={700}>
                            #{flag.itemId}
                          </Anchor>
                        </Tooltip>
                      </Table.Td>
                      <Table.Td>
                        <Badge color="red" size="xs">
                          {t(`duplicates.kind.${flag.kind}` as never)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Text size="xs">{flag.reason}</Text>
                      </Table.Td>
                      <Table.Td>
                        <Group gap={6}>
                          {flag.characterIds.map((cid) => (
                            <Anchor key={cid} component={Link} to={`/admin/characters/${cid}`} size="xs">
                              #{cid}
                            </Anchor>
                          ))}
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Stack>
        )}
      </Panel>
    </Stack>
  )
}
