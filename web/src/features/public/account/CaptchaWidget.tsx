import { useEffect, useRef } from 'react'

declare global {
  interface Window {
    turnstile?: {
      render: (
        el: HTMLElement,
        options: {
          sitekey: string
          callback: (token: string) => void
          'expired-callback'?: () => void
          'error-callback'?: () => void
        },
      ) => string
      reset: (widgetId: string) => void
      remove: (widgetId: string) => void
    }
  }
}

interface CaptchaWidgetProps {
  enabled: boolean
  siteKey?: string | null
  onToken: (token: string | null) => void
}

const scriptId = 'cf-turnstile-script'

export function CaptchaWidget({ enabled, siteKey, onToken }: CaptchaWidgetProps) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const widgetIdRef = useRef<string | null>(null)
  const onTokenRef = useRef(onToken)
  onTokenRef.current = onToken

  useEffect(() => {
    if (!enabled || !siteKey) return

    let cancelled = false

    const renderWidget = () => {
      if (cancelled || !containerRef.current || !window.turnstile) return
      if (widgetIdRef.current) {
        window.turnstile.remove(widgetIdRef.current)
        widgetIdRef.current = null
      }
      widgetIdRef.current = window.turnstile.render(containerRef.current, {
        sitekey: siteKey,
        callback: (token) => onTokenRef.current(token),
        'expired-callback': () => onTokenRef.current(null),
        'error-callback': () => onTokenRef.current(null),
      })
    }

    if (window.turnstile) {
      renderWidget()
    } else {
      let script = document.getElementById(scriptId) as HTMLScriptElement | null
      if (!script) {
        script = document.createElement('script')
        script.id = scriptId
        script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
        script.async = true
        script.defer = true
        document.head.appendChild(script)
      }
      script.addEventListener('load', renderWidget)
      return () => {
        cancelled = true
        script?.removeEventListener('load', renderWidget)
        if (widgetIdRef.current && window.turnstile) {
          window.turnstile.remove(widgetIdRef.current)
          widgetIdRef.current = null
        }
      }
    }

    return () => {
      cancelled = true
      if (widgetIdRef.current && window.turnstile) {
        window.turnstile.remove(widgetIdRef.current)
        widgetIdRef.current = null
      }
    }
  }, [enabled, siteKey])

  if (!enabled || !siteKey) return null
  return <div ref={containerRef} />
}
