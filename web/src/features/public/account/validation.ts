// Mirrors Comet.Account.Database.Repositories.AccountRules so invalid inputs fail before hitting the API.

export function isValidEmail(raw: string, maxLength = 127): boolean {
  const username = raw.trim()
  if (!username || username.length > maxLength) return false
  for (let i = 0; i < username.length; i++) {
    const code = username.charCodeAt(i)
    if (code <= 0x20 || code >= 0x7f) return false
  }
  const at = username.indexOf('@')
  if (at <= 0 || at !== username.lastIndexOf('@')) return false
  const domain = username.slice(at + 1)
  const dot = domain.lastIndexOf('.')
  if (dot <= 0 || dot === domain.length - 1 || domain.includes('..')) return false
  return true
}

export function isValidPassword(password: string, minLength = 6, maxLength = 16): boolean {
  if (password.length < minLength || password.length > maxLength) return false
  for (let i = 0; i < password.length; i++) {
    const code = password.charCodeAt(i)
    if (code < 0x20 || code >= 0x7f) return false
  }
  return true
}

export { classKey } from '../../../lib/gameMeta'
