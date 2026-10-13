import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import { describe, expect, it } from 'vitest'
import { detectPage } from '../adapters/registry'
import { contentProgress, createProgressTracker, scrollTopForPercent } from '../reader/progress'
import { isTrustedSender, parseExtensionMessage } from '../services/messages'
import { decideCursorConflict, decideProgressConflict, queueProgress } from '../services/sync'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../../..')

function page(fixture: string, url: string) {
  const html = readFileSync(resolve(repoRoot, fixture), 'utf8')
  return new JSDOM(html, { url }).window.document
}

describe('chapter detection', () => {
  it('reads a Royal Road chapter fixture', () => {
    const detected = detectPage(page('fixtures/royalroad/chapter.html', 'https://www.royalroad.com/fiction/21220/shadow-slave/chapter/301234/a-quiet-road'), new URL('https://www.royalroad.com/fiction/21220/shadow-slave/chapter/301234/a-quiet-road'))
    expect(detected.novelTitle).toBe('Shadow Slave')
    expect(detected.chapterNumber).toBe(145)
    expect(detected.contentSelector).toBe('.chapter-content')
    expect(detected.confidence).toBeGreaterThan(0.6)
    expect(detected.nextUrl).toContain('/chapter/146/')
  })

  it('reads a Webnovel chapter fixture', () => {
    const url = 'https://www.webnovel.com/book/shadow-slave_100/the-long-night_145'
    const detected = detectPage(page('fixtures/webnovel/chapter.html', url), new URL(url))
    expect(detected.novelTitle).toBe('Shadow Slave')
    expect(detected.chapterNumber).toBe(145)
    expect(detected.contentSelector).toBe('.cha-words')
    expect(detected.confidence).toBeGreaterThan(0.6)
  })

  it('accepts a generic chapter page and rejects a numbered headline', () => {
    const chapterUrl = 'https://stories.example/paper-boats/chapter-12'
    const chapter = detectPage(page('fixtures/generic/chapter.html', chapterUrl), new URL(chapterUrl))
    expect(chapter.confidence).toBeGreaterThan(0.6)
    expect(chapter.chapterNumber).toBe(12)

    const listUrl = 'https://example.com/top-10-tools'
    const list = detectPage(page('fixtures/generic/not-a-chapter.html', listUrl), new URL(listUrl))
    expect(list.confidence).toBeLessThan(0.6)
    expect(list.chapterNumber).toBeNull()
  })
})

describe('progress tracking', () => {
  it('measures the content element and ignores a page that loads at the bottom', () => {
    expect(contentProgress(50, 200, 100)).toBe(50)
    const events: Array<{ percent: number; complete: boolean }> = []
    const tracker = createProgressTracker({
      threshold: 95,
      autoComplete: true,
      emit: (percent, complete) => events.push({ percent, complete }),
    })
    expect(tracker.sample(100, 0)).toBe(false)
    expect(tracker.armed).toBe(false)
    expect(events).toHaveLength(0)
  })

  it('completes only after the reader has been higher on the page', () => {
    const events: boolean[] = []
    const tracker = createProgressTracker({
      threshold: 95,
      autoComplete: true,
      intervalMs: 0,
      emit: (_percent, complete) => events.push(complete),
    })
    tracker.sample(10, 0)
    tracker.sample(96, 10)
    expect(events).toEqual([true])
  })

  it('restores a percentage of the content height', () => {
    expect(scrollTopForPercent(1000, 200, 72)).toBeCloseTo(576)
  })
})

describe('extension security', () => {
  it('rejects malformed progress messages and foreign senders', () => {
    expect(parseExtensionMessage({ type: 'progress', chapterUrl: 'javascript:alert(1)', progressPercent: 10, complete: false })).toBeNull()
    expect(parseExtensionMessage({ type: 'progress', chapterUrl: 'https://example.com/1', progressPercent: 120, complete: false })).toBeNull()
    expect(parseExtensionMessage({ type: 'progress', chapterUrl: 'https://example.com/1', progressPercent: 40, complete: false })?.type).toBe('progress')
    expect(isTrustedSender('other', 'readinghub')).toBe(false)
    expect(isTrustedSender('readinghub', 'readinghub')).toBe(true)
  })

  it('keeps credentials out of the content script', () => {
    const source = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../entrypoints/reader.content.ts'), 'utf8')
    expect(source).not.toContain('refreshToken')
    expect(source).not.toContain('Authorization')
    expect(source).not.toContain('accessToken')
  })
})

describe('offline sync', () => {
  it('keeps the higher percent once and does not force a cursor backward', () => {
    expect(decideProgressConflict({ intent: 'advance', localPercent: 80, serverPercent: 72, serverRevision: 4, rebases: 0 })).toEqual({ action: 'rebase', baseRevision: 4, percent: 80 })
    expect(decideProgressConflict({ intent: 'advance', localPercent: 10, serverPercent: 72, serverRevision: 4, rebases: 0 })).toEqual({ action: 'drop' })
    expect(decideProgressConflict({ intent: 'advance', localPercent: 90, serverPercent: 72, serverRevision: 5, rebases: 1 })).toEqual({ action: 'drop' })
    expect(decideCursorConflict()).toEqual({ action: 'drop' })
    const queued = queueProgress([
      { id: '1', kind: 'progress', body: { chapterUrl: 'https://example.com/1', progressPercent: 10 }, attempts: 0, rebases: 0 },
    ], { id: '2', kind: 'progress', body: { chapterUrl: 'https://example.com/1', progressPercent: 20 }, attempts: 0, rebases: 0 })
    expect(queued).toHaveLength(1)
    expect(queued[0]?.body.progressPercent).toBe(20)
  })
})
