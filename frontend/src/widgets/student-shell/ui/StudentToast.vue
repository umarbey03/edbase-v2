<script setup lang="ts">
import { useToastMessage } from '@/features/student-toast/model/useToast'
import { AppIcon } from '@/shared/ui'

/**
 * Qisqa xabar (eski `.toast`).
 *
 * `role="status"` + `aria-live="polite"`: qulflangan darsni bosgan o'quvchi
 * sababni ko'z bilan ko'radi, ekran o'quvchisi esa eshitadi — aks holda
 * "bosdim, hech nima bo'lmadi" holati yuzaga kelardi.
 */
const message = useToastMessage()
</script>

<template>
  <Teleport to="body">
    <!--
      ★ PASTKI CHEKINISH XAVFSIZ MAYDONNI HISOBGA OLADI (2026-08-13).

      Ilgari `bottom-24` (96px) edi — u tab paneli ustidan o'tsin deb
      tanlangan. Lekin tab paneli 62px + `env(safe-area-inset-bottom)`:
      "tirnoqli" iPhone'da bu ~96px, ya'ni toast panelning AYNAN ostiga
      tushib, qulflangan darsning sababini o'quvchi umuman ko'rmasdi.

      Chekinish `StudentTabBar` dagi naqshga mos yozildi (o'sha yerda ham
      `env()` inline `style` da) — ikkalasi bir xil o'lchovga tayanadi.

      ★ DESKTOPDA (`lg:`) tab paneli YO'Q — u `lg:hidden`. 6rem lik chekinish
      o'sha yerda hech narsadan qochmaydi va toast ekran o'rtasida osilib
      turardi. Desktopda 1.5rem ga tushadi.

      ★ `!` (important) SHART: asosiy qiymat inline `style` da turibdi va
      inline uslub oddiy klassdan kuchliroq. Shell'dagi `lg:pb-0!` ham aynan
      shu sababdan shunday yozilgan.

      ★ `env()` desktopda ham QOLDIRILDI: `lg:` chegarasi 1024px — iPad
      yotiq holatda AYNAN shu tierga tushadi va uning "uy" chizig'i bor.
    -->
    <div
      class="pointer-events-none fixed left-1/2 z-[200] -translate-x-1/2 px-4 lg:bottom-[calc(1.5rem+env(safe-area-inset-bottom,0px))]!"
      style="bottom: calc(6rem + env(safe-area-inset-bottom, 0px))"
      role="status"
      aria-live="polite"
    >
      <p
        v-if="message !== null"
        class="flex max-w-[440px] animate-fade-up items-center gap-2 rounded-3xl border border-line bg-ink-800 px-5 py-3 text-sm text-slate-100 shadow-lg"
      >
        <AppIcon
          name="lock"
          :size="15"
          class="text-brand-500"
        />
        <span v-text="message" />
      </p>
    </div>
  </Teleport>
</template>
