export type SyncDecision =
  | { action: 'drop' }
  | { action: 'rebase'; baseRevision: number; percent: number }
  | { action: 'retry' }

export function decideProgressConflict(input: {
  intent: 'advance' | 'seek'
  localPercent: number
  serverPercent: number
  serverRevision: number
  rebases: number
}): SyncDecision {
  if (input.rebases >= 1) return { action: 'drop' }
  if (input.intent === 'advance' && input.localPercent > input.serverPercent) {
    return { action: 'rebase', baseRevision: input.serverRevision, percent: input.localPercent }
  }
  return { action: 'drop' }
}

export function decideCursorConflict(): SyncDecision {
  return { action: 'drop' }
}

export type OutboxItem = {
  id: string
  kind: 'progress' | 'cursor'
  body: Record<string, unknown>
  attempts: number
  rebases: number
}

export function queueProgress(items: OutboxItem[], next: OutboxItem) {
  const chapterUrl = String(next.body.chapterUrl ?? '')
  const withoutSameChapter = items.filter((item) => item.kind !== 'progress' || item.body.chapterUrl !== chapterUrl)
  return [...withoutSameChapter, next]
}
