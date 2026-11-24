import { Anchor, Button, Center, Paper, PasswordInput, Stack, Text, TextInput, Title } from '@mantine/core'
import { useForm } from '@mantine/form'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useNavigate } from 'react-router'
import { useMe } from '../../../app/auth'
import { AccountErrorAlert } from './AccountErrorAlert'
import { usePublicAccountConfig, useRegister } from './api'
import { CaptchaWidget } from './CaptchaWidget'
import { isValidEmail, isValidPassword } from './validation'

export default function RegisterPage() {
  const { t } = useTranslation('publicAccount')
  const navigate = useNavigate()
  const me = useMe()
  const config = usePublicAccountConfig()
  const register = useRegister()
  const [captchaToken, setCaptchaToken] = useState<string | null>(null)
  const [captchaError, setCaptchaError] = useState<string | null>(null)

  const minPassword = config.data?.minPasswordLength ?? 6
  const maxPassword = config.data?.maxPasswordLength ?? 16
  const maxUsername = config.data?.maxUsernameLength ?? 127
  const captchaEnabled = config.data?.captchaEnabled ?? false

  const form = useForm({
    initialValues: { username: '', password: '', confirmPassword: '' },
    validate: {
      username: (v) =>
        !v.trim()
          ? t('validation.required')
          : isValidEmail(v, maxUsername)
            ? null
            : t('validation.invalidEmail', { max: maxUsername }),
      password: (v) =>
        !v
          ? t('validation.required')
          : isValidPassword(v, minPassword, maxPassword)
            ? null
            : t('validation.invalidPassword', { min: minPassword, max: maxPassword }),
      confirmPassword: (v, values) =>
        !v ? t('validation.required') : v === values.password ? null : t('validation.passwordMismatch'),
    },
  })

  if (me.data && !register.isPending) return <Navigate to="/account" replace />

  return (
    <Center mih="70vh">
      <Paper withBorder p="xl" w={420} maw="100%">
        <form
          onSubmit={form.onSubmit((values) => {
            if (captchaEnabled && !captchaToken) {
              setCaptchaError(t('validation.captchaRequired'))
              return
            }
            setCaptchaError(null)
            register.mutate(
              { username: values.username.trim(), password: values.password, captchaToken },
              { onSuccess: () => navigate('/account', { replace: true }) },
            )
          })}
        >
          <Stack>
            <div>
              <Title order={3}>{t('register.title')}</Title>
              <Text size="sm" c="dimmed" mt={4}>
                {t('register.subtitle')}
              </Text>
            </div>
            <AccountErrorAlert error={register.error} />
            <TextInput
              label={t('register.username')}
              type="email"
              autoComplete="email"
              maxLength={maxUsername}
              autoFocus
              {...form.getInputProps('username')}
            />
            <PasswordInput
              label={t('register.password')}
              description={t('register.passwordHint', { min: minPassword, max: maxPassword })}
              autoComplete="new-password"
              maxLength={maxPassword}
              {...form.getInputProps('password')}
            />
            <PasswordInput
              label={t('register.confirmPassword')}
              autoComplete="new-password"
              maxLength={maxPassword}
              {...form.getInputProps('confirmPassword')}
            />
            <CaptchaWidget
              enabled={captchaEnabled}
              siteKey={config.data?.captchaSiteKey}
              onToken={(token) => {
                setCaptchaToken(token)
                if (token) setCaptchaError(null)
              }}
            />
            {captchaError && (
              <Text size="xs" c="red">
                {captchaError}
              </Text>
            )}
            <Button type="submit" loading={register.isPending}>
              {t('register.submit')}
            </Button>
            <Text size="sm" ta="center" c="dimmed">
              {t('register.haveAccount')}{' '}
              <Anchor component={Link} to="/login">
                {t('register.loginLink')}
              </Anchor>
            </Text>
          </Stack>
        </form>
      </Paper>
    </Center>
  )
}
