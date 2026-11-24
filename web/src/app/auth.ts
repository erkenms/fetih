import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Me, type Permission } from '../api/client'

const meKey = ['me'] as const

// The signed-in account, or null when nobody is signed in
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async (): Promise<Me | null> => {
      try {
        return await unwrap(api.GET('/api/me'))
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: 60_000,
  })
}

export function can(me: Me | null | undefined, permission: Permission) {
  return !!me && me.permissions.includes(permission)
}

export function useCan(permission: Permission) {
  return can(useMe().data, permission)
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { username: string; password: string }) =>
      unwrap(api.POST('/api/auth/login', { body })),
    onSuccess: (me) => {
      client.clear()
      client.setQueryData(meKey, me)
    },
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/auth/logout')),
    onSuccess: () => {
      client.clear()
      client.setQueryData(meKey, null)
    },
  })
}

