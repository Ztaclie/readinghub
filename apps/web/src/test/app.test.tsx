import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('ReadingHub web', () => {
  beforeEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows login when the server already has an account', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo) => {
      const url = String(input)
      if (url.includes('/auth/setup-status')) return json({ needsSetup: false })
      if (url.includes('/auth/me')) return json({ title: 'Unauthorized' }, 401)
      return json({ token: 'csrf' })
    }))

    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  })

  it('opens the library and continue reading from the dashboard', async () => {
    const user = userEvent.setup()
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo) => {
      const url = String(input)
      if (url.includes('/auth/setup-status')) return json({ needsSetup: false })
      if (url.includes('/auth/me')) return json({ id: 'user', email: 'ada@example.com', displayName: 'Ada' })
      if (url.includes('/dashboard')) {
        return json({
          continueReading: [{ id: 'novel', title: 'Shadow Slave', coverImageUrl: null, chapterLabel: '145', progressPercent: 72, lastReadAt: '2026-10-13T12:00:00Z' }],
          recentBookmarks: [{ id: 'b1', url: 'https://example.com', normalizedUrl: 'https://example.com', canonicalUrl: null, title: 'Notes', description: null, siteName: 'Example', faviconUrl: null, imageUrl: null, notes: null, createdAt: '2026-10-13T12:00:00Z', updatedAt: '2026-10-13T12:00:00Z', collectionIds: [], tags: [] }],
          recentlyRead: [],
          pinnedCollections: [],
        })
      }
      if (url.includes('/reading-items')) return json({ items: [{ id: 'novel', title: 'Shadow Slave', description: null, coverImageUrl: null, notes: null, status: 'reading', bookmarkId: null, activeSourceId: null, activeChapterId: null, cursorRevision: 1, lastReadAt: null, createdAt: '2026-10-13T12:00:00Z', progressPercent: 72, activeChapterLabel: '145', sources: [] }], nextCursor: null })
      if (url.includes('/bookmarks')) return json({ items: [], nextCursor: null })
      if (url.includes('/collections')) return json([])
      return json({ token: 'csrf' })
    }))

    render(<App />)
    expect(await screen.findByText('Shadow Slave')).toBeInTheDocument()
    expect(screen.getByText('Continue reading')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'My Library' }))
    expect(await screen.findByRole('heading', { name: 'My Library' })).toBeInTheDocument()
    expect(screen.getByText('Chapter 145')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'Bookmarks' }))
    expect(await screen.findByRole('heading', { name: 'Bookmarks' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Search bookmarks'), 'delphi')
    await waitFor(() => {
      const calls = vi.mocked(fetch).mock.calls.map((call) => String(call[0]))
      expect(calls.some((url) => url.includes('q=delphi'))).toBe(true)
    })
    expect(screen.getByLabelText('Open menu')).toBeInTheDocument()
  })
})
