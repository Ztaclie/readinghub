import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { ApiError, api, authApi } from '../lib/api'
import type { Collection, Settings, WebSession } from '../lib/types'
import { useTheme } from '../lib/theme'
import { Button, Card, ErrorText, Field, Input } from '../components/ui'

export function SettingsPage() {
  const { setTheme } = useTheme()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api<Settings>('/api/v1/settings') })
  const collections = useQuery({ queryKey: ['collections'], queryFn: () => api<Collection[]>('/api/v1/collections') })
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: () => api<WebSession[]>('/api/v1/auth/sessions') })
  const [draft, setDraft] = useState<Settings | null>(null)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (settings.data) setDraft(settings.data)
  }, [settings.data])

  const save = useMutation({
    mutationFn: (body: Settings) => api<Settings>('/api/v1/settings', { method: 'PUT', body: JSON.stringify(body) }),
    onSuccess: async (next) => {
      setTheme(next.theme)
      setMessage('Settings saved.')
      await queryClient.invalidateQueries({ queryKey: ['settings'] })
    },
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not save settings.'),
  })
  const changePassword = useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) => api<void>('/api/v1/auth/change-password', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => setMessage('Password updated. Other sessions were signed out.'),
    onError: (reason) => setError(reason instanceof ApiError ? reason.message : 'Could not change the password.'),
  })
  const revoke = useMutation({
    mutationFn: (id: string) => api<void>(`/api/v1/auth/sessions/${id}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  })

  if (!draft) return <p className="text-sm text-muted-foreground">Loading settings…</p>

  return (
    <div className="space-y-6">
      <h1 className="font-serif text-4xl">Settings</h1>
      {message ? <p className="text-sm text-primary">{message}</p> : null}
      {error ? <ErrorText>{error}</ErrorText> : null}
      <Card className="space-y-4 p-5">
        <h2 className="font-serif text-2xl">General</h2>
        <Field label="Theme">
          <select className="h-10 w-full rounded-xl border border-border bg-card px-3" value={draft.theme} onChange={(event) => setDraft({ ...draft, theme: event.target.value as Settings['theme'] })}>
            <option value="system">System</option>
            <option value="light">Light</option>
            <option value="dark">Dark</option>
          </select>
        </Field>
        <Field label="Language"><Input value={draft.locale} onChange={(event) => setDraft({ ...draft, locale: event.target.value })} /></Field>
        <Field label="Default collection">
          <select className="h-10 w-full rounded-xl border border-border bg-card px-3" value={draft.defaultCollectionId ?? ''} onChange={(event) => setDraft({ ...draft, defaultCollectionId: event.target.value || null })}>
            <option value="">None</option>
            {(collections.data ?? []).map((collection) => <option key={collection.id} value={collection.id}>{collection.name}</option>)}
          </select>
        </Field>
      </Card>
      <Card className="space-y-4 p-5">
        <h2 className="font-serif text-2xl">Reading</h2>
        <Toggle label="Automatic tracking" checked={draft.autoTracking} onChange={(autoTracking) => setDraft({ ...draft, autoTracking })} />
        <Toggle label="Automatic chapter completion" checked={draft.autoComplete} onChange={(autoComplete) => setDraft({ ...draft, autoComplete })} />
        <Toggle label="Reading history" checked={draft.historyEnabled} onChange={(historyEnabled) => setDraft({ ...draft, historyEnabled })} />
        <Toggle label="Position restoration" checked={draft.positionRestoration} onChange={(positionRestoration) => setDraft({ ...draft, positionRestoration })} />
        <Field label="Completion threshold">
          <Input type="number" min={50} max={100} value={draft.completionThreshold} onChange={(event) => setDraft({ ...draft, completionThreshold: Number(event.target.value) })} />
        </Field>
        <Field label="Progress sync delay (seconds)">
          <Input type="number" min={1} max={60} value={draft.syncDebounceSeconds} onChange={(event) => setDraft({ ...draft, syncDebounceSeconds: Number(event.target.value) })} />
        </Field>
      </Card>
      <Card className="space-y-4 p-5">
        <h2 className="font-serif text-2xl">Websites</h2>
        <Field label="Enabled domains"><Input value={draft.enabledDomains.join(', ')} onChange={(event) => setDraft({ ...draft, enabledDomains: splitDomains(event.target.value) })} /></Field>
        <Field label="Disabled domains"><Input value={draft.disabledDomains.join(', ')} onChange={(event) => setDraft({ ...draft, disabledDomains: splitDomains(event.target.value) })} /></Field>
        <Button onClick={() => save.mutate(draft)}>Save settings</Button>
      </Card>
      <Card className="space-y-4 p-5">
        <h2 className="font-serif text-2xl">Account</h2>
        <form className="grid gap-3 md:grid-cols-2" onSubmit={(event) => {
          event.preventDefault()
          const data = new FormData(event.currentTarget)
          changePassword.mutate({ currentPassword: String(data.get('current')), newPassword: String(data.get('next')) })
        }}>
          <Field label="Current password"><Input name="current" type="password" required /></Field>
          <Field label="New password"><Input name="next" type="password" minLength={10} required /></Field>
          <Button variant="outline" className="w-fit">Change password</Button>
        </form>
        <div className="space-y-2">
          {(sessions.data ?? []).map((session) => (
            <div key={session.id} className="flex items-center justify-between gap-3 text-sm">
              <span>{session.current ? 'This browser' : 'Other browser'} · {session.userAgent ?? 'Unknown device'}</span>
              <Button variant="ghost" size="sm" onClick={() => revoke.mutate(session.id)}>Revoke</Button>
            </div>
          ))}
        </div>
        <Button variant="ghost" onClick={() => authApi.logout().then(() => queryClient.invalidateQueries({ queryKey: ['me'] }))}>Log out</Button>
      </Card>
      <Card className="space-y-3 p-5">
        <h2 className="font-serif text-2xl">Data</h2>
        <div className="flex flex-wrap gap-2">
          <a className="inline-flex h-10 items-center rounded-xl border border-border px-4 text-sm" href="/api/v1/bookmarks/export">Export bookmarks</a>
          <a className="inline-flex h-10 items-center rounded-xl border border-border px-4 text-sm" href="/api/v1/reading-history/export">Export history</a>
        </div>
        <ImportBox />
        <p className="text-sm text-muted-foreground">Back up the PostgreSQL volume as well. JSON export does not replace a database backup.</p>
      </Card>
    </div>
  )
}

function Toggle({ label, checked, onChange }: { label: string; checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <label className="flex min-h-11 items-center justify-between gap-3 text-sm">
      {label}
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
    </label>
  )
}

function splitDomains(value: string) {
  return value.split(',').map((item) => item.trim()).filter(Boolean)
}

function ImportBox() {
  const queryClient = useQueryClient()
  const [message, setMessage] = useState('')
  const importFile = useMutation({
    mutationFn: async (file: File) => api<{ created: number; skipped: number }>('/api/v1/bookmarks/import', { method: 'POST', body: JSON.stringify({ content: await file.text() }) }),
    onSuccess: async (result) => {
      setMessage(`Imported ${result.created} bookmarks. Skipped ${result.skipped}.`)
      await queryClient.invalidateQueries({ queryKey: ['bookmarks'] })
    },
  })
  return (
    <div>
      <label className="inline-flex h-10 cursor-pointer items-center rounded-xl border border-border px-4 text-sm">
        Import Netscape HTML
        <input className="sr-only" type="file" accept=".html,text/html" onChange={(event) => {
          const file = event.target.files?.[0]
          if (file) importFile.mutate(file)
        }} />
      </label>
      {message ? <p className="mt-2 text-sm">{message}</p> : null}
    </div>
  )
}
