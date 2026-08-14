import { onScopeDispose, readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'

/**
 * CHEGARA KUZATUVCHISI — `matchMedia` ustidagi yupqa qobiq.
 *
 * NEGA UMUMAN KERAK: 2026-08-13 dagi auditgacha ilovada `matchMedia`,
 * `ResizeObserver`, `window.innerWidth` va `orientationchange` UMUMAN
 * ishlatilmagan edi — butun moslashuvchanlik sof CSS'da edi. CSS bilan
 * `hidden lg:block` naqshi ishlaydi, lekin u IKKALA daraxtni ham QURADI:
 * telefonda desktop jadvali ham mount bo'ladi, ma'lumot bilan to'ladi va
 * hech qachon ko'rsatilmaydi. 12 ta sahifada shunday.
 *
 * Bu kompozitsiya `v-if` bilan HAQIQIY tarmoqlanishni beradi:
 *
 * ```vue
 * <script setup lang="ts">
 * const { isDesktop } = useBreakpoint()
 * </script>
 *
 * <template>
 *   <StudentCardList v-if="!isDesktop" :rows="rows" />
 *   <StudentTable v-else :rows="rows" />
 * </template>
 * ```
 *
 * ★ QIYMATLAR `style.css` DAGI `--breakpoint-*` BILAN BIR XIL BO'LISHI SHART.
 * Ikki manba bo'lgani uchun ular qo'lda sinxron saqlanadi: CSS o'zgaruvchisini
 * JS'dan o'qish mumkin edi (`getComputedStyle`), lekin u har chaqiruvda
 * layout'ni majburlaydi (reflow) va SSR'da umuman ishlamaydi. Chegara
 * qiymatlari yilda bir marta o'zgaradi — nusxa arzonroq.
 *
 * ★ `min-width` PIKSELDA, `rem` DA EMAS: foydalanuvchi brauzerning asosiy
 * shrift o'lchamini kattalashtirsa `rem` li media query siljiydi va JS bilan
 * CSS turli javob berardi. Tailwind `rem` ishlatadi, lekin ILDIZ shrift
 * o'lchami bu loyihada o'zgarmaydi (`html` da `font-size` yo'q), shuning
 * uchun 35rem = 560px doim to'g'ri.
 */

/** `style.css` `@theme` bloki bilan bir xil. O'zgartirsangiz IKKALASINI. */
export const BREAKPOINTS = {
  xs: 560,
  sm: 640,
  md: 768,
  lg: 1024,
  xl: 1280,
} as const

export type BreakpointName = keyof typeof BREAKPOINTS

/**
 * Bitta media query'ni kuzatadi.
 *
 * ★ SSR/test himoyasi: `window` bo'lmasa `false` qaytaradi va hech narsaga
 * obuna bo'lmaydi — `vue-tsc` va Node muhitida yiqilmasin.
 */
export function useMediaQuery(query: string): DeepReadonly<Ref<boolean>> {
  const matches = ref(false)

  if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
    const list = window.matchMedia(query)
    matches.value = list.matches

    const onChange = (event: MediaQueryListEvent): void => {
      matches.value = event.matches
    }

    list.addEventListener('change', onChange)
    onScopeDispose(() => {
      list.removeEventListener('change', onChange)
    })
  }

  return readonly(matches)
}

/** Berilgan chegaradan KENG yoki teng ekanini kuzatadi. */
export function useMinWidth(name: BreakpointName): DeepReadonly<Ref<boolean>> {
  return useMediaQuery(`(min-width: ${BREAKPOINTS[name]}px)`)
}

export interface BreakpointState {
  /** ≥ 560px — o'quvchi bosh sahifasida ikkita kartochka yonma-yon. */
  isXs: DeepReadonly<Ref<boolean>>
  /** ≥ 640px — modal markazlashadi (varaqa emas, dialog). */
  isSm: DeepReadonly<Ref<boolean>>
  /** ≥ 768px — iPad tik holati. */
  isMd: DeepReadonly<Ref<boolean>>
  /** ≥ 1024px — yon menyu VA jadval shu yerda paydo bo'ladi. */
  isLg: DeepReadonly<Ref<boolean>>
  /** ≥ 1280px — keng desktop. */
  isXl: DeepReadonly<Ref<boolean>>
  /**
   * `isLg` bilan bir xil, lekin NIYATNI bildiradi: "bu yerda desktop
   * joylashuvi ko'rsatiladi". Chegara siljisa bitta joy o'zgaradi.
   */
  isDesktop: DeepReadonly<Ref<boolean>>
  /**
   * Teginish qurilmasi (sichqoncha yo'q). Ekran kengligidan MUSTAQIL —
   * iPad 1024px bo'lsa ham teginish qurilmasi.
   */
  isTouch: DeepReadonly<Ref<boolean>>
  /**
   * Bo'yi past yotiq ekran — telefon yotiq holatda. Jonli dars sahifasi
   * uchun: balandlik 500px dan kam bo'lsa video deyarli qolmaydi.
   */
  isShortLandscape: DeepReadonly<Ref<boolean>>
}

/**
 * Barcha bosqichlarni bir marta e'lon qiladi.
 *
 * ★ Har chaqiruv O'Z `MediaQueryList` larini yaratadi va komponent scope'i
 * tugaganda tozalaydi (`onScopeDispose`). Global singleton ATAYLAB emas:
 * obunachilar soni oz (o'nlab, minglab emas) va global holat testda
 * komponentlar orasida oqib ketardi.
 */
export function useBreakpoint(): BreakpointState {
  return {
    isXs: useMinWidth('xs'),
    isSm: useMinWidth('sm'),
    isMd: useMinWidth('md'),
    isLg: useMinWidth('lg'),
    isXl: useMinWidth('xl'),
    isDesktop: useMinWidth('lg'),
    isTouch: useMediaQuery('(pointer: coarse)'),
    isShortLandscape: useMediaQuery('(orientation: landscape) and (max-height: 500px)'),
  }
}
