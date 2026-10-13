import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LayoutGrid, List } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, api } from '../lib/api'
import type { Page, ProgressResult, ReadingItem, ReadingStatus } from '../lib/types'
import { useDebouncedValue } from '../lib/utils'
import { useViewStore } from '../lib/view-store'
import { Badge, Button, Card, EmptyState, ErrorText, Field, Input, Modal, TextArea } from '../components/ui'

const statuses: { value: ReadingStatus; label: string }[] = [
  { value: 'planToRead', label: 'Plan to Read' },
  { value: 'reading', label: 'Currently Reading' },
  { value: 'onHold', label: 'On Hold' },
  { value: 'completed', label: 'Completed' },
  { value: 'dropped', label: 'Dropped' },
]

export function LibraryPage() {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('')
  const [open, setOpen] = useState(false)
  const debounced = useDebouncedValue(query)
  const view = useViewStore((state) => state.library)
  const setView = useViewStore((state) => state.setLibrary)
  const items = useQuery({
    queryKey: ['reading-items', debounced, status],
    queryFn: () => {
      const search = new URLSearchParams()
      if (debounced) search.set('q', debounced)
      if (status) search.set('status', status)
      return api<Page<ReadingItem>>(`/api/v1/reading-items?${search}`)
    },
  })

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-serif text-4xl">My Library</h1>
        <div className="flex gap-2">
          <Button variant="ghost" size="icon" aria-label="Grid view" onClick={() => setView('grid')}><LayoutGrid className="size-4" /></Button>
          <Button variant="ghost" size="icon" aria-label="List view" onClick={() => setView('list')}><List className="size-4" /></Button>
          <Button onClick={() => setOpen(true)}>Add item</Button>
        </div>
      </header>
      <div className="grid gap-3 md:grid-cols-[1fr_14rem]">
        <Input aria-label="Search library" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search titles" />
        <select aria-label="Filter by status" className="h-10 rounded-xl border border-border bg-card px-3 text-sm" value={status} onChange={(event) => setStatus(event.target.value)}>
          <option value="">All statuses</option>
          {statuses.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
        </select>
      </div>
      {items.data?.items.length === 0 ? <EmptyState title="Your library is empty" body="Track a novel or article without turning every bookmark into a reading item." action={<Button onClick={() => setOpen(true)}>Add the first item</Button>} /> : null}
      <div className={view === 'grid' ? 'grid gap-4 sm:grid-cols-2 lg:grid-cols-3' : 'space-y-3'}>
        {items.data?.items.map((item) => (
          <Link key={item.id} to={`/library/${item.id}`}>
            <Card className="h-full p-4">
              <div className="mb-3 h-1.5 overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-primary" style={{ width: `${item.progressPercent ?? 0}%` }} />
              </div>
              <p className="font-serif text-xl">{item.title}</p>
              <p className="mt-1 text-sm text-muted-foreground">{item.activeChapterLabel ? `Chapter ${item.activeChapterLabel}` : 'Not started'}</p>
              <div className="mt-3"><Badge>{statuses.find((entry) => entry.value === item.status)?.label}</Badge></div>
            </Card>
          </Link>
        ))}
      </div>
      <ItemDialog open={open} onOpenChange={setOpen} />
    </div>
  )
}

