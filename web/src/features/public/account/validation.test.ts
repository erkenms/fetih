import { describe, expect, it } from 'vitest'
import { classKey, isValidEmail, isValidPassword } from './validation'

describe('isValidEmail', () => {
  it('accepts valid ASCII emails', () => {
    expect(isValidEmail('player@example.com')).toBe(true)
    expect(isValidEmail('  Player+1@sub.domain.org  ')).toBe(true)
  })

  it('rejects malformed or non-ASCII emails', () => {
    expect(isValidEmail('')).toBe(false)
    expect(isValidEmail('no-at-sign.com')).toBe(false)
    expect(isValidEmail('a@b')).toBe(false)
    expect(isValidEmail('a@b..c')).toBe(false)
    expect(isValidEmail('üser@example.com')).toBe(false)
    expect(isValidEmail(`${'a'.repeat(120)}@test.com`)).toBe(false)
  })
})

describe('isValidPassword', () => {
  it('accepts 6-16 printable ASCII passwords', () => {
    expect(isValidPassword('secret')).toBe(true)
    expect(isValidPassword('Pass word! 12345')).toBe(true)
  })

  it('rejects too short, too long, or non-ASCII passwords', () => {
    expect(isValidPassword('12345')).toBe(false)
    expect(isValidPassword('a'.repeat(17))).toBe(false)
    expect(isValidPassword('şifre123')).toBe(false)
  })
})

describe('classKey', () => {
  it('maps Conquer class tiers to base class keys', () => {
    expect(classKey(15)).toBe('trojan')
    expect(classKey(21)).toBe('warrior')
    expect(classKey(135)).toBe('waterTaoist')
    expect(classKey(142)).toBe('fireTaoist')
    expect(classKey(999)).toBe('unknown')
  })
})
