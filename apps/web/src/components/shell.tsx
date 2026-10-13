import { BookMarked, BookOpen, Clock, FolderTree, LayoutDashboard, LogOut, Menu, Settings, Tags, X } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { authApi } from '../lib/api'
import { cn } from '../lib/utils'
import { Button } from './ui'

const links = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/library', label: 'My Library', icon: BookOpen },
  { to: '/bookmarks', label: 'Bookmarks', icon: BookMarked },
  { to: '/collections', label: 'Collections', icon: FolderTree },
  { to: '/tags', label: 'Tags', icon: Tags },
  { to: '/history', label: 'Reading History', icon: Clock },
  { to: '/settings', label: 'Settings', icon: Settings },
]

export function AppShell() {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const logout = useMutation({
    mutationFn: authApi.logout,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      navigate('/login')
    },
  })

  return (
    <div className="min-h-screen md:grid md:grid-cols-[16rem_1fr]">
      <header className="sticky top-0 z-30 flex items-center justify-between border-b border-border bg-background/90 px-4 py-3 backdrop-blur md:hidden">
        <span className="font-serif text-xl">ReadingHub</span>
        <Button variant="ghost" size="icon" aria-label="Open menu" className="md:hidden" onClick={() => setOpen(true)}>
          <Menu className="size-5" />
        </Button>
      </header>
      {open ? <button aria-label="Close menu" className="fixed inset-0 z-40 bg-black/40 md:hidden" onClick={() => setOpen(false)} /> : null}
      <aside className={cn('fixed inset-y-0 left-0 z-50 flex w-64 flex-col border-r border-border bg-card p-4 transition md:static md:translate-x-0', open ? 'translate-x-0' : '-translate-x-full')}>
        <div className="mb-6 flex items-center justify-between px-2">
          <div>
            <p className="font-serif text-2xl leading-none">ReadingHub</p>
            <p className="mt-1 text-xs text-muted-foreground">Bookmarks and reading</p>
          </div>
          <Button variant="ghost" size="icon" className="md:hidden" aria-label="Close navigation" onClick={() => setOpen(false)}>
            <X className="size-4" />
          </Button>
        </div>
        <nav className="flex flex-1 flex-col gap-1">
          {links.map((link) => {
            const Icon = link.icon
            return (
              <NavLink
                key={link.to}
                to={link.to}
                end={link.end}
                onClick={() => setOpen(false)}
                className={({ isActive }) =>
                  cn('flex min-h-11 items-center gap-3 rounded-xl px-3 text-sm', isActive ? 'bg-accent font-medium' : 'text-muted-foreground hover:bg-accent')
                }
              >
                <Icon className="size-4" />
                {link.label}
              </NavLink>
            )
          })}
        </nav>
        <Button variant="ghost" className="justify-start" onClick={() => logout.mutate()}>
          <LogOut className="size-4" />
          Log out
        </Button>
      </aside>
      <main className="mx-auto w-full max-w-6xl px-4 py-6 md:px-8 md:py-8">
        <Outlet />
      </main>
    </div>
  )
}
