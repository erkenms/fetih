import { LogTimeline } from './LogTimeline'

export function CharacterTimelineTab({ characterId, accountId }: { characterId: number; accountId?: number }) {
  return <LogTimeline characterId={characterId} accountId={accountId} compact />
}
