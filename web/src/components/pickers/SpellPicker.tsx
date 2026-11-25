import { Badge, Group, Text } from '@mantine/core'
import { IconSparkles } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { AssetImage } from './AssetImage'
import { CatalogPicker } from './CatalogPicker'

type Spell = Schemas['SpellCatalogEntry']

export interface SpellPickerProps {
  value: number | null
  onChange: (type: number | null, spell?: Spell) => void
  label?: string
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
}

export function SpellPicker({
  value,
  onChange,
  label,
  placeholder,
  clearable = true,
  disabled = false,
}: SpellPickerProps) {
  const { t } = useTranslation('catalogs')

  return (
    <CatalogPicker<Spell>
      value={value}
      onChange={onChange}
      queryKeyPrefix={['catalogs', 'spells']}
      minId={0}
      fetchList={(search) =>
        unwrap(api.GET('/api/catalogs/spells', { params: { query: { search, pageSize: 30 } } }))
      }
      fetchOne={(type) =>
        unwrap(api.GET('/api/catalogs/spells/{type}', { params: { path: { type } } }))
      }
      getId={(s) => s.type}
      formatSelected={(s) => `${s.name} (#${s.type})`}
      label={label ?? t('pickers.spell.label')}
      placeholder={placeholder ?? t('pickers.spell.placeholder')}
      clearable={clearable}
      disabled={disabled}
      renderLeftSection={(s) => (
        <AssetImage path={s.icon} alt={s.name} size={20} fallback={<IconSparkles size={14} />} />
      )}
      renderOption={(s) => (
        <Group justify="space-between" wrap="nowrap" gap="xs">
          <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
            <AssetImage path={s.icon} alt={s.name} size={24} fallback={<IconSparkles size={14} />} />
            <Text size="sm" truncate>
              {s.name} <Text span c="dimmed" size="xs">#{s.type}</Text>
            </Text>
          </Group>
          <Badge size="xs" variant="light">
            {s.sort}
          </Badge>
        </Group>
      )}
    />
  )
}
