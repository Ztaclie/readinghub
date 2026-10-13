import { isTrustedSender, parseExtensionMessage } from '../services/messages'
import { decideCursorConflict, decideProgressConflict, queueProgress, type OutboxItem } from '../services/sync'

const defaultApi = 'http://localhost:5080'
let accessToken = ''
let accessExpiresAt = 0

const trackedHosts = new Set(['www.royalroad.com', 'royalroad.com', 'www.webnovel.com', 'webnovel.com'])

export default defineBackground(() => {
  chrome.alarms.create('readinghub-sync', { periodInMinutes: 1 })
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === 'readinghub-sync') void flushOutbox()
  })
  chrome.tabs.onUpdated.addListener((tabId, info, tab) => {
    if (info.status !== 'complete' || !tab.url) return
    void maybeTrack(tabId, tab.url)
  })

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (!isTrustedSender(sender.id, chrome.runtime.id) && sender.id !== undefined) {
      sendResponse({ ok: false })
      return
    }

    const parsed = parseExtensionMessage(message)
    if (parsed?.type === 'progress') {
      void enqueueProgress(parsed)
      sendResponse({ ok: true })
      return
    }

    if (message?.type === 'sign-in') {
      void signIn().then(sendResponse)
      return true
    }

    if (message?.type === 'sign-out') {
      void signOut().then(sendResponse)
      return true
    }

    if (message?.type === 'session') {
      void sessionState().then(sendResponse)
      return true
    }

    if (message?.type === 'inspect') {
      void inspectActiveTab().then(sendResponse)
      return true
    }

    if (message?.type === 'save-bookmark') {
      void saveBookmark(message).then(sendResponse)
      return true
    }

    if (message?.type === 'remove-bookmark') {
      void removeBookmark(message.id).then(sendResponse)
      return true
    }

    if (message?.type === 'collections') {
      void api('/api/v1/collections').then(sendResponse)
      return true
    }

    return false
  })
})

async function signIn() {
  const redirect = chrome.identity.getRedirectURL()
  const apiOrigin = await origin()
  const response = await chrome.identity.launchWebAuthFlow({
    url: `${webOrigin(apiOrigin)}/extension/connect?redirect_uri=${encodeURIComponent(redirect)}`,
    interactive: true,
  })
  const code = new URL(response ?? redirect).searchParams.get('code')
  if (!code) return { ok: false, error: 'The sign-in window did not return a code.' }
  const tokens = await fetch(`${apiOrigin}/api/v1/auth/extension/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code, deviceName: 'Chrome' }),
  })
  if (!tokens.ok) return { ok: false, error: 'ReadingHub rejected the device code.' }
  const body = await tokens.json()
  await chrome.storage.local.set({ refreshToken: body.refreshToken })
  accessToken = body.accessToken
  accessExpiresAt = Date.now() + body.expiresIn * 1000
  await chrome.storage.session.set({ accessToken, accessExpiresAt })
  return { ok: true }
}

async function signOut() {
  if (accessToken) {
    await fetch(`${await origin()}/api/v1/auth/extension/revoke`, { method: 'POST', headers: await authHeaders() }).catch(() => undefined)
  }
  accessToken = ''
  await chrome.storage.local.remove('refreshToken')
  await chrome.storage.session.remove(['accessToken', 'accessExpiresAt'])
  return { ok: true }
}

async function sessionState() {
  const token = await chrome.storage.local.get(['refreshToken', 'webOrigin'])
  return {
    signedIn: Boolean(token.refreshToken),
    webOrigin: typeof token.webOrigin === 'string' && token.webOrigin ? token.webOrigin : 'http://localhost:5173',
  }
}

async function maybeTrack(tabId: number, rawUrl: string) {
  let url: URL
  try {
    url = new URL(rawUrl)
  } catch {
    return
  }
  if (!trackedHosts.has(url.hostname)) return
  const signedIn = await chrome.storage.local.get('refreshToken')
  if (!signedIn.refreshToken) return
  const settings = await api('/api/v1/settings').catch(() => null)
  if (!settings?.ok || settings.autoTracking !== true) return
  const disabled = Array.isArray(settings.disabledDomains) ? settings.disabledDomains.map(String) : []
  if (disabled.includes(url.hostname)) return
  const granted = await chrome.permissions.contains({ origins: [`${url.protocol}//${url.hostname}/*`] })
  if (!granted) return
  await chrome.scripting.executeScript({ target: { tabId }, files: ['content-scripts/reader.js'] })
}

async function inspectActiveTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true })
  if (!tab?.id || !tab.url) return { ok: false }
  const [injected] = await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: () => ({
      title: document.title,
      url: location.href,
      favicon: document.querySelector<HTMLLinkElement>('link[rel~="icon"]')?.href ?? '',
    }),
  })
  const page = injected?.result
  if (!page) return { ok: false }
  const lookup = await api(`/api/v1/bookmarks?q=${encodeURIComponent(page.url)}`).catch(() => null)
  return { ok: true, page, bookmarks: lookup }
}

async function saveBookmark(message: { url?: string; title?: string; notes?: string; tags?: string[]; collectionIds?: string[] }) {
  return api('/api/v1/bookmarks', {
    method: 'POST',
    body: JSON.stringify({
      url: message.url,
      title: message.title,
      notes: message.notes,
      tags: message.tags ?? [],
      collectionIds: message.collectionIds ?? [],
      fetchMetadata: true,
    }),
  })
}

async function removeBookmark(id: string) {
  return api(`/api/v1/bookmarks/${id}`, { method: 'DELETE' })
}

