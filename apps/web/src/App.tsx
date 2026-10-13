import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { AppShell } from './components/shell'
import { authApi } from './lib/api'
import { ThemeProvider } from './lib/theme'
import { ExtensionConnectPage, LoginPage, SetupPage } from './pages/auth'
import { BookmarksPage, CollectionsPage, TagsPage } from './pages/bookmarks'
import { DashboardPage } from './pages/dashboard'
import { HistoryPage } from './pages/history'
import { LibraryPage, ReadingItemPage } from './pages/library'
import { SettingsPage } from './pages/settings'

export function App() {
  const [queryClient] = useState(() => new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  }))

  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <Gate />
        </BrowserRouter>
      </QueryClientProvider>
    </ThemeProvider>
  )
}

function Gate() {
  const setup = useQuery({ queryKey: ['setup'], queryFn: authApi.setup })
  const me = useQuery({ queryKey: ['me'], queryFn: authApi.me })
  if (setup.isLoading || me.isLoading) return <p className="px-6 py-10 text-sm text-muted-foreground">Loading ReadingHub…</p>
  const signedIn = me.isSuccess
  const needsSetup = setup.data?.needsSetup ?? false

  return (
    <Routes>
      <Route path="/setup" element={needsSetup ? <SetupPage /> : <Navigate to={signedIn ? '/' : '/login'} replace />} />
      <Route path="/login" element={signedIn ? <Navigate to="/" replace /> : needsSetup ? <Navigate to="/setup" replace /> : <LoginPage />} />
      <Route path="/extension/connect" element={<ExtensionConnectPage signedIn={signedIn} />} />
      <Route element={signedIn ? <AppShell /> : <Navigate to={needsSetup ? '/setup' : '/login'} replace />}>
        <Route index element={<DashboardPage />} />
        <Route path="library" element={<LibraryPage />} />
        <Route path="library/:id" element={<ReadingItemPage />} />
        <Route path="bookmarks" element={<BookmarksPage />} />
        <Route path="collections" element={<CollectionsPage />} />
        <Route path="tags" element={<TagsPage />} />
        <Route path="history" element={<HistoryPage />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  )
}
