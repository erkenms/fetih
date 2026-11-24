import { Button, Center, Paper, PasswordInput, Stack, TextInput, Title } from '@mantine/core'
import { useForm } from '@mantine/form'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useSearchParams } from 'react-router'
import { useLogin, useMe } from './auth'
import { ErrorAlert } from './ErrorAlert'

// Only same-site paths, so a crafted link can't send the user elsewhere after login
function safeNext(next: string | null) {
  return next && next.startsWith('/') && !next.startsWith('//') ? next : null
}

export function LoginPage() {
  const { t } = useTranslation()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const me = useMe()
  const login = useLogin()
  const form = useForm({
    initialValues: { username: '', password: '' },
    validate: {
      username: (v) => (v.trim() ? null : t('login.required')),
      password: (v) => (v ? null : t('login.required')),
    },
  })

  const next = safeNext(params.get('next'))
  if (me.data && !login.isPending) return <Navigate to={next ?? '/'} replace />

  return (
    <Center mih="70vh">
      <Paper withBorder p="xl" w={380} maw="100%">
        <form
          onSubmit={form.onSubmit((values) =>
            login.mutate(values, {
              onSuccess: (account) =>
                navigate(next ?? (account.permissions.includes('AdminArea') ? '/admin' : '/'), { replace: true }),
            }),
          )}
        >
          <Stack>
            <Title order={3}>{t('login.title')}</Title>
            <ErrorAlert error={login.error} />
            <TextInput
              label={t('login.username')}
              autoComplete="username"
              autoFocus
              {...form.getInputProps('username')}
            />
            <PasswordInput
              label={t('login.password')}
              autoComplete="current-password"
              maxLength={16}
              {...form.getInputProps('password')}
            />
            <Button type="submit" loading={login.isPending}>
              {t('login.submit')}
            </Button>
          </Stack>
        </form>
      </Paper>
    </Center>
  )
}
