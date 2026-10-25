import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchMySubscription } from '../api/subscription-api'
import type { MySubscriptionDto } from './types'

export interface MySubscription {
  data: ComputedRef<MySubscriptionDto | null>
  /**
   * Mustaqil o'quvchimi. Guruh va obuna server tomonda bir-birini
   * taqiqlaydi, ya'ni obunasi (faol yoki tugagan) bor o'quvchi — mustaqil.
   *
   * ★ FAQAT KO'RINISH UCHUN: qaysi tab va qaysi kartochka chiqishini
   *   hal qiladi. Himoya serverda — mustaqil o'quvchi guruh
   *   endpoint'laridan baribir bo'sh javob yoki 403 oladi.
   */
  isSelfStudy: ComputedRef<boolean>
  isLoaded: ComputedRef<boolean>
}

/** Bitta kalit — shell, bosh sahifa va profil bitta so'rovdan oziqlanadi. */
export const MY_SUBSCRIPTION_KEY = ['subscriptions', 'me'] as const

export function useMySubscription(): MySubscription {
  const query = useQuery({
    queryKey: MY_SUBSCRIPTION_KEY,
    queryFn: ({ signal }) => fetchMySubscription({ signal }),
    staleTime: 5 * 60_000,
  })

  const data = computed(() => query.data.value ?? null)

  return {
    data,
    isSelfStudy: computed(() => data.value?.hasSubscription === true),
    isLoaded: computed(() => query.data.value !== undefined),
  }
}
