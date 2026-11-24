export type DiffLineKind = 'header' | 'hunk' | 'add' | 'remove' | 'context'

export interface DiffLine {
  kind: DiffLineKind
  oldLine: number | null
  newLine: number | null
  text: string
}

const hunkHeaderRe = /^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/

export function parseUnifiedDiff(diff: string): DiffLine[] {
  if (!diff.trim()) return []

  const lines = diff.replace(/\r\n/g, '\n').split('\n')
  if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop()

  const result: DiffLine[] = []
  let oldNum = 0
  let newNum = 0
  let inHunk = false

  for (const line of lines) {
    const hunk = hunkHeaderRe.exec(line)
    if (hunk) {
      oldNum = Number(hunk[1])
      newNum = Number(hunk[2])
      inHunk = true
      result.push({ kind: 'hunk', oldLine: null, newLine: null, text: line })
      continue
    }

    if (!inHunk) {
      result.push({ kind: 'header', oldLine: null, newLine: null, text: line })
      continue
    }

    if (line.startsWith('+')) {
      result.push({ kind: 'add', oldLine: null, newLine: newNum++, text: line.slice(1) })
    } else if (line.startsWith('-')) {
      result.push({ kind: 'remove', oldLine: oldNum++, newLine: null, text: line.slice(1) })
    } else if (line.startsWith('\\')) {
      result.push({ kind: 'header', oldLine: null, newLine: null, text: line })
    } else {
      result.push({
        kind: 'context',
        oldLine: oldNum++,
        newLine: newNum++,
        text: line.startsWith(' ') ? line.slice(1) : line,
      })
    }
  }

  return result
}
