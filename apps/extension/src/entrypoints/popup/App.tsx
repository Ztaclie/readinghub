import { useEffect, useState } from 'react'

type PageInfo = { title: string; url: string; favicon: string }
type Inspect = { ok: boolean; page?: PageInfo; bookmarks?: { items?: { id: string; title: string }[] } }

export function App() {
  const [signedIn, setSignedIn] = useState(false)
  const [libraryUrl, setLibraryUrl] = useState('http://localhost:5173')
  const [page, setPage] = useState<PageInfo | null>(null)
  const [savedId, setSavedId] = useState<string | null>(null)
  const [notes, setNotes] = useState('')
  const [tags, setTags] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    chrome.runtime.sendMessage({ type: 'session' }).then((response) => {
      setSignedIn(Boolean(response?.signedIn))
      if (response?.webOrigin) setLibraryUrl(response.webOrigin)
    })
    chrome.runtime.sendMessage({ type: 'inspect' }).then((response: Inspect) => {
      if (response?.page) setPage(response.page)
      const existing = response?.bookmarks?.items?.find((item) => item.title)
      if (existing) setSavedId(existing.id)
    })
  }, [])

  return (
    <main>
      <h1>ReadingHub</h1>
      <p className="muted">Save this page. Tracking stays off until you enable it in settings.</p>
      {page ? (
        <div className="page">
          {page.favicon ? <img src={page.favicon} alt="" /> : null}
          <div>
            <strong>{page.title || 'Untitled page'}</strong>
            <div className="muted">{page.url}</div>
          </div>
        </div>
      ) : <p className="muted">This page could not be read.</p>}
      {savedId ? <p>Already in your bookmarks.</p> : null}
      <div className="bar" aria-hidden="true"><span style={{ width: '0%' }} /></div>
      {!signedIn ? <button onClick={() => chrome.runtime.sendMessage({ type: 'sign-in' }).then((response) => {
        setSignedIn(Boolean(response?.ok))
        setError(response?.error ?? '')
      })}>Sign in</button> : null}
      <label>
        Tags
        <input value={tags} onChange={(event) => setTags(event.target.value)} placeholder="fantasy, notes" />
      </label>
      <label>
        Notes
        <textarea value={notes} onChange={(event) => setNotes(event.target.value)} rows={3} />
      </label>
      {error ? <p className="error">{error}</p> : null}
      {message ? <p>{message}</p> : null}
      <button disabled={!signedIn || !page} onClick={() => chrome.runtime.sendMessage({
        type: 'save-bookmark',
        url: page?.url,
        title: page?.title,
        notes,
        tags: tags.split(',').map((tag) => tag.trim()).filter(Boolean),
      }).then((response) => {
        if (response?.id) {
          setSavedId(response.id)
          setMessage('Bookmark saved.')
          setError('')
        } else {
          setError(response?.detail ?? response?.title ?? 'Could not save this page.')
        }
      })}>Save bookmark</button>
      {savedId ? <button className="secondary" onClick={() => chrome.runtime.sendMessage({ type: 'remove-bookmark', id: savedId }).then(() => {
        setSavedId(null)
        setMessage('Bookmark removed.')
      })}>Remove bookmark</button> : null}
      <button className="secondary" onClick={() => chrome.permissions.request({ origins: ['https://www.royalroad.com/*', 'https://royalroad.com/*', 'https://www.webnovel.com/*', 'https://webnovel.com/*'] })}>Allow reading sites</button>
      <button className="secondary" onClick={() => chrome.tabs.create({ url: libraryUrl })}>Open library</button>
    </main>
  )
}
