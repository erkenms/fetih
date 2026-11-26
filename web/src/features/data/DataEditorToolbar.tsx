import { Badge, Button, Group } from '@mantine/core'
import { IconDeviceFloppy, IconPlus, IconRefresh, IconRotate } from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { useStudioReadOnly } from '../../components/StudioReadOnlyBanner'
import { ProblemsAlert } from './ProblemsAlert'

type DataProblem = Schemas['DataProblemDto']

export interface DataEditorToolbarProps {
  children: ReactNode
  dirty: boolean
  addLabel: string
  onAdd: () => void
  onReset: () => void
  onSave: () => void
  savePending: boolean
  saveError: unknown
  onReload: () => void
  reloadPending: boolean
  problems: DataProblem[]
}

export function DataEditorToolbar({
  children,
  dirty,
  addLabel,
  onAdd,
  onReset,
  onSave,
  savePending,
  saveError,
  onReload,
  reloadPending,
  problems,
}: DataEditorToolbarProps) {
  const { t } = useTranslation('data')
  const studioReadOnly = useStudioReadOnly()

  return (
    <>
      <Group justify="space-between" align="center" wrap="wrap">
        <Group gap="sm" wrap="wrap">
          {children}
          {dirty && (
            <Badge color="yellow" variant="light">
              {t('status.unsaved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} disabled={studioReadOnly} onClick={onAdd}>
            {addLabel}
          </Button>
          {dirty && (
            <Button variant="subtle" color="gray" leftSection={<IconRotate size={16} />} onClick={onReset}>
              {t('actions.reset')}
            </Button>
          )}
          <Button
            leftSection={<IconDeviceFloppy size={16} />}
            loading={savePending}
            disabled={studioReadOnly || !dirty}
            onClick={onSave}
          >
            {t('actions.save')}
          </Button>
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            loading={reloadPending}
            disabled={studioReadOnly}
            onClick={onReload}
          >
            {t('actions.reload')}
          </Button>
        </Group>
      </Group>

      {saveError != null && <ErrorAlert error={saveError} />}
      <ProblemsAlert problems={problems} />
    </>
  )
}
