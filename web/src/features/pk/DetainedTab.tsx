import {
  Badge,
  Group,
  Loader,
  ScrollArea,
  SegmentedControl,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { ItemCard } from '../../components/items'
import { detainedStatusColor, timeRemaining } from './pkUtils'
import { CharacterCell, useNumber, useTime } from './shared'

type DetainedItemView = Schemas['DetainedItemView']

function DetainedItemCell({ row }: { row: DetainedItemView }) {
  const { t } = useTranslation('pk')
  const number = useNumber()

  if (row.catalogItem) {
    return (
      <ItemCard
        item={row.catalogItem}
        instance={
          row.instance
            ? {
                plus: row.instance.plus,
                bless: row.instance.bless,
                enchant: row.instance.enchant,
                gem1: row.instance.gem1,
                gem2: row.instance.gem2,
                amount: row.instance.amount,
                amountLimit: row.instance.amountLimit,
                locked: row.instance.locked,
              }
            : undefined
        }
      />
    )
  }

  if (row.redeemed) {
    return (
      <Text size="sm" c="dimmed">
        {t('detained.ransomClaim', { cost: number(row.cost) })}
      </Text>
    )
  }

  return (
    <Text size="sm" c="dimmed">
      {t('detained.unknownItem', { id: row.itemId })}
    </Text>
  )
}

function WindowCell({ row }: { row: DetainedItemView }) {
  const { t } = useTranslation('pk')
  if (row.redeemed) {
    return (
      <Text size="xs" c="dimmed">
        {t('detained.redeemedLabel')}
      </Text>
    )
  }
  const rem = timeRemaining(row.expiresAt, Date.now())
  if (!rem || rem.expired) {
    return (
      <Text size="xs" c="red">
        {t('detained.expiredLabel', { days: row.daysElapsed })}
      </Text>
    )
  }
  if (rem.days > 0) {
    return (
      <Text size="xs" c="dimmed">
        {t('detained.daysLeft', { days: rem.days, hours: rem.hours })}
      </Text>
    )
  }
  return (
    <Text size="xs" c="orange">
      {t('detained.hoursLeft', { hours: rem.hours, minutes: rem.minutes })}
    </Text>
  )
}

export function DetainedTab() {
  const { t } = useTranslation('pk')
  const number = useNumber()
  const time = useTime()
  const [status, setStatus] = useState('all')
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 250)

  const detained = useQuery({
    queryKey: ['pk', 'detained', status, debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/pk/detained', {
          params: {
            query: {
              status: status === 'all' ? undefined : status,
              search: debounced.trim() || undefined,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  })

  const items = detained.data?.items ?? []

  return (
    <Stack>
      <Group wrap="wrap" gap="sm">
        <SegmentedControl
          value={status}
          onChange={setStatus}
          data={[
            { value: 'all', label: t('detained.all') },
            {
              value: 'redeemable',
              label: `${t('detained.redeemable')} (${detained.data?.redeemableCount ?? 0})`,
            },
            {
              value: 'expired',
              label: `${t('detained.expired')} (${detained.data?.expiredCount ?? 0})`,
            },
            {
              value: 'redeemed',
              label: `${t('detained.redeemed')} (${detained.data?.redeemedCount ?? 0})`,
            },
          ]}
        />
        <TextInput
          placeholder={t('search')}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          w={260}
        />
      </Group>

      <ErrorAlert error={detained.error} />

      {detained.isPending ? (
        <Loader />
      ) : items.length === 0 ? (
        <Text c="dimmed">{t('detained.empty')}</Text>
      ) : (
        <ScrollArea>
          <Table striped highlightOnHover miw={860}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('detained.item')}</Table.Th>
                <Table.Th>{t('detained.owner')}</Table.Th>
                <Table.Th>{t('detained.killer')}</Table.Th>
                <Table.Th>{t('detained.cost')}</Table.Th>
                <Table.Th>{t('detained.status')}</Table.Th>
                <Table.Th>{t('detained.detainedAt')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {items.map((row) => (
                <Table.Tr key={row.detainId}>
                  <Table.Td miw={240}>
                    <DetainedItemCell row={row} />
                  </Table.Td>
                  <Table.Td>
                    <CharacterCell character={row.owner} />
                  </Table.Td>
                  <Table.Td>
                    <CharacterCell character={row.killer} />
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" fw={600}>
                      {t('detained.cps', { amount: number(row.cost) })}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Stack gap={2}>
                      <Badge size="sm" variant="light" color={detainedStatusColor(row.status)}>
                        {t(`detained.${row.status}`, { defaultValue: row.status })}
                      </Badge>
                      <WindowCell row={row} />
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{time(row.detainedAt)}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  )
}
