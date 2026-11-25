import { describe, expect, it } from 'vitest'
import { isHex, packetSourceFromEntry, parsePastedPacket } from './packet'

const talk = '3300EC03FFFFFF0035080000000000000000000000000000040653595354454D08414C4C555345525300084E45575F524F4C45'

describe('packetSourceFromEntry', () => {
  it('reads sent packets as Out and received ones as In', () => {
    expect(packetSourceFromEntry({ server: 'game', event: 'packet.out', fields: { hex: talk } })).toEqual({
      hex: talk,
      server: 'Game',
      direction: 'Out',
    })
    expect(packetSourceFromEntry({ server: 'account', event: 'packet.in', fields: { hex: talk } })?.server).toBe(
      'Account',
    )
    expect(packetSourceFromEntry({ server: 'game', event: 'packet.unhandled', fields: { hex: talk } })?.direction).toBe(
      'In',
    )
  })

  it('ignores other events and entries without hex', () => {
    expect(packetSourceFromEntry({ server: 'game', event: 'item.bought', fields: { hex: talk } })).toBeNull()
    expect(packetSourceFromEntry({ server: 'game', event: 'packet.in', fields: {} })).toBeNull()
    expect(packetSourceFromEntry({ server: 'game', event: 'packet.in', fields: { hex: 'xyz' } })).toBeNull()
  })
})

describe('parsePastedPacket', () => {
  const fallback = { server: 'Game', direction: 'In' } as const

  it('takes plain hex with spaces', () => {
    expect(parsePastedPacket('1C00 1C04 3022 35AB\n', fallback)).toEqual({ ...fallback, hex: '1C001C04302235AB' })
  })

  it('takes a whole log line and its server and direction', () => {
    const line = JSON.stringify({ server: 'account', event: 'packet.out', hex: talk })
    expect(parsePastedPacket(line, fallback)).toEqual({ hex: talk, server: 'Account', direction: 'Out' })
  })

  it('refuses text that is not a packet', () => {
    expect(parsePastedPacket('hello', fallback)).toBeNull()
    expect(parsePastedPacket('{"msg":"no hex"}', fallback)).toBeNull()
    expect(parsePastedPacket('{broken', fallback)).toBeNull()
    expect(isHex('ABC')).toBe(false)
  })
})
