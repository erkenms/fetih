import { Badge, Card, Group, Loader, Pagination, Stack, Text, Title } from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'

const pageSize = 20

export default function NewsPage() {
  const { t, i18n } = useTranslation('publicSite')
  const [page, setPage] = useState(1)

  const news = useQuery({
    queryKey: ['public', 'news', page],
    queryFn: () => unwrap(api.GET('/api/public/news', { params: { query: { page, pageSize } } })),
    placeholderData: keepPreviousData,
  })

  const total = news.data?.total ?? 0

  return (
    <Stack py="md">
      <Group justify="space-between">
        <div>
          <Title order={2}>{t('news.title')}</Title>
          <Text c="dimmed" size="sm">
            {t('news.subtitle')}
          </Text>
        </div>
        {news.isFetching && <Loader size="sm" />}
      </Group>

      <ErrorAlert error={news.error} />

      <Stack gap="sm">
        {news.data?.items.map((item) => (
          <Card key={item.hash} withBorder>
            <Group justify="space-between" mb={item.body ? 'xs' : 0}>
              <Group gap="xs">
                <Badge variant="outline" size="sm">
                  {item.shortHash}
                </Badge>
                <Text fw={600}>{item.subject}</Text>
              </Group>
              <Text size="xs" c="dimmed">
                {new Date(item.date).toLocaleString(i18n.language)}
              </Text>
            </Group>
            {item.body && (
              <Text size="sm" c="dimmed" style={{ whiteSpace: 'pre-wrap' }}>
                {item.body}
              </Text>
            )}
          </Card>
        ))}
        {news.data && news.data.items.length === 0 && (
          <Text c="dimmed" py="md">
            {t('news.empty')}
          </Text>
        )}
      </Stack>

      <Group justify="flex-end">
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>
    </Stack>
  )
}
