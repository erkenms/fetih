import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Me } from '../../../api/client'

export function usePublicAccountConfig() {
  return useQuery({
    queryKey: ['publicAccount', 'config'],
    queryFn: () => unwrap(api.GET('/api/public/account/config')),
    staleTime: 300_000,
  })
}

export function useRegister() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { username: string; password: string; captchaToken?: string | null }) =>
      unwrap(api.POST('/api/public/account/register', { body })),
    onSuccess: (me: Me) => {
      client.clear()
      client.setQueryData(['me'], me)
    },
  })
}

export function useMyAccount() {
  return useQuery({
    queryKey: ['publicAccount', 'profile'],
    queryFn: () => unwrap(api.GET('/api/public/account/profile')),
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrap(api.POST('/api/public/account/password', { body })),
  })
}
