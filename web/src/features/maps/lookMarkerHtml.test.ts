import { describe, expect, it } from 'vitest'
import { lookMarkerHtml, lookScale } from './lookMarkerHtml'

describe('lookMarkers', () => {
  it('draws pictures at the client role scale on the max zoom', () => {
    expect(lookScale(5, 5)).toBe(0.75)
    expect(lookScale(3, 5)).toBe(0.1875)
    expect(lookScale(6, 5)).toBe(1.5)
  })

  it('stands the picture on its anchor pixel and keeps the fallback', () => {
    const html = lookMarkerHtml({ image: 'looks/monster/104.png', x: 60, y: 120, zoom: 1.5 }, '<i>dot</i>', {
      below: '<b>Pheasant</b>',
    })
    expect(html).toContain('src="/assets/looks/monster/104.png"')
    expect(html).toContain('left:-60px;top:-120px;transform-origin:60px 120px;--look-zoom:1.5;')
    expect(html).toContain('<div class="fetih-look-fallback"><i>dot</i></div>')
    expect(html).toContain('<b>Pheasant</b>')
  })

  it('shows only the fallback without a picture', () => {
    const html = lookMarkerHtml(null, '<i>dot</i>', { below: '<b>name</b>' })
    expect(html).toContain('fetih-look-none')
    expect(html).not.toContain('<img')
    expect(html).not.toContain('<b>name</b>')
  })
})
