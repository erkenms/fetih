import { Badge, CloseButton, Combobox, Group, Loader, ScrollArea, Text, TextInput, useCombobox } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconSparkles } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { AssetImage } from './AssetImage'

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
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  })
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 200)

  const list = useQuery({
    queryKey: ['catalogs', 'spells', 'picker', debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/spells', {
          params: { query: { search: debounced.trim() || undefined, pageSize: 30 } },
        }),
      ),
    enabled: combobox.dropdownOpened,
  })

  const selected = useQuery({
    queryKey: ['catalogs', 'spells', 'one', value],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/spells/{type}', {
          params: { path: { type: value! } },
        }),
      ),
    enabled: value != null && value >= 0,
    retry: false,
  })

  const displayValue = combobox.dropdownOpened
    ? search
    : selected.data
      ? `${selected.data.name} (#${selected.data.type})`
      : value != null
        ? `#${value}`
        : ''

  return (
    <Combobox
      store={combobox}
      onOptionSubmit={(val) => {
        const type = Number(val)
        const found = list.data?.items.find((s) => s.type === type)
        onChange(type, found)
        setSearch('')
        combobox.closeDropdown()
      }}
    >
      <Combobox.Target>
        <TextInput
          label={label ?? t('pickers.spell.label')}
          placeholder={placeholder ?? t('pickers.spell.placeholder')}
          value={displayValue}
          disabled={disabled}
          leftSection={
            selected.data && !combobox.dropdownOpened ? (
              <AssetImage path={selected.data.icon} alt={selected.data.name} size={20} fallback={<IconSparkles size={14} />} />
            ) : undefined
          }
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
            {list.data?.items.map((s) => (
              <Combobox.Option value={String(s.type)} key={s.type} active={s.type === value}>
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