export function ReadingItemPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const item = useQuery({ queryKey: ['reading-item', id], queryFn: () => api<ReadingItem>(`/api/v1/reading-items/${id}`) })
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['reading-item', id] })
  const update = useMutation({
    mutationFn: (body: Partial<ReadingItem> & { title: string; status: ReadingStatus }) =>
      api<ReadingItem>(`/api/v1/reading-items/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
    onSuccess: invalidate,
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not update the item.'),
  })
  const addSource = useMutation({
    mutationFn: (url: string) => api(`/api/v1/reading-items/${id}/sources`, { method: 'POST', body: JSON.stringify({ url, status: 'active' }) }),
    onSuccess: invalidate,
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not add the source.'),
  })
  const saveProgress = useMutation({
    mutationFn: async (values: { sourceId: string; url: string; label: string; number: string; percent: string; baseRevision: number }) => {
      const current = item.data
      const progress = await api<ProgressResult>('/api/v1/reading-progress', {
        method: 'PUT',
        body: JSON.stringify({
          readingItemId: id,
          sourceId: values.sourceId,
          chapterUrl: values.url,
          chapterLabel: values.label,
          chapterNumber: values.number ? Number(values.number) : null,
          progressPercent: Number(values.percent),
          baseRevision: values.baseRevision,
          intent: 'seek',
          updateId: crypto.randomUUID(),
          markCompleted: false,
        }),
      })
      await api(`/api/v1/reading-items/${id}/cursor`, {
        method: 'PUT',
        body: JSON.stringify({ sourceId: values.sourceId, chapterId: progress.chapterId, baseRevision: current?.cursorRevision ?? 0, updateId: crypto.randomUUID() }),
      })
    },
    onSuccess: async () => {
      await invalidate()
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not save progress. Refresh and try again if another device updated it.'),
  })

  if (item.isLoading) return <p className="text-sm text-muted-foreground">Loading…</p>
  if (!item.data) return <ErrorText>This reading item was not found.</ErrorText>
  const current = item.data

  return (
    <div className="space-y-6">
      <Link to="/library" className="text-sm text-muted-foreground">Back to library</Link>
      <h1 className="font-serif text-4xl">{current.title}</h1>
      <Card className="grid gap-3 p-4 md:grid-cols-2">
        <Field label="Status">
          <select className="h-10 w-full rounded-xl border border-border bg-card px-3" value={current.status} onChange={(event) => update.mutate({ ...current, status: event.target.value as ReadingStatus })}>
            {statuses.map((entry) => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
          </select>
        </Field>
        <Field label="Notes"><TextArea defaultValue={current.notes ?? ''} onBlur={(event) => update.mutate({ ...current, notes: event.target.value })} /></Field>
      </Card>
      <section className="space-y-3">
        <h2 className="font-serif text-2xl">Sources</h2>
        {current.sources.map((source) => (
          <Card key={source.id} className="space-y-3 p-4">
            <p className="font-medium">{source.domain}</p>
            <p className="text-sm text-muted-foreground">Furthest completed chapter stays put when you reread an earlier one.</p>
            <form className="grid gap-3 md:grid-cols-4" onSubmit={(event) => {
              event.preventDefault()
              const data = new FormData(event.currentTarget)
              const url = String(data.get('url') ?? '')
              saveProgress.mutate({
                sourceId: source.id,
                url,
                label: String(data.get('label') ?? ''),
                number: String(data.get('number') ?? ''),
                percent: String(data.get('percent') ?? '0'),
                baseRevision: source.currentChapter?.url === url ? source.currentChapter.revision ?? 0 : 0,
              })
            }}>
              <Input name="url" required placeholder="Chapter URL" aria-label="Chapter URL" />
              <Input name="label" required placeholder="Label" aria-label="Chapter label" />
              <Input name="number" placeholder="Number" aria-label="Chapter number" />
              <Input name="percent" type="number" min="0" max="100" defaultValue="0" aria-label="Progress percent" />
              <Button className="md:col-span-4 w-fit">Update progress</Button>
            </form>
          </Card>
        ))}
        <form className="flex gap-2" onSubmit={(event) => {
          event.preventDefault()
          const data = new FormData(event.currentTarget)
          addSource.mutate(String(data.get('url') ?? ''))
          event.currentTarget.reset()
        }}>
          <Input name="url" type="url" required placeholder="https://another-site.example/novel" aria-label="Source URL" />
          <Button variant="outline">Add source</Button>
        </form>
      </section>
      {error ? <ErrorText>{error}</ErrorText> : null}
    </div>
  )
}

function ItemDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const create = useMutation({
    mutationFn: (body: { title: string; description: string; status: ReadingStatus }) =>
      api<ReadingItem>('/api/v1/reading-items', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['reading-items'] })
      onOpenChange(false)
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not add the item.'),
  })

  return (
    <Modal open={open} onOpenChange={onOpenChange} title="Add to library">
      <form className="space-y-3" onSubmit={(event) => {
        event.preventDefault()
        const data = new FormData(event.currentTarget)
        create.mutate({ title: String(data.get('title')), description: String(data.get('description') ?? ''), status: String(data.get('status')) as ReadingStatus })
      }}>
        <Field label="Title"><Input name="title" required /></Field>
        <Field label="Description"><TextArea name="description" /></Field>
        <Field label="Status">
          <select name="status" className="h-10 w-full rounded-xl border border-border bg-card px-3">
            {statuses.map((entry) => <option key={entry.value} value={entry.value}>{entry.label}</option>)}
          </select>
        </Field>
        {error ? <ErrorText>{error}</ErrorText> : null}
        <Button>Add item</Button>
      </form>
    </Modal>
  )
}
