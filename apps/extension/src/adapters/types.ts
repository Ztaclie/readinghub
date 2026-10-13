export type Detection = {
  novelTitle: string | null
  chapterTitle: string | null
  chapterLabel: string | null
  chapterNumber: number | null
  chapterUrl: string
  sourceDomain: string
  contentSelector: string | null
  previousUrl: string | null
  nextUrl: string | null
  confidence: number
}

export type SiteAdapter = {
  id: string
  matches: (url: URL) => boolean
  detect: (document: Document, url: URL) => Detection
}

export function chapterFromText(value: string | null | undefined) {
  const text = value?.replace(/\s+/g, ' ').trim() ?? ''
  const match = text.match(/chapter\s+([0-9]+(?:\.[0-9]+)?)/i)
  if (!match) return { label: text || null, number: null as number | null }
  return { label: match[1], number: Number(match[1]) }
}

export function emptyDetection(url: URL, confidence = 0.1): Detection {
  return {
    novelTitle: null,
    chapterTitle: null,
    chapterLabel: null,
    chapterNumber: null,
    chapterUrl: url.href,
    sourceDomain: url.hostname,
    contentSelector: null,
    previousUrl: null,
    nextUrl: null,
    confidence,
  }
}
