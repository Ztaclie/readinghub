export type ProgressMessage = {
  type: 'progress'
  chapterUrl: string
  novelTitle: string | null
  chapterLabel: string | null
  chapterNumber: number | null
  progressPercent: number
  complete: boolean
}

export type DetectedMessage = {
  type: 'detected'
  novelTitle: string | null
  chapterTitle: string | null
  chapterLabel: string | null
  chapterNumber: number | null
  chapterUrl: string
  confidence: number
}

export function parseExtensionMessage(value: unknown): ProgressMessage | DetectedMessage | null {
  if (!value || typeof value !== 'object') return null
  const record = value as Record<string, unknown>
  if (record.type === 'progress') {
    if (typeof record.chapterUrl !== 'string' || !record.chapterUrl.startsWith('http')) return null
    if (typeof record.progressPercent !== 'number' || record.progressPercent < 0 || record.progressPercent > 100) return null
    if (typeof record.complete !== 'boolean') return null
    return {
      type: 'progress',
      chapterUrl: record.chapterUrl,
      novelTitle: typeof record.novelTitle === 'string' ? record.novelTitle : null,
      chapterLabel: typeof record.chapterLabel === 'string' ? record.chapterLabel : null,
      chapterNumber: typeof record.chapterNumber === 'number' ? record.chapterNumber : null,
      progressPercent: record.progressPercent,
      complete: record.complete,
    }
  }

  if (record.type === 'detected') {
    if (typeof record.chapterUrl !== 'string' || typeof record.confidence !== 'number') return null
    return {
      type: 'detected',
      novelTitle: typeof record.novelTitle === 'string' ? record.novelTitle : null,
      chapterTitle: typeof record.chapterTitle === 'string' ? record.chapterTitle : null,
      chapterLabel: typeof record.chapterLabel === 'string' ? record.chapterLabel : null,
      chapterNumber: typeof record.chapterNumber === 'number' ? record.chapterNumber : null,
      chapterUrl: record.chapterUrl,
      confidence: record.confidence,
    }
  }

  return null
}

export function isTrustedSender(senderId: string | undefined, extensionId: string) {
  return senderId === extensionId
}
