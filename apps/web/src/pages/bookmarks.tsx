import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LayoutGrid, List, Search } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError, api } from '../lib/api'
import type { Bookmark, Collection, Page, Tag } from '../lib/types'
import { useDebouncedValue } from '../lib/utils'
import { useViewStore } from '../lib/view-store'
import { Badge, Button, Card, EmptyState, ErrorText, Field, Input, Modal, TextArea } from '../components/ui'

export function BookmarksPage() {
  const [params] = useSearchParams()
  const collection = params.get('collection') ?? ''
  const [query, setQuery] = useState('')
  const [tag, setTag] = useState('')
  const [open, setOpen] = useState(false)
  const debounced = useDebouncedValue(query)
  const view = useViewStore((state) => state.bookmarks)
  const setView = useViewStore((state) => state.setBookmarks)
  const bookmarks = useQuery({
    queryKey: ['bookmarks', debounced, tag, collection],
    queryFn: () => {
      const search = new URLSearchParams()
      if (debounced) search.set('q', debounced)
      if (tag) search.set('tag', tag)
      if (collection) search.set('collectionId', collection)
      return api<Page<Bookmark>>(`/api/v1/bookmarks?${search}`)
    },
  })

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-serif text-4xl">Bookmarks</h1>
        <div className="flex gap-2">
          <Button variant="ghost" size="icon" aria-label="Grid view" onClick={() => setView('grid')}><LayoutGrid className="size-4" /></Button>
          <Button variant="ghost" size="icon" aria-label="List view" onClick={() => setView('list')}><List className="size-4" /></Button>
          <Button onClick={() => setOpen(true)}>Save URL</Button>
        </div>
      </header>
      <div className="grid gap-3 md:grid-cols-[1fr_12rem]">
        <div className="relative">
          <Search className="pointer-events-none absolute top-3 left-3 size-4 text-muted-foreground" />
          <Input aria-label="Search bookmarks" className="pl-9" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search title, URL, or description" />
        </div>
        <Input aria-label="Filter by tag" value={tag} onChange={(event) => setTag(event.target.value)} placeholder="Tag" />
      </div>
      {bookmarks.isLoading ? <p className="text-sm text-muted-foreground">Searching…</p> : null}
      {bookmarks.isError ? <ErrorText>Bookmarks could not be loaded.</ErrorText> : null}
      {bookmarks.data?.items.length === 0 ? <EmptyState title="No matching bookmarks" body="Try another search, or save the page you have open." /> : null}
      <div className={view === 'grid' ? 'grid gap-4 sm:grid-cols-2' : 'space-y-3'}>
        {bookmarks.data?.items.map((bookmark) => (
          <Card key={bookmark.id} className="p-4">
            <a href={bookmark.url} target="_blank" rel="noreferrer" className="font-medium hover:underline">{bookmark.title}</a>
            <p className="truncate text-sm text-muted-foreground">{bookmark.siteName ?? bookmark.url}</p>
            {bookmark.description ? <p className="mt-2 line-clamp-2 text-sm">{bookmark.description}</p> : null}
            <div className="mt-3 flex flex-wrap gap-2">
              {bookmark.tags.map((item) => <Badge key={item.id}>#{item.name}</Badge>)}
            </div>
          </Card>
        ))}
      </div>
      <BookmarkDialog open={open} onOpenChange={setOpen} />
    </div>
  )
}

function BookmarkDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const collections = useQuery({ queryKey: ['collections'], queryFn: () => api<Collection[]>('/api/v1/collections'), enabled: open })
  const create = useMutation({
    mutationFn: (body: { url: string; title: string; notes: string; tags: string[]; collectionIds: string[] }) =>
      api<Bookmark>('/api/v1/bookmarks', { method: 'POST', body: JSON.stringify({ ...body, fetchMetadata: true }) }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['bookmarks'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      onOpenChange(false)
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not save the bookmark.'),
  })

  return (
    <Modal open={open} onOpenChange={onOpenChange} title="Save a bookmark">
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault()
          const data = new FormData(event.currentTarget)
          create.mutate({
            url: String(data.get('url') ?? ''),
            title: String(data.get('title') ?? ''),
            notes: String(data.get('notes') ?? ''),
            tags: String(data.get('tags') ?? '').split(',').map((tag) => tag.trim()).filter(Boolean),
            collectionIds: data.getAll('collection').map(String),
          })
        }}
      >
        <Field label="URL"><Input name="url" type="url" required placeholder="https://" /></Field>
        <Field label="Title"><Input name="title" placeholder="Leave blank to use the page title" /></Field>
        <Field label="Tags"><Input name="tags" placeholder="fantasy, programming" /></Field>
        <Field label="Notes"><TextArea name="notes" /></Field>
        <fieldset>
          <legend className="mb-2 text-sm font-medium">Collections</legend>
          <div className="max-h-32 space-y-1 overflow-auto">
            {(collections.data ?? []).map((collection) => (
              <label key={collection.id} className="flex items-center gap-2 text-sm">
                <input type="checkbox" name="collection" value={collection.id} />
                {collection.name}
              </label>
            ))}
          </div>
        </fieldset>
        {error ? <ErrorText>{error}</ErrorText> : null}
        <Button disabled={create.isPending}>{create.isPending ? 'Saving…' : 'Save bookmark'}</Button>
      </form>
    </Modal>
  )
}

