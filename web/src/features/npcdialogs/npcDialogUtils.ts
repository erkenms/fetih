export type DialogRange = '24h' | '7d'

export function kindBadgeColor(kind: string): string {
  switch (kind) {
    case 'script':
      return 'grape'
    case 'teleporter':
      return 'blue'
    case 'talker':
      return 'teal'
    case 'shop':
      return 'yellow'
    case 'warehouse':
      return 'cyan'
    default:
      return 'gray'
  }
}

export function optionKindColor(kind: string): string {
  switch (kind) {
    case 'textbox':
      return 'orange'
    case 'close':
      return 'gray'
    default:
      return 'green'
  }
}

