export type User = { id: string; email: string; displayName: string }
export type SetupStatus = { needsSetup: boolean }
export type AuthResponse = { user: User; csrfToken: string }

export type Tag = { id: string; name: string }
export type Bookmark = {
  id: string
  url: string
  normalizedUrl: string
  canonicalUrl: string | null
  title: string
  description: string | null
  siteName: string | null
  faviconUrl: string | null
  imageUrl: string | null
  notes: string | null
  createdAt: string
  updatedAt: string
  collectionIds: string[]
  tags: Tag[]
}
export type Page<T> = { items: T[]; nextCursor: string | null }
export type Collection = {
  id: string
  parentId: string | null
  name: string
  description: string | null
  icon: string | null
  color: string | null
  isPinned: boolean
  sortOrder: number
  bookmarkCount: number
}

export type ReadingStatus = 'planToRead' | 'reading' | 'onHold' | 'completed' | 'dropped'
export type SourceStatus = 'active' | 'paused' | 'broken'
export type ChapterSummary = {
  id: string
  url: string
  label: string
  title: string | null
  number: number | null
  progressPercent: number | null
  revision: number | null
  completedAt: string | null
}
export type ReadingSource = {
  id: string
  url: string
  domain: string
  externalKey: string | null
  status: SourceStatus
  lastDetectedChapterLabel: string | null
  furthestCompletedChapterId: string | null
  currentChapter: ChapterSummary | null
}
export type ReadingItem = {
  id: string
  title: string
  description: string | null
  coverImageUrl: string | null
  notes: string | null
  status: ReadingStatus
  bookmarkId: string | null
  activeSourceId: string | null
  activeChapterId: string | null
  cursorRevision: number
  lastReadAt: string | null
  createdAt: string
  progressPercent: number | null
  activeChapterLabel: string | null
  sources: ReadingSource[]
}
export type HistoryEvent = {
  id: string
  eventType: string
  itemTitle: string
  chapterLabel: string | null
  detail: string | null
  occurredAt: string
  readingItemId: string | null
}
export type Settings = {
  theme: 'light' | 'dark' | 'system'
  locale: string
  dashboardView: string
  defaultCollectionId: string | null
  autoTracking: boolean
  autoComplete: boolean
  completionThreshold: number
  historyEnabled: boolean
  positionRestoration: boolean
  syncDebounceSeconds: number
  enabledDomains: string[]
  disabledDomains: string[]
}
export type ContinueCard = {
  id: string
  title: string
  coverImageUrl: string | null
  chapterLabel: string | null
  progressPercent: number | null
  lastReadAt: string | null
}
export type Dashboard = {
  continueReading: ContinueCard[]
  recentBookmarks: Bookmark[]
  recentlyRead: HistoryEvent[]
  pinnedCollections: Collection[]
}
export type ProgressResult = {
  chapterId: string
  progressPercent: number
  revision: number
  applied: boolean
  completed: boolean
}
export type WebSession = {
  id: string
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  userAgent: string | null
  current: boolean
}
