import { detectPage } from '../adapters/registry'
import { contentProgress, createProgressTracker, scrollTopForPercent } from '../reader/progress'

export default defineContentScript({
  matches: ['http://localhost:5080/*'],
  registration: 'runtime',
  runAt: 'document_idle',
  main() {
    const marker = window as Window & { __readingHub?: boolean }
    if (marker.__readingHub) return
    marker.__readingHub = true
    const detection = detectPage(document, new URL(location.href))
    const content = detection.contentSelector ? document.querySelector(detection.contentSelector) : null
    chrome.runtime.sendMessage({
      type: 'detected',
      novelTitle: detection.novelTitle,
      chapterTitle: detection.chapterTitle,
      chapterLabel: detection.chapterLabel,
      chapterNumber: detection.chapterNumber,
      chapterUrl: detection.chapterUrl,
      confidence: detection.confidence,
    })

    let threshold = 95
    let autoComplete = true
    let restore = true
    const tracker = createProgressTracker({
      threshold,
      autoComplete,
      emit: (percent, complete) => {
        chrome.runtime.sendMessage({
          type: 'progress',
          chapterUrl: location.href,
          novelTitle: detection.novelTitle,
          chapterLabel: detection.chapterLabel,
          chapterNumber: detection.chapterNumber,
          progressPercent: percent,
          complete,
        })
      },
    })

    const sample = () => {
      const target = content instanceof HTMLElement ? content : document.scrollingElement
      if (!target) return
      tracker.sample(contentProgress(target.scrollTop, target.scrollHeight, target.clientHeight), Date.now())
    }

    const applyRestore = (percent: number) => {
      const target = content instanceof HTMLElement ? content : document.scrollingElement
      if (!target) return
      const top = scrollTopForPercent(target.scrollHeight, target.clientHeight, percent)
      target.scrollTop = top
      window.setTimeout(() => {
        target.scrollTop = scrollTopForPercent(target.scrollHeight, target.clientHeight, percent)
      }, 800)
    }

    chrome.runtime.onMessage.addListener((message) => {
      if (!message || typeof message !== 'object') return
      if (message.type === 'configure') {
        threshold = Number(message.threshold ?? 95)
        autoComplete = Boolean(message.autoComplete)
        restore = Boolean(message.restore)
      }
      if (message.type === 'restore' && restore && typeof message.percent === 'number') {
        applyRestore(message.percent)
      }
    })

    document.addEventListener('scroll', sample, { passive: true, capture: true })
    window.setInterval(sample, 2000)
  },
})
