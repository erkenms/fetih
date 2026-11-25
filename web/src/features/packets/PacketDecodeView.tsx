import { ActionIcon, Alert, Badge, Code, Group, Loader, ScrollArea, Stack, Table, Text, Tooltip } from '@mantine/core'
import { IconChevronDown, IconChevronRight } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import type { PacketSource } from './packet'

type Field = Schemas['PacketField']
type Decoded = Schemas['PacketDecodeResponse']

// Lists longer than this start folded
const openListLimit = 12

export function PacketDecodeView({ source }: { source: PacketSource }) {
  const decoded = useQuery({
    queryKey: ['packets', 'decode', source],
    queryFn: () => unwrap(api.POST('/api/packets/decode', { body: source })),
    staleTime: Infinity,
  })

  if (decoded.isPending)
    return (
      <Group justify="center" py="sm">
        <Loader size="sm" />
      </Group>
    )
  if (decoded.error) return <ErrorAlert error={decoded.error} namespace="packets" />
  return <DecodedPacket packet={decoded.data} source={source} />
}

function DecodedPacket({ packet, source }: { packet: Decoded; source: PacketSource }) {
  const { t } = useTranslation('packets')
  const readerColor = packet.reader === 'Server' ? 'green' : packet.reader === 'Client' ? 'blue' : 'gray'

  return (
    <Stack gap="xs">
      <Group gap="xs" wrap="wrap">
        <Text fw={700}>{packet.typeName ?? t('unknownType')}</Text>
        <Code>{packet.type}</Code>
        <Badge variant="light" color="gray" size="sm">
          {t(`server.${source.server}`)} · {t(`direction.${source.direction}`)}
        </Badge>
        <Tooltip label={packet.readerName ?? t('reader.noneHint')} withArrow>
          <Badge variant="light" color={readerColor} size="sm">
            {t(`reader.${packet.reader}`)}
          </Badge>
        </Tooltip>
        {source.direction === 'In' && !packet.handled && (
          <Badge variant="light" color="yellow" size="sm">
            {t('unhandled')}
          </Badge>
        )}
      </Group>
      <Text size="sm" c="dimmed">
        {t('size', { length: packet.length, declared: packet.declaredLength })}
        {packet.seal && <> · {t('seal', { seal: packet.seal })}</>}
        {packet.declaredLength > packet.length && <> · {t('short')}</>}
      </Text>

      {packet.error && (
        <Alert color="red" p="xs">
          {t('readerError', { error: packet.error })}
        </Alert>
      )}

      {packet.reader === 'None' && (
        <Text size="sm" c="dimmed">
          {packet.typeName ? t('noReader') : t('notAGamePacket')}
        </Text>
      )}

      {packet.fields.length > 0 && (
        <ScrollArea.Autosize mah={420}>
          <Table striped withRowBorders={false} verticalSpacing={2} fz="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.field')}</Table.Th>
                <Table.Th>{t('column.value')}</Table.Th>
                <Table.Th>{t('column.type')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {packet.fields.map((field, i) => (
                <FieldRows key={`${field.name}-${i}`} field={field} depth={0} />
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea.Autosize>
      )}

      <Text fw={600} size="sm">
        {t('dump')}
      </Text>
      <ScrollArea.Autosize mah={260}>
        <Code block fz={11}>
          {packet.dump}
        </Code>
      </ScrollArea.Autosize>
    </Stack>
  )
}

function FieldRows({ field, depth }: { field: Field; depth: number }) {
  const { t } = useTranslation('packets')
  const children = field.children ?? []
  const nested = field.kind === 'List' || field.kind === 'Object'
  const [open, setOpen] = useState(children.length <= openListLimit)

  return (
    <>
      <Table.Tr>
        <Table.Td style={{ paddingLeft: 8 + depth * 16, whiteSpace: 'nowrap' }}>
          <Group gap={4} wrap="nowrap">
            {nested && children.length > 0 ? (
              <ActionIcon
                variant="subtle"
                size="xs"
                onClick={() => setOpen((o) => !o)}
                aria-label={open ? t('collapse') : t('expand')}
              >
                {open ? <IconChevronDown size={12} /> : <IconChevronRight size={12} />}
              </ActionIcon>
            ) : (
              <span style={{ width: 18 }} />
            )}
            <Text span fw={depth === 0 ? 600 : 400} size="sm">
              {field.name}
            </Text>
          </Group>
        </Table.Td>
        <Table.Td style={{ wordBreak: 'break-all' }}>
          <FieldValue field={field} />
        </Table.Td>
        <Table.Td>
          <Text span size="xs" c="dimmed">
            {field.typeName}
          </Text>
        </Table.Td>
      </Table.Tr>
      {nested && open && children.map((child, i) => <FieldRows key={`${child.name}-${i}`} field={child} depth={depth + 1} />)}
    </>
  )
}

function FieldValue({ field }: { field: Field }) {
  const { t } = useTranslation('packets')
  switch (field.kind) {
    case 'Null':
      return <Text span size="sm" c="dimmed">null</Text>
    case 'List':
      return (
        <Text span size="sm" c="dimmed">
          {t('items', { count: Number(field.value ?? 0) })}
        </Text>
      )
    case 'Object':
      return field.value ? <Text span size="sm" c="dimmed">{field.value}</Text> : null
    case 'Text':
      return <Code>{JSON.stringify(field.value ?? '')}</Code>
    default:
      return <Code>{field.value}</Code>
  }
}
