export const ACCOUNT_STATUS_IDS = [1, 2, 3, 4, 5] as const
export const NON_BAN_STATUS_IDS = [1, 2, 3, 4] as const
export const AUTHORITY_IDS = [1, 2, 3, 4, 5] as const

export function accountStatusColor(statusId: number): string {
  switch (statusId) {
    case 1:
      return 'blue'
    case 2:
      return 'green'
    case 3:
      return 'yellow'
    case 4:
      return 'orange'
    case 5:
      return 'red'
    default:
      return 'gray'
  }
}

export function authorityColor(authorityId: number): string {
  switch (authorityId) {
    case 5:
      return 'red'
    case 4:
      return 'violet'
    case 3:
      return 'orange'
    case 2:
      return 'cyan'
    default:
      return 'gray'
  }
}

export function isAccountBanned(statusId: number): boolean {
  return statusId === 5
}
