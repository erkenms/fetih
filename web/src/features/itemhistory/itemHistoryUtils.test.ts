import { describe, expect, it } from 'vitest'
import { eventBadgeColor, parseItemIdInput, positionCategory, rangeToBounds } from './itemHistoryUtils'

describe('itemHistoryUtils', () => {
  it('parses valid positive uint32 item IDs and rejects invalid input', () => {
    expect(parseItemIdInput('12345')).toBe(12345)
    expect(parseItemIdInput('  55001 ')).toBe(55001)
    expect(parseItemIdInput('0')).toBeNull()
    expect(parseItemIdInput('-5')).toBeNull()
    expect(parseItemIdInput('abc')).toBeNull()
    expect(parseItemIdInput('999999999999')).toBeNull()
  })

  it('computes ISO time bounds for preset windows', () => {
    const now = new Date('2026-11-25T12:00:00.000Z')
    const bounds = rangeToBounds('7d', now)
    expect(bounds.to).toBe('2026-11-25T12:00:00.000Z')
    expect(bounds.from).toBe('2026-11-18T12:00:00.000Z')
  })

  it('maps event types to badge colors and positions to categories', () => {
    expect(eventBadgeColor('item.created')).toBe('green')
    expect(eventBadgeColor('trade.completed')).toBe('blue')
    expect(eventBadgeColor('booth.sold')).toBe('blue')
    expect(eventBadgeColor('item.sold')).toBe('red')
    expect(eventBadgeColor('item.upgraded')).toBe('grape')

    expect(positionCategory(0)).toBe('inventory')
    expect(positionCategory(4)).toBe('equipped')
    expect(positionCategory(201)).toBe('warehouse')
    expect(positionCategory(250)).toBe('detained')
  })
})
