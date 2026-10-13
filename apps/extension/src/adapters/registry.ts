import { genericAdapter } from './generic'
import { royalRoadAdapter } from './royalroad'
import type { Detection, SiteAdapter } from './types'
import { webnovelAdapter } from './webnovel'

export const adapters: SiteAdapter[] = [royalRoadAdapter, webnovelAdapter]

export function detectPage(document: Document, url: URL): Detection {
  const adapter = adapters.find((candidate) => candidate.matches(url)) ?? genericAdapter
  return adapter.detect(document, url)
}

export const confirmationThreshold = 0.6
