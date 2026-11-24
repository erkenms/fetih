import { Badge, CloseButton, Combobox, Group, Loader, ScrollArea, Text, TextInput, useCombobox } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'

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
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  })
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 200)

  const list = useQuery({
    queryKey: ['catalogs', 'monsters', 'picker', debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/monsters', {
          params: { query: { search: debounced.trim() || undefined, pageSize: 30 } },
        }),
      ),
    enabled: combobox.dropdownOpened,
  })

  const selected = useQuery({
    queryKey: ['catalogs', 'monsters', 'one', value],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/monsters/{id}', {
          params: { path: { id: value! } },
        }),
      ),
    enabled: value != null && value > 0,
    retry: false,
  })

  const displayValue = combobox.dropdownOpened
    ? search
    : selected.data
      ? `${selected.data.name} (#${selected.data.id})`
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
          label={label ?? t('pickers.monster.label')}
          placeholder={placeholder ?? t('pickers.monster.placeholder')}
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
