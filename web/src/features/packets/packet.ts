export type PacketServer = 'Game' | 'Account'
export type PacketDirection = 'In' | 'Out'

export interface PacketSource {
  hex: string
  server: PacketServer
  direction: PacketDirection
}

// The parts of a log entry that say which packet it carries
export interface PacketLogLine {
  server?: string | null
  event?: string | null
  fields?: Record<string, string> | null
}

const packetEvents = new Set(['packet.in', 'packet.out', 'packet.unhandled', 'packet.error'])

export function isPacketEvent(event: string | null | undefined): boolean {
  return !!event && packetEvents.has(event)
}

export function packetServer(server: string | null | undefined): PacketServer {
  return server?.toLowerCase() === 'account' ? 'Account' : 'Game'
}

// A timeline entry with a packet in it: packet.out was sent by the server, every other
// packet event is one the server received
export function packetSourceFromEntry(entry: PacketLogLine): PacketSource | null {
  if (!isPacketEvent(entry.event)) return null
  const hex = entry.fields?.hex
  if (!hex || !isHex(hex)) return null
  return {
    hex,
    server: packetServer(entry.server),
    direction: entry.event === 'packet.out' ? 'Out' : 'In',
  }
}

export function isHex(text: string): boolean {
  const clean = stripHex(text)
  return clean.length >= 8 && clean.length % 2 === 0 && /^[0-9a-f]+$/i.test(clean)
}

export function stripHex(text: string): string {
  return text.replace(/[\s:-]/g, '')
}

// What the paste box accepts: plain hex, or a whole JSON log line from logs/*.jsonl or Loki
export function parsePastedPacket(text: string, fallback: Omit<PacketSource, 'hex'>): PacketSource | null {
  const trimmed = text.trim()
  if (trimmed.startsWith('{')) {
    try {
      const line = JSON.parse(trimmed) as Record<string, unknown>
      const hex = typeof line.hex === 'string' ? line.hex : null
      if (!hex || !isHex(hex)) return null
      const event = typeof line.event === 'string' ? line.event : null
      return {
        hex: stripHex(hex),
        server: typeof line.server === 'string' ? packetServer(line.server) : fallback.server,
        direction: event === 'packet.out' ? 'Out' : event && isPacketEvent(event) ? 'In' : fallback.direction,
      }
    } catch {
      return null
    }
  }
  return isHex(trimmed) ? { ...fallback, hex: stripHex(trimmed) } : null
}
