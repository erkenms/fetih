import { Button, Menu } from '@mantine/core'
import { IconLogout, IconSettings, IconUser } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { can, useLogout, useMe } from './auth'

export function UserMenu() {
  const { t } = useTranslation()
  const me = useMe().data
  const logout = useLogout()
  const navigate = useNavigate()

  if (!me)
    return (
      <Button component={Link} to="/login" variant="default" size="sm">
        {t('shell.login')}
      </Button>
    )

  return (
    <Menu position="bottom-end">
      <Menu.Target>
        <Button variant="default" size="sm" leftSection={<IconUser size={16} />}>
          {me.username}
        </Button>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{t(`authority.${me.authority}`)}</Menu.Label>
        {can(me, 'AdminArea') && (
          <Menu.Item component={Link} to="/admin" leftSection={<IconSettings size={16} />}>
            {t('shell.adminPanel')}
          </Menu.Item>
        )}
        <Menu.Item
          leftSection={<IconLogout size={16} />}
          onClick={() => logout.mutate(undefined, { onSuccess: () => navigate('/') })}
        >
          {t('shell.logout')}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