async function enqueueProgress(message: { chapterUrl: string; novelTitle: string | null; chapterLabel: string | null; chapterNumber: number | null; progressPercent: number; complete: boolean }) {
  const stored = await chrome.storage.local.get('outbox')
  const current = (stored.outbox ?? []) as OutboxItem[]
  const next: OutboxItem = {
    id: crypto.randomUUID(),
    kind: 'progress',
    attempts: 0,
    rebases: 0,
    body: { ...message, intent: 'advance' },
  }
  await chrome.storage.local.set({ outbox: queueProgress(current, next) })
  await flushOutbox()
}

async function flushOutbox() {
  const stored = await chrome.storage.local.get('outbox')
  const items = (stored.outbox ?? []) as OutboxItem[]
  const remaining: OutboxItem[] = []
  for (const item of items) {
    try {
      const prepared = await prepareProgress(item)
      if (!prepared) continue
      const response = await api('/api/v1/reading-progress', { method: 'PUT', body: JSON.stringify(prepared) })
      if (!response.ok && response.status === 409) {
        const decision = decideProgressConflict({
          intent: 'advance',
          localPercent: Number(prepared.progressPercent ?? 0),
          serverPercent: Number(response.progressPercent ?? 0),
          serverRevision: Number(response.revision ?? 0),
          rebases: item.rebases,
        })
        if (decision.action === 'rebase') {
          remaining.push({ ...item, rebases: item.rebases + 1, body: { ...item.body, baseRevision: decision.baseRevision } })
        }
        continue
      }
      if (!response.ok) {
        remaining.push({ ...item, attempts: item.attempts + 1 })
        continue
      }
      const cursor = await api(`/api/v1/reading-items/${prepared.readingItemId}/cursor`, {
        method: 'PUT',
        body: JSON.stringify({
          sourceId: prepared.sourceId,
          chapterId: response.chapterId,
          baseRevision: prepared.cursorRevision,
          updateId: crypto.randomUUID(),
        }),
      })
      if (!cursor.ok && cursor.status === 409) decideCursorConflict()
    } catch {
      remaining.push({ ...item, attempts: item.attempts + 1 })
    }
  }
  await chrome.storage.local.set({ outbox: remaining })
}

async function prepareProgress(item: OutboxItem) {
  const title = String(item.body.novelTitle ?? '')
  const chapterUrl = String(item.body.chapterUrl ?? '')
  const label = String(item.body.chapterLabel ?? '')
  if (!title || !label || !chapterUrl.startsWith('http')) return null
  const list = await api(`/api/v1/reading-items?q=${encodeURIComponent(title)}`)
  const items = Array.isArray(list.items) ? list.items as Array<Record<string, unknown>> : []
  let reading = items.find((entry) => entry.title === title)
  if (!reading) {
    reading = await api('/api/v1/reading-items', { method: 'POST', body: JSON.stringify({ title, status: 'reading' }) })
  }
  const readingItemId = String(reading.id ?? '')
  if (!readingItemId) return null
  const detail = await api(`/api/v1/reading-items/${readingItemId}`)
  const domain = new URL(chapterUrl).hostname
  const sources = Array.isArray(detail.sources) ? detail.sources as Array<Record<string, unknown>> : []
  let source = sources.find((entry) => entry.domain === domain)
  if (!source) {
    source = await api(`/api/v1/reading-items/${readingItemId}/sources`, {
      method: 'POST',
      body: JSON.stringify({ url: `${new URL(chapterUrl).origin}/`, status: 'active' }),
    })
  }
  return {
    readingItemId,
    sourceId: String(source.id),
    chapterUrl,
    chapterLabel: label,
    chapterNumber: item.body.chapterNumber ?? null,
    progressPercent: item.body.progressPercent,
    baseRevision: item.body.baseRevision ?? 0,
    intent: 'advance',
    updateId: item.id,
    markCompleted: item.body.complete === true,
    cursorRevision: Number(detail.cursorRevision ?? 0),
  }
}

async function api(path: string, init: RequestInit = {}) {
  const headers = await authHeaders()
  if (init.body) headers['Content-Type'] = 'application/json'
  const response = await fetch(`${await origin()}${path}`, { ...init, headers })
  const text = await response.text()
  let data: Record<string, unknown> = {}
  if (text) {
    try {
      data = JSON.parse(text) as Record<string, unknown>
    } catch {
      data = { detail: text }
    }
  }
  return { ok: response.ok, status: response.status, ...data }
}

async function authHeaders(): Promise<Record<string, string>> {
  if (!accessToken || Date.now() > accessExpiresAt - 15_000) await refresh()
  return accessToken ? { Authorization: `Bearer ${accessToken}` } : {}
}

async function refresh() {
  const stored = await chrome.storage.local.get('refreshToken')
  if (!stored.refreshToken) return
  const response = await fetch(`${await origin()}/api/v1/auth/extension/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: stored.refreshToken }),
  })
  if (!response.ok) {
    await chrome.storage.local.remove('refreshToken')
    accessToken = ''
    return
  }
  const body = await response.json()
  await chrome.storage.local.set({ refreshToken: body.refreshToken })
  accessToken = body.accessToken
  accessExpiresAt = Date.now() + body.expiresIn * 1000
}

async function origin() {
  const stored = await chrome.storage.local.get('apiOrigin')
  return typeof stored.apiOrigin === 'string' && stored.apiOrigin ? stored.apiOrigin.replace(/\/$/, '') : defaultApi
}

function webOrigin(apiOrigin: string) {
  return apiOrigin
}
