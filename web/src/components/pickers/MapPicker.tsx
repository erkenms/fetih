import { Group, Text } from '@mantine/core'
import { IconMap } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { AssetImage } from './AssetImage'
import { CatalogPicker } from './CatalogPicker'
import { formatMapLabel } from './catalogUtils'

type MapEntry = Schemas['MapCatalogEntry']

export interface MapPickerProps {
  value: number | null
  onChange: (id: number | null, map?: MapEntry) => void
  label?: string
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
}

export function MapPicker({
  value,
  onChange,
  label,
  placeholder,
  clearable = true,
  disabled = false,
}: MapPickerProps) {
  const { t } = useTranslation('catalogs')

  return (
    <CatalogPicker<MapEntry>
      value={value}
      onChange={onChange}
      queryKeyPrefix={['catalogs', 'maps']}
      fetchList={(search) =>
        unwrap(api.GET('/api/catalogs/maps', { params: { query: { search, pageSize: 30 } } }))
      }
      fetchOne={(id) =>
        unwrap(api.GET('/api/catalogs/maps/{id}', { params: { path: { id } } }))
      }
      getId={(m) => m.id}
      formatSelected={formatMapLabel}
      label={label ?? t('pickers.map.label')}
      placeholder={placeholder ?? t('pickers.map.placeholder')}
      clearable={clearable}
      disabled={disabled}
      renderOption={(m) => (
        <Group wrap="nowrap" gap="xs">
          <AssetImage path={m.minimap} alt={formatMapLabel(m)} size={28} fallback={<IconMap size={14} />} />
          <div style={{ minWidth: 0 }}>
            <Text size="sm" truncate>
              {formatMapLabel(m)}
            </Text>
            {m.aliases.length > 0 && (
              <Text size="xs" c="dimmed" truncate>
                {m.aliases.join(', ')}
              </Text>
            )}
          </div>
        </Group>
      )}
    />
  )
}
