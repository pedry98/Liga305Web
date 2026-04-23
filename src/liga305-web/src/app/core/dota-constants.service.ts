import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface HeroInfo {
  id: number;
  name: string;            // internal, e.g. "npc_dota_hero_antimage"
  localizedName: string;   // display, e.g. "Anti-Mage"
  iconUrl: string;         // Valve CDN portrait
}

export interface ItemInfo {
  id: number;
  name: string;            // internal, e.g. "blink"
  localizedName: string;   // display, e.g. "Blink Dagger"
  iconUrl: string;         // Valve CDN icon
}

const CACHE_KEY = 'liga305:dotaConstants';
const CACHE_TTL_MS = 7 * 24 * 3600 * 1000; // 7 days
const CDN_BASE = 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react';

/**
 * Lazy loader + localStorage cache for OpenDota's /constants/heroes and
 * /constants/items. Single-fetch per session, 7-day persistent cache.
 *
 * If the fetch fails (offline, OpenDota down), the service quietly degrades:
 * the signals stay empty and UI falls back to showing raw IDs.
 */
@Injectable({ providedIn: 'root' })
export class DotaConstantsService {
  private readonly http = inject(HttpClient);

  readonly heroesById = signal<Map<number, HeroInfo>>(new Map());
  readonly itemsById = signal<Map<number, ItemInfo>>(new Map());
  readonly loaded = signal(false);

  private loadPromise: Promise<void> | null = null;

  /** Trigger a lazy load. Safe to call repeatedly — only fetches once. */
  ensureLoaded(): Promise<void> {
    if (this.loadPromise) return this.loadPromise;
    this.loadPromise = this.load();
    return this.loadPromise;
  }

  private async load(): Promise<void> {
    // Try cache first.
    try {
      const raw = localStorage.getItem(CACHE_KEY);
      if (raw) {
        const cached = JSON.parse(raw) as { fetchedAt: number; heroes: HeroInfo[]; items: ItemInfo[] };
        if (Date.now() - cached.fetchedAt < CACHE_TTL_MS) {
          this.heroesById.set(new Map(cached.heroes.map(h => [h.id, h])));
          this.itemsById.set(new Map(cached.items.map(i => [i.id, i])));
          this.loaded.set(true);
          return;
        }
      }
    } catch { /* fall through to network */ }

    try {
      const [heroesRaw, itemsRaw] = await Promise.all([
        firstValueFrom(this.http.get<Record<string, RawHero>>('https://api.opendota.com/api/constants/heroes')),
        firstValueFrom(this.http.get<Record<string, RawItem>>('https://api.opendota.com/api/constants/items'))
      ]);

      const heroes: HeroInfo[] = Object.values(heroesRaw).map(h => {
        const slug = (h.name ?? '').replace(/^npc_dota_hero_/, '');
        return {
          id: h.id,
          name: h.name,
          localizedName: h.localized_name,
          iconUrl: slug ? `${CDN_BASE}/heroes/${slug}.png` : ''
        };
      });

      const items: ItemInfo[] = Object.entries(itemsRaw).map(([key, v]) => ({
        id: v.id,
        name: key,
        localizedName: v.dname ?? key,
        iconUrl: v.img ? `https://cdn.cloudflare.steamstatic.com${v.img.startsWith('/') ? v.img : '/' + v.img}` : `${CDN_BASE}/items/${key}.png`
      }));

      this.heroesById.set(new Map(heroes.map(h => [h.id, h])));
      this.itemsById.set(new Map(items.map(i => [i.id, i])));
      this.loaded.set(true);

      try {
        localStorage.setItem(CACHE_KEY, JSON.stringify({ fetchedAt: Date.now(), heroes, items }));
      } catch { /* quota exceeded — non-fatal */ }
    } catch {
      // Degrade silently. UI shows raw IDs.
      this.loaded.set(true);
    }
  }

  hero(id: number | null | undefined): HeroInfo | null {
    if (id == null) return null;
    return this.heroesById().get(id) ?? null;
  }

  item(id: number | null | undefined): ItemInfo | null {
    if (id == null || id === 0) return null;
    return this.itemsById().get(id) ?? null;
  }
}

interface RawHero {
  id: number;
  name: string;
  localized_name: string;
}

interface RawItem {
  id: number;
  dname?: string;
  img?: string;
}
