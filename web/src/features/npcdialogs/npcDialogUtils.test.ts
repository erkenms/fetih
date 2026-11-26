import { describe, expect, it } from 'vitest'
import { kindBadgeColor, optionKindColor } from './npcDialogUtils'

describe('npcDialogUtils', () => {
  it('maps NPC dialog kinds to badge colors', () => {
    expect(kindBadgeColor('script')).toBe('grape')
    expect(kindBadgeColor('teleporter')).toBe('blue')
    expect(kindBadgeColor('talker')).toBe('teal')
    expect(kindBadgeColor('shop')).toBe('yellow')
    expect(kindBadgeColor('warehouse')).toBe('cyan')
    expect(kindBadgeColor('default')).toBe('gray')
  })

  it('maps dialog option kinds to badge colors', () => {
    expect(optionKindColor('textbox')).toBe('orange')
    expect(optionKindColor('close')).toBe('gray')
    expect(optionKindColor('choice')).toBe('green')
  })
})
