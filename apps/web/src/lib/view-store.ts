import { create } from 'zustand'

type ViewMode = 'grid' | 'list'

type ViewState = {
  bookmarks: ViewMode
  library: ViewMode
  setBookmarks: (mode: ViewMode) => void
  setLibrary: (mode: ViewMode) => void
}

function read(key: string): ViewMode {
  return localStorage.getItem(key) === 'list' ? 'list' : 'grid'
}

export const useViewStore = create<ViewState>((set) => ({
  bookmarks: read('rh-bookmarks-view'),
  library: read('rh-library-view'),
  setBookmarks: (mode) => {
    localStorage.setItem('rh-bookmarks-view', mode)
    set({ bookmarks: mode })
  },
  setLibrary: (mode) => {
    localStorage.setItem('rh-library-view', mode)
    set({ library: mode })
  },
}))
