import { describe, expect, it } from 'vitest'
import { parseUnifiedDiff } from './diff'

describe('parseUnifiedDiff', () => {
  it('returns empty array for blank diff', () => {
    expect(parseUnifiedDiff('')).toEqual([])
    expect(parseUnifiedDiff('   \n')).toEqual([])
  })

  it('parses headers, hunks, additions, removals, and context with line numbers', () => {
    const sample = [
      'diff --git a/npcs.json b/npcs.json',
      '--- a/npcs.json',
      '+++ b/npcs.json',
      '@@ -10,3 +10,3 @@',
      '   { "id": 1 },',
      '-  { "id": 2, "x": 100 },',
      '+  { "id": 2, "x": 101 },',
      '   { "id": 3 },',
      '',
    ].join('\n')

    expect(parseUnifiedDiff(sample)).toEqual([
      { kind: 'header', oldLine: null, newLine: null, text: 'diff --git a/npcs.json b/npcs.json' },
      { kind: 'header', oldLine: null, newLine: null, text: '--- a/npcs.json' },
      { kind: 'header', oldLine: null, newLine: null, text: '+++ b/npcs.json' },
      { kind: 'hunk', oldLine: null, newLine: null, text: '@@ -10,3 +10,3 @@' },
      { kind: 'context', oldLine: 10, newLine: 10, text: '  { "id": 1 },' },
      { kind: 'remove', oldLine: 11, newLine: null, text: '  { "id": 2, "x": 100 },' },
      { kind: 'add', oldLine: null, newLine: 11, text: '  { "id": 2, "x": 101 },' },
      { kind: 'context', oldLine: 12, newLine: 12, text: '  { "id": 3 },' },
    ])
  })
})
