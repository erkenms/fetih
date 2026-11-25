import { CloseButton, Combobox, Loader, ScrollArea, TextInput, useCombobox } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useQuery } from '@tanstack/react-query'
import { type ReactNode, useState } from 'react'
import { useTranslation } from 'react-i18next'

export interface CatalogPickerProps<TItem> {
  value: number | null
  onChange: (id: number | null, item?: TItem) => void
  queryKeyPrefix: readonly string[]
  fetchList: (search: string | undefined) => Promise<{ items: TItem[] }>
  fetchOne: (id: number) => Promise<TItem>
  getId: (item: TItem) => number
  formatSelected: (item: TItem) => string
  renderOption: (item: TItem) => ReactNode
  renderLeftSection?: (item: TItem) => ReactNode
  label: string
  placeholder: string
  clearable?: boolean
  disabled?: boolean
  minId?: number
}

export function CatalogPicker<TItem>({
  value,
  onChange,
  queryKeyPrefix,
  fetchList,
  fetchOne,
  getId,
  formatSelected,
  renderOption,
  renderLeftSection,
  label,
  placeholder,
  clearable = true,
  disabled = false,
  minId = 1,
}: CatalogPickerProps<TItem>) {
  const { t } = useTranslation('catalogs')
  const combobox = useCombobox({
    onDropdownClose: () => combobox.resetSelectedOption(),
  })
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 200)

  const list = useQuery({
    queryKey: [...queryKeyPrefix, 'picker', debounced],
    queryFn: () => fetchList(debounced.trim() || undefined),
    enabled: combobox.dropdownOpened,
  })

  const selected = useQuery({
    queryKey: [...queryKeyPrefix, 'one', value],
    queryFn: () => fetchOne(value!),
    enabled: value != null && value >= minId,
    retry: false,
  })

  const displayValue = combobox.dropdownOpened
    ? search
    : selected.data
      ? formatSelected(selected.data)
      : value != null
        ? `#${value}`
        : ''

  return (
    <Combobox
      store={combobox}
      onOptionSubmit={(val) => {
        const id = Number(val)
        const found = list.data?.items.find((item) => getId(item) === id)
        onChange(id, found)
        setSearch('')
        combobox.closeDropdown()
      }}
    >
      <Combobox.Target>
        <TextInput
          label={label}
          placeholder={placeholder}
          value={displayValue}
          disabled={disabled}
          leftSection={
            renderLeftSection && selected.data && !combobox.dropdownOpened
              ? renderLeftSection(selected.data)
              : undefined
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
            {list.data?.items.map((item) => {
              const id = getId(item)
              return (
                <Combobox.Option value={String(id)} key={id} active={id === value}>
                  {renderOption(item)}
                </Combobox.Option>
              )
            })}
            {list.data && list.data.items.length === 0 && (
              <Combobox.Empty>{t('pickers.empty')}</Combobox.Empty>
            )}
          </ScrollArea.Autosize>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  )
}
