import { chapterFromText, emptyDetection, type SiteAdapter } from './types'

export const webnovelAdapter: SiteAdapter = {
  id: 'webnovel',
  matches: (url) => url.hostname === 'www.webnovel.com' || url.hostname === 'webnovel.com',
  detect: (document, url) => {
    const result = emptyDetection(url, 0.25)
    const heading = document.querySelector('h1.cha-tit, h1')?.textContent
    const parsed = chapterFromText(heading)
    const novel = document.querySelector('.book-name a')?.textContent?.trim() || null
    const content = document.querySelector('.cha-words, .cha-content')
    result.novelTitle = novel
    result.chapterTitle = heading?.replace(/\s+/g, ' ').trim() || null
    result.chapterLabel = parsed.label
    result.chapterNumber = parsed.number
    result.contentSelector = document.querySelector('.cha-words') ? '.cha-words' : content ? '.cha-content' : null
    result.previousUrl = document.querySelector('a.btn-prev')?.getAttribute('href') ?? null
    result.nextUrl = document.querySelector('a.btn-next')?.getAttribute('href') ?? null
    const onChapter = /\/book\/[^/]+\/[^/]+/.test(url.pathname)
    if (onChapter && content && parsed.number !== null) result.confidence = 0.84
    else if (url.pathname.includes('/book/')) result.confidence = 0.4
    return result
  },
}
