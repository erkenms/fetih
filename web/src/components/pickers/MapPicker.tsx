import { CloseButton, Combobox, Group, Loader, ScrollArea, Text, TextInput, useCombobox } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconMap } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { AssetImage } from './AssetImage'
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
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  })
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 200)

  const list = useQuery({
    queryKey: ['catalogs', 'maps', 'picker', debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/maps', {
          params: { query: { search: debounced.trim() || undefined, pageSize: 30 } },
        }),
      ),
    enabled: combobox.dropdownOpened,
  })

  const selected = useQuery({
    queryKey: ['catalogs', 'maps', 'one', value],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/maps/{id}', {
          params: { path: { id: value! } },
        }),
      ),
    enabled: value != null && value > 0,
    retry: false,
  })

  const displayValue = combobox.dropdownOpened
    ? search
    : selected.data
      ? formatMapLabel(selected.data)
      : value != null
        ? `#${value}`
        : ''

  return (
    <Combobox
      store={combobox}
      onOptionSubmit={(val) => {
        const id = Number(val)
        const found = list.data?.items.find((m) => m.id === id)
        onChange(id, found)
        setSearch('')
        combobox.closeDropdown()
      }}
    >
      <Combobox.Target>
        <TextInput
          label={label ?? t('pickers.map.label')}
          placeholder={placeholder ?? t('pickers.map.placeholder')}
          value={displayValue}
          disabled={disabled}
          onFocus={() => {
            setSearch('')
            combobox.openDropdown()
          }}
          onClick={() => combobox.openDropdown()}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            combobox.openDropdown()
          }}
          rightSection={
            list.isFetching ? (
              <Loader size={16} />
            ) : clearable && value != null && !disabled ? (
              <CloseButton
                size="sm"
                aria-label={t('pickers.clear')}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  onChange(null)
                  setSearch('')
                }}
              />
            ) : (
              <Combobox.Chevron />
            )
          }
        />
      </Combobox.Target>

      <Combobox.Dropdown>
        <Combobox.Options>
          <ScrollArea.Autosize mah={240} type="scroll">
            {list.data?.items.map((m) => (
              <Combobox.Option value={String(m.id)} key={m.id} active={m.id === value}>
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
              </Combobox.Option>
            ))}
            {list.data && list.data.items.length === 0 && (
              <Combobox.Empty>{t('pickers.empty')}</Combobox.Empty>
            )}
          </ScrollArea.Autosize>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  )
}
