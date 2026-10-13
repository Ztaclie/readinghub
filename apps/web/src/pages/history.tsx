import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../lib/api'
import type { HistoryEvent, Page } from '../lib/types'
import { Button, Card, EmptyState } from '../components/ui'

const labels: Record<string, string> = {
  readingItemAdded: 'Added',
  statusChanged: 'Status changed',
  chapterOpened: 'Opened',
  chapterCompleted: 'Completed',
}

export function HistoryPage() {
  const queryClient = useQueryClient()
  const history = useQuery({ queryKey: ['history'], queryFn: () => api<Page<HistoryEvent>>('/api/v1/reading-history') })
  const clear = useMutation({
    mutationFn: () => api<void>('/api/v1/reading-history', { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['history'] }),
  })
  const groups = new Map<string, HistoryEvent[]>()
  for (const event of history.data?.items ?? []) {
    const day = new Date(event.occurredAt).toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })
    groups.set(day, [...(groups.get(day) ?? []), event])
  }

  return (
    <div className="space-y-6">
      <header className="flex items-center justify-between gap-3">
        <h1 className="font-serif text-4xl">Reading history</h1>
        <div className="flex gap-2">
          <a className="inline-flex h-10 items-center rounded-xl border border-border px-4 text-sm" href="/api/v1/reading-history/export">Export</a>
          <Button variant="outline" onClick={() => clear.mutate()}>Delete history</Button>
        </div>
      </header>
      {[...groups.entries()].length === 0 ? <EmptyState title="No history yet" body="Chapter opens and completions show up here. Scroll updates do not." /> : [...groups.entries()].map(([day, events]) => (
        <section key={day}>
          <h2 className="mb-2 text-sm font-medium text-muted-foreground">{day}</h2>
          <Card className="divide-y divide-border">
            {events.map((event) => (
              <div key={event.id} className="flex items-baseline justify-between gap-4 px-4 py-3 text-sm">
                <p>{new Date(event.occurredAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} — {event.itemTitle}{event.chapterLabel ? ` — Chapter ${event.chapterLabel}` : ''}</p>
                <span className="text-muted-foreground">{labels[event.eventType] ?? event.eventType}</span>
              </div>
            ))}
          </Card>
        </section>
      ))}
    </div>
  )
}
