import { LogTimeline } from './LogTimeline'

export function AccountTimelineTab({ accountId }: { accountId: number }) {
  return <LogTimeline accountId={accountId} compact />
}