export function CollectionsPage() {
  const queryClient = useQueryClient()
  const collections = useQuery({ queryKey: ['collections'], queryFn: () => api<Collection[]>('/api/v1/collections') })
  const [error, setError] = useState('')
  const create = useMutation({
    mutationFn: (body: { name: string; description: string; color: string; parentId: string | null }) =>
      api<Collection>('/api/v1/collections', { method: 'POST', body: JSON.stringify({ ...body, isPinned: false, sortOrder: 0, parentId: body.parentId || null }) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['collections'] }),
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not create the collection.'),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api<void>(`/api/v1/collections/${id}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['collections'] }),
  })
  const pin = useMutation({
    mutationFn: (collection: Collection) => api<Collection>(`/api/v1/collections/${collection.id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...collection, isPinned: !collection.isPinned }),
    }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['collections'] }),
  })

  return (
    <div className="space-y-6">
      <h1 className="font-serif text-4xl">Collections</h1>
      <Card className="space-y-3 p-4">
        <form className="grid gap-3 md:grid-cols-2" onSubmit={(event) => {
          event.preventDefault()
          const data = new FormData(event.currentTarget)
          create.mutate({ name: String(data.get('name')), description: String(data.get('description') ?? ''), color: String(data.get('color') || '#1f6f5b'), parentId: String(data.get('parentId') ?? '') })
          event.currentTarget.reset()
        }}>
          <Field label="Name"><Input name="name" required /></Field>
          <Field label="Parent">
            <select name="parentId" className="h-10 w-full rounded-xl border border-border bg-card px-3 text-sm">
              <option value="">None</option>
              {(collections.data ?? []).map((collection) => <option key={collection.id} value={collection.id}>{collection.name}</option>)}
            </select>
          </Field>
          <Field label="Description"><Input name="description" /></Field>
          <Field label="Color"><Input name="color" type="color" defaultValue="#1f6f5b" /></Field>
          <Button className="md:col-span-2 w-fit">Create collection</Button>
        </form>
        {error ? <ErrorText>{error}</ErrorText> : null}
      </Card>
      <div className="space-y-2">
        {(collections.data ?? []).map((collection) => (
          <Card key={collection.id} className="flex flex-wrap items-center justify-between gap-3 p-4">
            <div>
              <p className="font-medium" style={{ color: collection.color ?? undefined }}>{collection.name}</p>
              <p className="text-sm text-muted-foreground">{collection.bookmarkCount} bookmarks</p>
            </div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" onClick={() => pin.mutate(collection)}>{collection.isPinned ? 'Unpin' : 'Pin'}</Button>
              <Button variant="ghost" size="sm" onClick={() => remove.mutate(collection.id)}>Delete</Button>
            </div>
          </Card>
        ))}
      </div>
    </div>
  )
}

export function TagsPage() {
  const queryClient = useQueryClient()
  const tags = useQuery({ queryKey: ['tags'], queryFn: () => api<Tag[]>('/api/v1/tags') })
  const create = useMutation({
    mutationFn: (name: string) => api<Tag>('/api/v1/tags', { method: 'POST', body: JSON.stringify({ name }) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
  })
  const remove = useMutation({
    mutationFn: (id: string) => api<void>(`/api/v1/tags/${id}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tags'] }),
  })

  return (
    <div className="space-y-6">
      <h1 className="font-serif text-4xl">Tags</h1>
      <form className="flex gap-2" onSubmit={(event) => {
        event.preventDefault()
        const data = new FormData(event.currentTarget)
        create.mutate(String(data.get('name') ?? ''))
        event.currentTarget.reset()
      }}>
        <Input name="name" aria-label="New tag" placeholder="New tag" required />
        <Button>Add</Button>
      </form>
      <div className="flex flex-wrap gap-2">
        {(tags.data ?? []).map((tag) => (
          <button key={tag.id} className="rounded-full bg-accent px-3 py-1 text-sm" onClick={() => remove.mutate(tag.id)}>#{tag.name}</button>
        ))}
      </div>
    </div>
  )
}
