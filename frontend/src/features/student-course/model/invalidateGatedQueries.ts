import type { QueryClient } from '@tanstack/vue-query'

/**
 * Gating holatiga bog'liq o'quvchi ro'yxatlarini eskirtiradi.
 *
 * Server vazifa/test topshirilganda yoki video ko'rilganda o'zining gating
 * keshini DARHOL bekor qiladi (`GatingService.InvalidateAsync`). Klientda
 * esa kurs daraxti 5 daqiqa (`staleTime`) saqlanadi — shu chaqiruvsiz
 * keyingi dars o'quvchiga 5 daqiqagacha "yopiq" ko'rinib turardi.
 *
 * `['courses']` prefiksi `['courses','student']` va `['courses','tree',id]`
 * ikkalasini qamraydi; testlar va vazifalar ham yangilanadi, chunki ochilgan
 * darsning testi/vazifasi shu ro'yxatlarda endi paydo bo'ladi.
 */
export function invalidateGatedQueries(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: ['courses'] })
  void queryClient.invalidateQueries({ queryKey: ['tests', 'available'] })
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}
