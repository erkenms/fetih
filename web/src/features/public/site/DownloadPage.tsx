import { Alert, Badge, Button, Card, List, Loader, Stack, Text, Title } from '@mantine/core'
import { IconDownload, IconInfoCircle } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'

export default function DownloadPage() {
  const { t } = useTranslation('publicSite')
  const download = useQuery({
    queryKey: ['public', 'download'],
    queryFn: () => unwrap(api.GET('/api/public/download')),
  })

  return (
    <Stack py="md" gap="lg">
      <div>
        <Title order={2}>{t('download.title')}</Title>
        <Text c="dimmed" size="sm">
          {t('download.subtitle')}
        </Text>
      </div>

      <ErrorAlert error={download.error} />

      <Card withBorder p="lg">
        {download.isLoading ? (
          <Loader size="sm" />
        ) : (
          <Stack gap="md" align="flex-start">
            <Badge size="lg" variant="light">
              {t('download.version', { version: download.data?.version ?? '5619' })}
            </Badge>
            {download.data?.url ? (
              <Button component="a" href={download.data.url} target="_blank" rel="noreferrer" leftSection={<IconDownload size={16} />}>
                {t('download.downloadButton')}
              </Button>
            ) : (
              <Alert icon={<IconInfoCircle size={16} />} variant="light">
                {t('download.unavailable')}
              </Alert>
            )}
          </Stack>
        )}
      </Card>

      <Card withBorder p="lg">
        <Title order={4} mb="sm">
          {t('download.stepsTitle')}
        </Title>
        <List type="ordered" spacing="xs">
          <List.Item>{t('download.steps.register')}</List.Item>
          <List.Item>{t('download.steps.extract')}</List.Item>
          <List.Item>{t('download.steps.launch')}</List.Item>
        </List>
      </Card>
    </Stack>
  )
}
