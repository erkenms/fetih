import { Anchor, Badge, Card, Group, Loader, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { api, ApiError, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { clanRankKey, classKey, formatNumber, guildRankKey, subClassKey } from './format'
import { PlayerAvatar } from './PlayerAvatar'

export default function CharacterPage() {
  const { id } = useParams<{ id: string }>()
  const { t, i18n } = useTranslation('publicSite')
  const charId = Number(id)

  const query = useQuery({
    queryKey: ['public', 'character', charId],
    queryFn: () => unwrap(api.GET('/api/public/characters/{id}', { params: { path: { id: charId } } })),
    enabled: Number.isFinite(charId) && charId > 0,
  })

  if (query.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (query.error instanceof ApiError && query.error.status === 404) {
    return (
      <Stack py="xl">
        <Title order={2}>{t('character.notFound')}</Title>
      </Stack>
    )
  }

  const c = query.data
  if (!c) return <ErrorAlert error={query.error} />

  const lineage = [c.previousClass, c.ancestorClass].filter((x) => x > 0).map((x) => t(`classes.${classKey(x)}`))

  return (
    <Stack gap="lg" py="md">
      <Card withBorder p="lg">
        <Group justify="space-between" wrap="wrap" gap="md">
          <Group gap="md">
            <PlayerAvatar avatar={c.avatar} name={c.name} size="xl" />
            <Stack gap={4}>
              <Group gap="sm">
                <Title order={2}>{c.name}</Title>
                <Badge size="lg" variant="light">
                  {t('character.level', { level: c.level })}
                </Badge>
                {c.rebirths > 0 && (
                  <Badge size="lg" color="grape" variant="light">
                    {t('character.rebirths', { count: c.rebirths })}
                  </Badge>
                )}
              </Group>
              <Text c="dimmed">{t(`classes.${classKey(c.currentClass)}`)}</Text>
            </Stack>
          </Group>
          {c.nobility && c.nobility.rank !== 'Commoner' && (
            <Stack align="flex-end" gap={2}>
              <Badge size="lg" color="yellow" variant="light">
                {t(`nobilityRanks.${c.nobility.rank}`)}
              </Badge>
              {c.nobility.position && (
                <Text size="xs" c="dimmed">
                  {t('character.nobilityPlace', { place: c.nobility.position })}
                </Text>
              )}
            </Stack>
          )}
        </Group>
      </Card>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        <Card withBorder>
          <Title order={4} mb="sm">
            {t('character.title')}
          </Title>
          <Table>
            <Table.Tbody>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.class')}</Table.Td>
                <Table.Td>{t(`classes.${classKey(c.currentClass)}`)}</Table.Td>
              </Table.Tr>
              {lineage.length > 0 && (
                <Table.Tr>
                  <Table.Td c="dimmed">{t('character.lineage')}</Table.Td>
                  <Table.Td>{lineage.join(' / ')}</Table.Td>
                </Table.Tr>
              )}
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.spouse')}</Table.Td>
                <Table.Td>{c.spouse ?? t('character.none')}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.guild')}</Table.Td>
                <Table.Td>
                  {c.guild ? (
                    <Group gap="xs">
                      <Anchor component={Link} to={`/guilds/${c.guild.guildId}`} fw={500}>
                        {c.guild.name}
                      </Anchor>
                      <Text size="xs" c="dimmed">
                        ({t(`guildRanks.${guildRankKey(c.guild.rank)}`)})
                      </Text>
                    </Group>
                  ) : (
                    t('character.none')
                  )}
                </Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.family')}</Table.Td>
                <Table.Td>
                  {c.family ? `${c.family.name} (${t(`clanRanks.${clanRankKey(c.family.rank)}`)})` : t('character.none')}
                </Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.pkPoints')}</Table.Td>
                <Table.Td>{c.pkPoints}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.virtue')}</Table.Td>
                <Table.Td>{formatNumber(c.virtue, i18n.language)}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.quizPoints')}</Table.Td>
                <Table.Td>{formatNumber(c.quizPoints, i18n.language)}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('character.registered')}</Table.Td>
                <Table.Td>{new Date(c.registered).toLocaleDateString(i18n.language)}</Table.Td>
              </Table.Tr>
            </Table.Tbody>
          </Table>
        </Card>

        <Stack gap="md">
          {c.arena && (
            <Card withBorder>
              <Title order={4} mb="sm">
                {t('character.arena')}
              </Title>
              <Table>
                <Table.Tbody>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('character.arenaPoints')}</Table.Td>
                    <Table.Td>{formatNumber(c.arena.points, i18n.language)}</Table.Td>
                  </Table.Tr>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('character.arenaHonor')}</Table.Td>
                    <Table.Td>{formatNumber(c.arena.honorTotal, i18n.language)}</Table.Td>
                  </Table.Tr>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('character.arenaRecord')}</Table.Td>
                    <Table.Td>
                      {c.arena.winsTotal} / {c.arena.lossesTotal}
                    </Table.Td>
                  </Table.Tr>
                </Table.Tbody>
              </Table>
            </Card>
          )}

          {c.flowers && (
            <Card withBorder>
              <Title order={4} mb="sm">
                {t('character.flowers')}
              </Title>
              <Table>
                <Table.Tbody>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('rankings.filters.redRose')}</Table.Td>
                    <Table.Td>{formatNumber(c.flowers.redRoses, i18n.language)}</Table.Td>
                  </Table.Tr>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('rankings.filters.lily')}</Table.Td>
                    <Table.Td>{formatNumber(c.flowers.lilies, i18n.language)}</Table.Td>
                  </Table.Tr>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('rankings.filters.orchid')}</Table.Td>
                    <Table.Td>{formatNumber(c.flowers.orchids, i18n.language)}</Table.Td>
                  </Table.Tr>
                  <Table.Tr>
                    <Table.Td c="dimmed">{t('rankings.filters.tulip')}</Table.Td>
                    <Table.Td>{formatNumber(c.flowers.tulips, i18n.language)}</Table.Td>
                  </Table.Tr>
                </Table.Tbody>
              </Table>
            </Card>
          )}

          {c.subClasses.length > 0 && (
            <Card withBorder>
              <Title order={4} mb="sm">
                {t('character.subclasses')}
              </Title>
              <Table>
                <Table.Tbody>
                  {c.subClasses.map((s) => (
                    <Table.Tr key={s.subClassId}>
                      <Table.Td>
                        <Group gap="xs">
                          <Text>{t(`subClasses.${subClassKey(s.subClassId)}`)}</Text>
                          {s.active && (
                            <Badge size="xs" variant="light">
                              {t('character.active')}
                            </Badge>
                          )}
                        </Group>
                      </Table.Td>
                      <Table.Td>
                        Lv. {s.level} · {t('character.phase', { phase: s.phase })}
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Card>
          )}
        </Stack>
      </SimpleGrid>
    </Stack>
  )
}
