const DISCORD_WEBHOOK_RE =
  /^https:\/\/(?:(?:ptb|canary)\.)?discord(?:app)?\.com\/api(?:\/v\d+)?\/webhooks\/\d{15,22}\/[A-Za-z0-9._-]{20,}$/i

export function isValidDiscordWebhookUrl(url: string): boolean {
  const trimmed = url.trim()
  return trimmed.length > 0 && trimmed.length <= 512 && DISCORD_WEBHOOK_RE.test(trimmed)
}

export function formatCooldown(seconds: number): string {
  if (seconds <= 0) return '0s'
  if (seconds >= 3600 && seconds % 3600 === 0) return `${seconds / 3600}h`
  if (seconds >= 60 && seconds % 60 === 0) return `${seconds / 60}m`
  return `${seconds}s`
}
