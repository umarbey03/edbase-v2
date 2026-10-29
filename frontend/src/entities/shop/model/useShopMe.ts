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
  /**
   * Ikkala so'rov ham keldi — o'quvchi turi (guruh / mustaqil / mehmon)
   * aniq. ★ Shu paytgacha shell tab va sahifa CHIZMAYDI (audit,
   * 2026-10-29): aks holda mehmon va mustaqil o'quvchiga bir lahza
   * guruhning 5 tabi va guruh bosh sahifasi ko'rinib ketardi.
   */
  isLoaded: ComputedRef<boolean>
  /** Biror so'rov yiqildi — shell «Qayta urinish» ni ko'rsatadi. */
  isError: ComputedRef<boolean>
  retry: () => void
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

  const isError = computed(() => query.isError.value || subscription.isError.value)
  function retry(): void {
    if (query.isError.value) void query.refetch()
    if (subscription.isError.value) void subscription.refetch()
  }

  return { data, isGuest, isLoaded, isError, retry }
}
