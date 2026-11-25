import { Button, Group, SegmentedControl, Stack, Text, Textarea, Title } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router'
import { PacketDecodeView } from './PacketDecodeView'
import { parsePastedPacket, type PacketDirection, type PacketServer, type PacketSource } from './packet'

export default function PacketsPage() {
  const { t } = useTranslation('packets')
  // A packet opened from the timeline comes in the router state, not the URL: login packets
  // carry the account name
  const opened = (useLocation().state as { packet?: PacketSource } | null)?.packet
  const [text, setText] = useState(opened?.hex ?? '')
  const [server, setServer] = useState<PacketServer>(opened?.server ?? 'Game')
  const [direction, setDirection] = useState<PacketDirection>(opened?.direction ?? 'In')
  const [source, setSource] = useState<PacketSource | null>(opened ?? null)
  const [invalid, setInvalid] = useState(false)

  const decode = () => {
    const parsed = parsePastedPacket(text, { server, direction })
    setInvalid(!parsed)
    if (!parsed) return
    setServer(parsed.server)
    setDirection(parsed.direction)
    setSource(parsed)
  }

  return (
    <Stack>
      <Title order={2}>{t('title')}</Title>
      <Text c="dimmed">{t('description')}</Text>
      <Textarea
        label={t('input')}
        placeholder="1C001C04302235AB…"
        value={text}
        onChange={(e) => setText(e.currentTarget.value)}
        error={invalid ? t('invalidInput') : undefined}
        autosize
        minRows={3}
        maxRows={10}
        styles={{ input: { fontFamily: 'monospace', fontSize: 12 } }}
      />
      <Group align="flex-end">
        <SegmentedControl
          value={server}
          onChange={(v) => setServer(v as PacketServer)}
          data={[
            { value: 'Game', label: t('server.Game') },
            { value: 'Account', label: t('server.Account') },
          ]}
        />
        <SegmentedControl
          value={direction}
          onChange={(v) => setDirection(v as PacketDirection)}
          data={[
            { value: 'In', label: t('direction.In') },
            { value: 'Out', label: t('direction.Out') },
          ]}
        />
        <Button onClick={decode}>{t('decode')}</Button>
      </Group>
      {source && <PacketDecodeView key={`${source.server}-${source.direction}-${source.hex}`} source={source} />}
    </Stack>
  )
}
