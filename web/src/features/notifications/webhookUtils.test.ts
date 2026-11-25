import { describe, expect, it } from 'vitest'
import { formatCooldown, isValidDiscordWebhookUrl } from './webhookUtils'

describe('notification webhook helpers', () => {
  it('validates Discord webhook URLs', () => {
    expect(
      isValidDiscordWebhookUrl(
        'https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456',
      ),
    ).toBe(true)
    expect(
      isValidDiscordWebhookUrl(
        'https://canary.discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz123456',
      ),
    ).toBe(true)
    expect(isValidDiscordWebhookUrl('https://example.com/api/webhooks/1/abc')).toBe(false)
    expect(isValidDiscordWebhookUrl('')).toBe(false)
  })

  it('formats cooldown durations', () => {
    expect(formatCooldown(0)).toBe('0s')
    expect(formatCooldown(45)).toBe('45s')
    expect(formatCooldown(120)).toBe('2m')
    expect(formatCooldown(3600)).toBe('1h')
  })
})
