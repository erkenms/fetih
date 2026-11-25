import { describe, expect, it } from 'vitest'
import {
  ACCOUNT_STATUS_IDS,
  NON_BAN_STATUS_IDS,
  accountStatusColor,
  authorityColor,
  isAccountBanned,
} from './accountMeta'

describe('accountMeta', () => {
  it('excludes banned status from non-ban status choices', () => {
    expect(ACCOUNT_STATUS_IDS).toContain(5)
    expect(NON_BAN_STATUS_IDS).not.toContain(5)
    expect(isAccountBanned(5)).toBe(true)
    expect(isAccountBanned(2)).toBe(false)
  })

  it('maps statuses and authorities to badge colors', () => {
    expect(accountStatusColor(2)).toBe('green')
    expect(accountStatusColor(5)).toBe('red')
    expect(accountStatusColor(99)).toBe('gray')
    expect(authorityColor(5)).toBe('red')
    expect(authorityColor(1)).toBe('gray')
  })
})
