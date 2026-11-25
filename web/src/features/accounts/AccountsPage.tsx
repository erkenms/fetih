import {
  Badge,
  Button,
  Group,
  Loader,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconSearch } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import {
  ACCOUNT_STATUS_IDS,
  AUTHORITY_IDS,
  accountStatusColor,
  authorityColor,
} from './accountMeta'

const pageSize = 25

export default function AccountsPage() {
  const { t, i18n } = useTranslation(['accounts', 'common'])
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()

  const [q, setQ] = useState(() => searchParams.get('q') ?? '')
  const [ip, setIp] = useState(() => searchParams.get('ip') ?? '')
  const [status, setStatus] = useState<string | null>(() => searchParams.get('status'))
  const [authority, setAuthority] = useState<string | null>(() => searchParams.get('authority'))
  const [page, setPage] = useState(1)

  useEffect(() => {
    const paramIp = searchParams.get('ip') ?? ''
    if (paramIp !== ip) {
      setIp(paramIp)
      setPage(1)
    }
  }, [searchParams])

  const [debounced] = useDebouncedValue({ q, ip }, 250)

  const query = {
    q: debounced.q.trim() || undefined,
    ip: debounced.ip.trim() || undefined,
    status: status ? Number(status) : undefined,
    authority: authority ? Number(authority) : undefined,
    page,
    pageSize,
  }

  const accounts = useQuery({
    queryKey: ['accounts', query],
    queryFn: () => unwrap(api.GET('/api/accounts', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = accounts.data?.total ?? 0
  const hasFilters = Boolean(q || ip || status || authority)

  const clearFilters = () => {
    setQ('')
    setIp('')
    setStatus(null)
    setAuthority(null)
    setPage(1)
    setSearchParams({})
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('accounts:title')}</Title>
        {accounts.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('accounts:description')}</Text>

      <Group align="end" wrap="wrap">
        <TextInput
          style={{ flex: 2, minWidth: 240 }}
          placeholder={t('accounts:searchPlaceholder')}
          leftSection={<IconSearch size={16} />}
          value={q}
          onChange={(e) => {
            setQ(e.currentTarget.value)
            setPage(1)
          }}
        />
        <TextInput
          style={{ flex: 1, minWidth: 150 }}
          placeholder={t('accounts:filter.ip')}
          value={ip}
          onChange={(e) => {
            setIp(e.currentTarget.value)
            setPage(1)
          }}
        />
        <Select
          style={{ minWidth: 140 }}
          placeholder={t('accounts:filter.status')}
          clearable
          value={status}
          onChange={(v) => {
            setStatus(v)
            setPage(1)
          }}
          data={ACCOUNT_STATUS_IDS.map((id) => ({
            value: String(id),
            label: t(`accounts:status.${id}` as never),
          }))}
        />
        <Select
          style={{ minWidth: 160 }}
          placeholder={t('accounts:filter.authority')}
          clearable
          value={authority}
          onChange={(v) => {
            setAuthority(v)
            setPage(1)
          }}
          data={AUTHORITY_IDS.map((id) => ({
            value: String(id),
            label: t(`common:authority.${id}` as never),
          }))}
        />
        {hasFilters && (
          <Button variant="subtle" onClick={clearFilters}>
            {t('accounts:filter.clear')}
          </Button>
        )}
      </Group>

      <ErrorAlert error={accounts.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={820}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('accounts:column.id')}</Table.Th>
                <Table.Th>{t('accounts:column.username')}</Table.Th>
                <Table.Th>{t('accounts:column.authority')}</Table.Th>
                <Table.Th>{t('accounts:column.status')}</Table.Th>
                <Table.Th>{t('accounts:column.ip')}</Table.Th>
                <Table.Th>{t('accounts:column.characters')}</Table.Th>
                <Table.Th>{t('accounts:column.lastLogin')}</Table.Th>
                <Table.Th>{t('accounts:column.registered')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {accounts.data?.items.map((account) => (
                <Table.Tr
                  key={account.accountId}
                  style={{ cursor: 'pointer' }}
                  onClick={() => navigate(`/admin/accounts/${account.accountId}`)}
                >
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      #{account.accountId}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" fw={600}>
                      {account.username}
                    </Text>
                    {account.email && account.email !== account.username && (
                      <Text size="xs" c="dimmed">
                        {account.email}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={authorityColor(account.authorityId)}>
                      {t(`common:authority.${account.authorityId}` as never)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={accountStatusColor(account.statusId)}>
                      {t(`accounts:status.${account.statusId}` as never)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{account.ipAddress || '—'}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{account.characterCount}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">
                      {account.lastLogin
                        ? new Date(account.lastLogin).toLocaleString(i18n.language)
                        : '—'}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">
                      {new Date(account.registered).toLocaleDateString(i18n.language)}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              ))}
              {accounts.data && accounts.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={8}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('accounts:empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('accounts:total', { count: total })}
        </Text>
        <Pagination
          total={Math.max(1, Math.ceil(total / pageSize))}
          value={page}
          onChange={setPage}
        />
      </Group>
    </Stack>
  )
}
