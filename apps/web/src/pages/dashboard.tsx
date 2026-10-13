import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../lib/api'
import type { Dashboard } from '../lib/types'
import { Badge, Card, EmptyState } from '../components/ui'

export function DashboardPage() {
  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: () => api<Dashboard>('/api/v1/dashboard') })
  if (dashboard.isLoading) return <p className="text-sm text-muted-foreground">Loading your desk…</p>
  if (dashboard.isError || !dashboard.data) return <p className="text-sm text-danger">The dashboard could not be loaded.</p>
  const data = dashboard.data

  return (
    <div className="space-y-8">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-serif text-4xl">Dashboard</h1>
          <p className="mt-1 text-sm text-muted-foreground">Pick up a book, or save something for later.</p>
        </div>
        <div className="flex gap-2">
          <Link to="/bookmarks" className="inline-flex h-10 items-center rounded-xl bg-primary px-4 text-sm text-primary-foreground">Add bookmark</Link>
          <Link to="/library" className="inline-flex h-10 items-center rounded-xl border border-border bg-card px-4 text-sm">Add reading item</Link>
        </div>
      </header>

      <section>
        <h2 className="mb-3 font-serif text-2xl">Continue reading</h2>
        {data.continueReading.length === 0 ? (
          <EmptyState title="Nothing in progress" body="Add a novel to your library and set the chapter you are on." />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            {data.continueReading.map((item) => (
              <Link key={item.id} to={`/library/${item.id}`}>
                <Card className="p-5 transition hover:-translate-y-0.5">
                  <p className="font-serif text-xl">{item.title}</p>
                  <p className="mt-1 text-sm text-muted-foreground">{item.chapterLabel ? `Chapter ${item.chapterLabel}` : 'No chapter yet'}</p>
                  <div className="mt-4 h-2 overflow-hidden rounded-full bg-muted">
                    <div className="h-full bg-primary" style={{ width: `${item.progressPercent ?? 0}%` }} />
                  </div>
                </Card>
              </Link>
            ))}
          </div>
        )}
      </section>

      <section>
        <h2 className="mb-3 font-serif text-2xl">Recently saved</h2>
        <div className="grid gap-3 md:grid-cols-2">
          {data.recentBookmarks.length === 0 ? <EmptyState title="No bookmarks yet" body="Save a page from the extension or add a URL here." /> : data.recentBookmarks.map((bookmark) => (
            <a key={bookmark.id} href={bookmark.url} target="_blank" rel="noreferrer" className="rounded-2xl border border-border bg-card p-4">
              <p className="font-medium">{bookmark.title}</p>
              <p className="truncate text-sm text-muted-foreground">{bookmark.url}</p>
            </a>
          ))}
        </div>
      </section>

      <section className="grid gap-6 lg:grid-cols-2">
        <div>
          <h2 className="mb-3 font-serif text-2xl">Reading activity</h2>
          <Card className="divide-y divide-border">
            {data.recentlyRead.length === 0 ? <p className="p-4 text-sm text-muted-foreground">No reading events yet.</p> : data.recentlyRead.map((event) => (
              <div key={event.id} className="px-4 py-3 text-sm">
                <span className="text-muted-foreground">{new Date(event.occurredAt).toLocaleString()}</span>
                <p>{event.itemTitle}{event.chapterLabel ? ` — Chapter ${event.chapterLabel}` : ''}</p>
              </div>
            ))}
          </Card>
        </div>
        <div>
          <h2 className="mb-3 font-serif text-2xl">Pinned collections</h2>
          <div className="flex flex-wrap gap-2">
            {data.pinnedCollections.length === 0 ? <p className="text-sm text-muted-foreground">Pin a collection to keep it here.</p> : data.pinnedCollections.map((collection) => (
              <Link key={collection.id} to={`/bookmarks?collection=${collection.id}`}><Badge>{collection.icon} {collection.name}</Badge></Link>
            ))}
          </div>
        </div>
      </section>
    </div>
  )
}
