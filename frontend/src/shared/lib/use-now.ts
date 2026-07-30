import { onScopeDispose, ref } from 'vue'
import type { Ref } from 'vue'

/**
 * Har sekundda yangilanadigan "hozirgi vaqt" — BUTUN ILOVA UCHUN BITTA.
 *
 * NEGA SINGLETON: o'quvchi bosh sahifasida orqaga sanoq (kun/soat/daqiqa/sek),
 * appbar'dagi "keyingi dars" chipi va kalendar bir vaqtda vaqtga qaraydi.
 * Har biri o'z `setInterval` ini ochsa, taymerlar bir-biriga nisbatan surilib,
 * ekranning ikki joyida turli sekund raqami ko'rinardi — eski ilovada aynan
 * shu muammo bor edi (`startAllCountdowns` va `setInterval(tickMini, 1000)`
 * alohida yurardi). Bitta manba bo'lsa hamma joy bir zumda mos keladi.
 *
 * Obunachilar soni nolga tushganda taymer to'xtaydi: xodim sahifalarida
 * (o'quvchi karkasidan tashqarida) fonda ishlab turgan interval qolmaydi.
 */
const now = ref(new Date())
let subscribers = 0
let timer: number | null = null

function tick(): void {
  // Fon rejimida qayta chizish shart emas: brauzer interval'ni baribir
  // sekinlashtiradi, biz esa ortiqcha render'ni butunlay to'xtatamiz.
  if (document.hidden) return
  now.value = new Date()
}

export function useNow(): Ref<Date> {
  subscribers += 1
  if (timer === null) {
    now.value = new Date()
    timer = window.setInterval(tick, 1000)
  }

  onScopeDispose(() => {
    subscribers -= 1
    if (subscribers <= 0 && timer !== null) {
      window.clearInterval(timer)
      timer = null
      subscribers = 0
    }
  })

  return now
}
