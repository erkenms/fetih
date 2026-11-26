import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api, unwrap } from '../../api/client'
import { indexLooks, type LookIndex } from './looks'

// The rendered NPC and monster looks; empty until loaded or when the assets aren't built
export function useLooks(): {
  index: LookIndex
  isLoading: boolean
  error: unknown
} {
  const query = useQuery({
    queryKey: ['catalogs', 'looks'],
    queryFn: () => unwrap(api.GET('/api/catalogs/looks')),
    staleTime: Infinity,
  })
  const index = useMemo(() => indexLooks(query.data), [query.data])
  return { index, isLoading: query.isLoading, error: query.error }
}
