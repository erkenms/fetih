import { Badge, Group, Text } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { CatalogPicker } from './CatalogPicker'

type Monster = Schemas['MonsterCatalogEntry']

export interface MonsterPickerProps {
  value: number | null
  onChange: (id: number | null, monster?: Monster) => void
  label?: string
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
}

export function MonsterPicker({
  value,
  onChange,
  label,
  placeholder,
  clearable = true,
  disabled = false,
}: MonsterPickerProps) {
  const { t } = useTranslation('catalogs')

  return (
    <CatalogPicker<Monster>
      value={value}
      onChange={onChange}
      queryKeyPrefix={['catalogs', 'monsters']}
      fetchList={(search) =>
        unwrap(api.GET('/api/catalogs/monsters', { params: { query: { search, pageSize: 30 } } }))
      }
      fetchOne={(id) =>
        unwrap(api.GET('/api/catalogs/monsters/{id}', { params: { path: { id } } }))
      }
      getId={(m) => m.id}
      formatSelected={(m) => `${m.name} (#${m.id})`}
      label={label ?? t('pickers.monster.label')}
      placeholder={placeholder ?? t('pickers.monster.placeholder')}
      clearable={clearable}
      disabled={disabled}
      renderOption={(m) => (
        <Group justify="space-between" wrap="nowrap" gap="xs">
          <Text size="sm" truncate>
            {m.name} <Text span c="dimmed" size="xs">#{m.id}</Text>
          </Text>
          <Group gap={4} wrap="nowrap">
            {(m.level ?? m.clientLevel) != null && (
              <Badge size="xs" variant="light">
                Lv.{m.level ?? m.clientLevel}
              </Badge>
            )}
            {m.boss && (
              <Badge size="xs" color="red" variant="light">
                {t('monsters.boss')}
              </Badge>
            )}
          </Group>
        </Group>
      )}
    />
  )
}
