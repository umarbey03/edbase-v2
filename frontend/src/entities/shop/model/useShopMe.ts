import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { useMySubscription } from '@/entities/subscription'

import { fetchShopMe } from '../api/shop-api'
import type { ShopMeDto } from './types'

/** Bitta kalit — shell, tariflar sahifasi va bosh sahifa bitta so'rovdan oziqlanadi. */
export const SHOP_ME_KEY = ['shop', 'me'] as const

export interface ShopMe {
  data: ComputedRef<ShopMeDto | null>
  /**
   * MEHMON — saytda o'zi ro'yxatdan o'tgan, hali na guruhi, na obunasi bor
   * o'quvchi. Unga faqat «Tariflar» va «Profil» ko'rinadi.
   *
   * ★ FAQAT KO'RINISH UCHUN: himoya serverda (guruh endpoint'lari unga bo'sh
   *   javob yoki 403 beradi, markaz reytingi bo'sh).
   */
  isGuest: ComputedRef<boolean>
  isLoaded: ComputedRef<boolean>
}

export function useShopMe(): ShopMe {
  const query = useQuery({
    queryKey: SHOP_ME_KEY,
    queryFn: ({ signal }) => fetchShopMe({ signal }),
    staleTime: 30_000,
  })
  const subscription = useMySubscription()

  const data = computed(() => query.data.value ?? null)
  const isLoaded = computed(() => data.value !== null && subscription.isLoaded.value)
  const isGuest = computed(
    () => isLoaded.value && data.value?.inGroup === false && !subscription.isSelfStudy.value,
  )

  return { data, isGuest, isLoaded }
}
