import { Group, Loader, Stack } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Navigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'

// The old accounts and characters lists
export function PlayersRedirect() {
  return <Navigate to="/admin/players" replace />
}

// Old account links: the account is the player
export function AccountRedirect() {
  const { id } = useParams()
  return <Navigate to={`/admin/players/${id}`} replace />
}

// Links that only know a character go to its account's player page
export function CharacterRedirect() {
  const { id } = useParams()
  const characterId = Number(id)
  const character = useQuery({
    queryKey: ['character', characterId],
    queryFn: () => unwrap(api.GET('/api/characters/{id}', { params: { path: { id: characterId } } })),
    enabled: Number.isFinite(characterId) && characterId > 0,
  })
  if (character.data) return <Navigate to={`/admin/players/${character.data.accountId}`} replace />
  if (character.error)
    return (
      <Stack>
        <ErrorAlert error={character.error} />
      </Stack>
    )
  return (
    <Group justify="center" py="xl">
      <Loader />
    </Group>
  )
}
