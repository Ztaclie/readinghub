import { chapterFromText, emptyDetection, type SiteAdapter } from './types'

export const royalRoadAdapter: SiteAdapter = {
  id: 'royalroad',
  matches: (url) => url.hostname === 'www.royalroad.com' || url.hostname === 'royalroad.com',
  detect: (document, url) => {
    const result = emptyDetection(url, 0.2)
    const chapterMatch = url.pathname.match(/\/fiction\/\d+\/[^/]+\/chapter\/\d+/)
    const heading = document.querySelector('h1')?.textContent
    const parsed = chapterFromText(heading)
    const novel = document.querySelector('.fic-header h2 a, h2 a')?.textContent?.trim() || null
    const content = document.querySelector('.chapter-content')
    result.novelTitle = novel
    result.chapterTitle = heading?.trim() || null
    result.chapterLabel = parsed.label
    result.chapterNumber = parsed.number
    result.contentSelector = content ? '.chapter-content' : null
    result.previousUrl = document.querySelector('a[rel="prev"]')?.getAttribute('href') ?? null
    result.nextUrl = document.querySelector('a[rel="next"]')?.getAttribute('href') ?? null
    if (chapterMatch && content && parsed.number !== null) result.confidence = 0.92
    else if (url.pathname.includes('/fiction/') && novel) result.confidence = 0.45
    return result
  },
}
