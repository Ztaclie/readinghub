import { chapterFromText, emptyDetection, type SiteAdapter } from './types'

export const genericAdapter: SiteAdapter = {
  id: 'generic',
  matches: () => true,
  detect: (document, url) => {
    const result = emptyDetection(url, 0.15)
    const heading = document.querySelector('h1')?.textContent ?? document.title
    const parsed = chapterFromText(heading)
    const content = document.querySelector('article, main')
    const pathLooksLikeChapter = /\/chapter[-/]/i.test(url.pathname)
    result.chapterTitle = heading?.replace(/\s+/g, ' ').trim() || null
    result.chapterLabel = parsed.number !== null ? parsed.label : null
    result.chapterNumber = parsed.number
    result.novelTitle = document.querySelector('meta[property="og:site_name"]')?.getAttribute('content') ?? null
    result.contentSelector = document.querySelector('article') ? 'article' : content ? 'main' : null
    if (parsed.number !== null && content && (pathLooksLikeChapter || /chapter\s+[0-9]/i.test(heading ?? ''))) {
      result.confidence = 0.72
    } else if (pathLooksLikeChapter) {
      result.confidence = 0.4
    }
    return result
  },
}
