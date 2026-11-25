import { ErrorAlert } from '../../../app/ErrorAlert'

export function AccountErrorAlert({ error }: { error: unknown }) {
  return <ErrorAlert error={error} namespace="publicAccount" />
}
