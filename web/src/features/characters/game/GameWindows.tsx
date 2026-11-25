import { useQuery } from '@tanstack/react-query'
import { useState, type CSSProperties, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../../api/client'
import { ItemTooltip, itemIconUrl } from '../../../components/items'
import { formatProfession } from '../../../lib/gameMeta'
import { guildRankKey } from '../../social/names'
import { mapEquipmentSlots, toCatalogItem, toItemInstance, type CharacterItemEntry } from '../characterMeta'
import { TakeItemModal } from '../TakeItemModal'
import {
  EQUIPMENT_BOXES,
  FRAMED_SLOTS,
  INVENTORY,
  SKILLS,
  STATS_ROWS,
  STATUS,
  type Box,
  type StatusPage,
} from './layout'

type Character = Schemas['CharacterDetailResponse']

const text: CSSProperties = {
  position: 'absolute',
  color: '#ffffff',
  font: '12px/14px Tahoma, Verdana, Arial, sans-serif',
  whiteSpace: 'nowrap',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
}

function at(box: Pick<Box, 'x' | 'y'> & Partial<Box>): CSSProperties {
  return { position: 'absolute', left: box.x, top: box.y, width: box.w, height: box.h }
}

function Art({ src, width, height, children }: { src: string; width: number; height: number; children?: ReactNode }) {
  return (
    <div
      style={{
        position: 'relative',
        width,
        height,
        flexShrink: 0,
        background: `url(/assets/ui/${src}) 0 0 no-repeat`,
        imageRendering: 'pixelated',
      }}
    >
      {children}
    </div>
  )
}

function GameItem({ entry, box, onClick }: { entry: CharacterItemEntry; box: Box; onClick?: () => void }) {
  const catalog = toCatalogItem(entry)
  const instance = toItemInstance(entry)
  return (
    <ItemTooltip item={catalog} instance={instance}>
      <div style={{ ...at(box), cursor: onClick ? 'pointer' : 'default' }} onClick={onClick}>
        <img
          src={itemIconUrl(catalog.icon)}
          alt={catalog.name ?? ''}
          style={{ width: '100%', height: '100%', display: 'block' }}
          onError={(e) => {
            e.currentTarget.src = '/assets/icons/items/default.png'
          }}
        />
        {instance.stackCount && instance.stackCount > 1 ? (
          <span style={{ ...text, right: 2, bottom: 0, textShadow: '1px 1px 0 #000' }}>{instance.stackCount}</span>
        ) : null}
      </div>
    </ItemTooltip>
  )
}

function StatsPage({ ch }: { ch: Character }) {
  const { t } = useTranslation(['characters', 'items', 'social'])
  const values: Record<keyof typeof STATS_ROWS, string | number> = {
    name: ch.name,
    level: ch.level,
    class: formatProfession(ch.currentClass, t, '—'),
    pk: ch.killPoints,
    spouse: !ch.spouse || ch.spouse === 'None' ? t('characters:detail.social.none') : ch.spouse,
    guild: ch.guild?.name ?? t('characters:detail.social.none'),
    position: ch.guild ? t(`social:rank.${guildRankKey(ch.guild.rank)}` as never) : '',
    title: '',
    strength: ch.strength,
    agility: ch.agility,
    vitality: ch.vitality,
    spirit: ch.spirit,
    attributePoints: ch.attributePoints,
  }
  return (
    <>
      {(Object.keys(STATS_ROWS) as (keyof typeof STATS_ROWS)[]).map((key) => (
        <span key={key} style={{ ...text, left: STATS_ROWS[key].x, top: STATS_ROWS[key].y, maxWidth: 196 - STATS_ROWS[key].x }}>
          {values[key]}
        </span>
      ))}
    </>
  )
}

function EquipmentPage({ ch }: { ch: Character }) {
  const bySlot = mapEquipmentSlots(ch.equipment)
  return (
    <>
      {Object.entries(EQUIPMENT_BOXES).map(([slot, box]) => {
        const entry = bySlot.get(Number(slot))
        // Inside the frame drawn in the art; the left hand gets the inventory slot instead
        const inner = FRAMED_SLOTS.has(Number(slot))
          ? { x: box.x + 2, y: box.y + 2, w: box.w - 3, h: box.h - 3 }
          : box
        return (
          <div key={slot}>
            {!FRAMED_SLOTS.has(Number(slot)) && (
              <div
                style={{
                  ...at(box),
                  background: 'url(/assets/ui/item-slot.png) 0 0 / 100% 100% no-repeat',
                }}
              />
            )}
            {entry && <GameItem entry={entry} box={inner} />}
          </div>
        )
      })}
    </>
  )
}

interface SkillRow {
  key: string
  name: string
  icon: string | null
  // The share of the picture the art fills, from its top left
  iconArt: number
  level: number
  experience: number
}

function SkillsPage({ rows }: { rows: SkillRow[] }) {
  const { t, i18n } = useTranslation('characters')
  const [first, setFirst] = useState(0)
  const [selected, setSelected] = useState(0)
  const shown = rows.slice(first, first + SKILLS.rows)
  const current = rows[selected]
  const arrow: CSSProperties = {
    ...text,
    width: 18,
    height: 18,
    textAlign: 'center',
    color: '#f5dfa3',
    cursor: 'pointer',
    userSelect: 'none',
  }
  return (
    <>
      {current && (
        <div style={{ ...text, ...at(SKILLS.info), whiteSpace: 'normal' }}>
          <div>{current.name}</div>
          <div>{t('detail.game.level', { level: current.level })}</div>
          <div>{t('detail.game.experience', { value: current.experience.toLocaleString(i18n.language) })}</div>
        </div>
      )}
      {rows.length === 0 && (
        <span style={{ ...text, left: SKILLS.info.x, top: SKILLS.info.y }}>{t('detail.game.none')}</span>
      )}
      {shown.map((row, i) => {
        const y = SKILLS.rowTop + i * SKILLS.rowPitch
        return (
          <div key={row.key} onMouseEnter={() => setSelected(first + i)}>
            {row.icon && (
              <div style={{ ...at({ x: SKILLS.icon.x, y, w: SKILLS.icon.w, h: SKILLS.icon.h }), overflow: 'hidden' }}>
                <img
                  src={row.icon}
                  alt=""
                  style={{ display: 'block', width: SKILLS.icon.w / row.iconArt, height: SKILLS.icon.h / row.iconArt }}
                  onError={(e) => {
                    e.currentTarget.style.display = 'none'
                  }}
                />
              </div>
            )}
            <div style={{ ...text, left: SKILLS.text.x + 5, top: y + 3, width: SKILLS.text.w - 10 }}>{row.name}</div>
            <div style={{ ...text, left: SKILLS.text.x + 5, top: y + 18, width: SKILLS.text.w - 10, color: '#f5dfa3' }}>
              {t('detail.game.level', { level: row.level })}
            </div>
          </div>
        )
      })}
      {first > 0 && (
        <span style={{ ...arrow, left: SKILLS.up.x, top: SKILLS.up.y }} onClick={() => setFirst(first - SKILLS.rows)}>
          ▲
        </span>
      )}
      {first + SKILLS.rows < rows.length && (
        <span
          style={{ ...arrow, left: SKILLS.down.x, top: SKILLS.down.y }}
          onClick={() => setFirst(first + SKILLS.rows)}
        >
          ▼
        </span>
      )}
    </>
  )
}

function BattlePower({ value }: { value: number | undefined }) {
  const { t } = useTranslation('characters')
  if (value === undefined) return null
  const digits = String(value)
  const { globe, digit } = STATUS
  const left = globe.x - (digits.length * digit.advance) / 2 - (digit.size - digit.advance) / 2
  return (
    <div title={t('detail.game.battlePower', { value })} style={{ pointerEvents: 'auto' }}>
      {[...digits].map((d, i) => (
        <img
          key={i}
          src={`/assets/ui/bp-${d}.png`}
          alt={i === 0 ? digits : ''}
          style={at({ x: left + i * digit.advance, y: globe.y - digit.size / 2, w: digit.size, h: digit.size })}
        />
      ))}
    </div>
  )
}

type WeaponSkills = Record<string, { name: string; icon?: string }>

function useWeaponSkills() {
  return useQuery({
    queryKey: ['assets', 'weaponSkills'],
    queryFn: async (): Promise<WeaponSkills> => {
      const response = await fetch('/assets/weaponSkills.json')
      return response.ok ? ((await response.json()) as WeaponSkills) : {}
    },
    staleTime: Infinity,
  })
}

export function StatusWindow({ ch }: { ch: Character }) {
  const { t } = useTranslation('characters')
  const [page, setPage] = useState<StatusPage>('stats')
  const weaponSkills = useWeaponSkills().data ?? {}

  const proficiencies: SkillRow[] = ch.proficiencies.map((p) => ({
    key: `p${p.type}`,
    name: weaponSkills[p.type]?.name ?? `#${p.type}`,
    icon: weaponSkills[p.type]?.icon ? `/assets/${weaponSkills[p.type].icon}` : null,
    iconArt: 1,
    level: p.level,
    experience: p.experience,
  }))
  const magics: SkillRow[] = ch.magics.map((m) => ({
    key: `m${m.type}`,
    name: m.name || `#${m.type}`,
    icon: `/assets/icons/magic/${m.type}.png`,
    // Skill textures are 64×64 with the art in the top left 50×50
    iconArt: 50 / 64,
    level: m.level,
    experience: m.experience,
  }))

  const pageArt: Record<StatusPage, string> = {
    stats: 'status-stats.png',
    equipment: 'status-equipment.png',
    proficiencies: 'status-skills.png',
    skills: 'status-skills.png',
  }

  return (
    <Art src="status.png" width={STATUS.width} height={STATUS.height}>
      {(Object.keys(STATUS.tabs) as StatusPage[]).map((tab) => (
        <img
          key={tab}
          src={`/assets/ui/tab-${tab}${tab === page ? '-on' : ''}.png`}
          alt={t(`detail.game.tabs.${tab}`)}
          title={t(`detail.game.tabs.${tab}`)}
          style={{
            position: 'absolute',
            left: 0,
            top: STATUS.tabs[tab],
            width: 64,
            height: 64,
            clipPath: `inset(0 ${64 - STATUS.tabSize.w}px ${64 - STATUS.tabSize.h}px 0)`,
            cursor: 'pointer',
          }}
          onClick={() => setPage(tab)}
        />
      ))}
      <BattlePower value={ch.battlePower} />
      <div style={{ ...at(STATUS.page), background: `url(/assets/ui/${pageArt[page]}) 0 0 no-repeat` }}>
        {page === 'stats' && <StatsPage ch={ch} />}
        {page === 'equipment' && <EquipmentPage ch={ch} />}
        {page === 'proficiencies' && <SkillsPage key="proficiencies" rows={proficiencies} />}
        {page === 'skills' && <SkillsPage key="skills" rows={magics} />}
      </div>
    </Art>
  )
}

export function InventoryWindow({
  ch,
  canManage,
  onChanged,
}: {
  ch: Character
  canManage: boolean
  onChanged: () => void
}) {
  const { t, i18n } = useTranslation('characters')
  const [taking, setTaking] = useState<CharacterItemEntry | null>(null)
  const { slot } = INVENTORY
  return (
    <Art src="inventory.png" width={INVENTORY.width} height={INVENTORY.height}>
      {ch.inventory.slice(0, INVENTORY.columns * INVENTORY.rows).map((entry, i) => (
        <GameItem
          key={entry.itemId}
          entry={entry}
          box={{
            x: slot.x + (i % INVENTORY.columns) * slot.pitch + 1,
            y: slot.y + Math.floor(i / INVENTORY.columns) * slot.pitch + 1,
            w: slot.size - 2,
            h: slot.size - 2,
          }}
          onClick={canManage ? () => setTaking(entry) : undefined}
        />
      ))}
      <img src="/assets/ui/silver.png" alt="" title={t('detail.silver')} style={at(INVENTORY.silverIcon)} />
      <img src="/assets/ui/cps.png" alt="" title={t('detail.cp')} style={at(INVENTORY.cpsIcon)} />
      <span style={{ ...text, ...at(INVENTORY.silver), padding: '2px 5px' }}>
        {ch.silver.toLocaleString(i18n.language)}
      </span>
      <span style={{ ...text, ...at(INVENTORY.cps), padding: '2px 5px' }}>
        {ch.conquerPoints.toLocaleString(i18n.language)}
      </span>
      <TakeItemModal
        characterId={ch.characterId}
        characterName={ch.name}
        item={taking}
        onClose={() => setTaking(null)}
        onTaken={() => {
          setTaking(null)
          onChanged()
        }}
      />
    </Art>
  )
}
