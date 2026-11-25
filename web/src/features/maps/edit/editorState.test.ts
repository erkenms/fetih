import { describe, expect, it } from 'vitest'
import {
  canRedo,
  canUndo,
  cellDistance,
  clampSpread,
  createHistory,
  findDuplicateIds,
  isDirty,
  markSaved,
  MAX_HISTORY,
  nextAvailableId,
  pushHistory,
  redoHistory,
  undoHistory,
} from './editorState'

describe('editorState history', () => {
  it('supports undo, redo, and clears future on new push', () => {
    let h = createHistory({ value: 1 })
    expect(canUndo(h)).toBe(false)
    expect(canRedo(h)).toBe(false)
    expect(isDirty(h)).toBe(false)

    h = pushHistory(h, { value: 2 })
    h = pushHistory(h, { value: 3 })
    expect(h.present.value).toBe(3)
    expect(canUndo(h)).toBe(true)
    expect(isDirty(h)).toBe(true)

    h = undoHistory(h)
    expect(h.present.value).toBe(2)
    expect(canRedo(h)).toBe(true)

    h = undoHistory(h)
    expect(h.present.value).toBe(1)
    expect(isDirty(h)).toBe(false)

    h = redoHistory(h)
    expect(h.present.value).toBe(2)

    // Pushing after undo discards redo branch
    h = pushHistory(h, { value: 42 })
    expect(canRedo(h)).toBe(false)
    expect(h.present.value).toBe(42)

    h = markSaved(h)
    expect(isDirty(h)).toBe(false)
  })

  it('ignores no-op pushes with identical snapshots', () => {
    const h0 = createHistory({ a: [1, 2] })
    const h1 = pushHistory(h0, { a: [1, 2] })
    expect(h1).toBe(h0)
  })

  it('caps past stack at MAX_HISTORY', () => {
    let h = createHistory({ n: 0 })
    for (let i = 1; i <= MAX_HISTORY + 25; i++) {
      h = pushHistory(h, { n: i })
    }
    expect(h.past.length).toBe(MAX_HISTORY)
    expect(h.present.n).toBe(MAX_HISTORY + 25)
  })
})

describe('editorState helpers', () => {
  it('computes next available id above both existing and suggested minimum', () => {
    expect(nextAvailableId([10, 20], 50)).toBe(50)
    expect(nextAvailableId([10, 55, 20], 50)).toBe(56)
    expect(nextAvailableId([], 1)).toBe(1)
  })

  it('computes cell distance and clamps spread to 0..255', () => {
    expect(cellDistance(10, 20, 13, 24)).toBe(5)
    expect(clampSpread(-4)).toBe(0)
    expect(clampSpread(14.6)).toBe(15)
    expect(clampSpread(999)).toBe(255)
  })

  it('finds duplicate ids sorted', () => {
    expect(findDuplicateIds([5, 1, 5, 2, 1, 1])).toEqual([1, 5])
    expect(findDuplicateIds([1, 2, 3])).toEqual([])
  })
})
