<script setup lang="ts">
import { BaseCard } from '@/shared/ui'

/**
 * Eski ilovadagi `kpiCard(label,val,color,sub)` (academic.html, 2616–2619):
 * markazlangan matn, tepada 3px rangli chiziq, 26px qalin son, ostida
 * BOSH HARFLI kichkina yorliq va ixtiyoriy izoh.
 *
 * ★ YANGI KARTOCHKA KOMPONENTI EMAS: sirt sifatida `shared/ui` dagi
 * `BaseCard` ishlatiladi (chegara, radius, fon — hammasi o'shanikidan),
 * bu yerda faqat KPI joylashuvi qo'shiladi. Aks holda dizayn tizimida
 * ikkita "kartochka" paydo bo'lib, tema o'zgarganda ular ajralib ketardi.
 *
 * `value` — TAYYOR MATN. Formatlash chaqiruvchida bajariladi
 * (`formatMoney` / `collectionRateLabel`), shunda bu komponent pul va foizni
 * farqlashi shart emas va ikkinchi formatlovchi tug'ilmaydi.
 */
withDefaults(
  defineProps<{
    label: string
    value: string
    /**
     * Kartochkaning 3px YUQORI CHIZIG'I — `model/finance-view.ts` dagi
     * `KPI_ACCENTS`. Faqat shu chiziq; RAQAM ENDI SIYOH RANGIDA (quyidagi
     * izoh). Prop nomi `color` dan `accent` ga o'zgartirildi — aks holda
     * "raqam ham shu rangda" degan yolg'on taassurot qolardi.
     */
    accent: string
    /** Yorliq ostidagi kichik izoh (eski `sub`). Bo'sh bo'lsa chizilmaydi. */
    sub?: string
  }>(),
  { sub: '' },
)
</script>

<template>
  <BaseCard
    class="text-center"
    :style="{ borderTopWidth: '3px', borderTopColor: accent }"
  >
    <!--
      🔴 RAQAM RANGSIZ (`slate-50` siyohi, 18.9:1). Ilgari `:style="{ color }"`
      bilan urg'u rangida edi.

      Sabab (`dataviz` qoidasi): "qiymat va yorliq SIYOH tokenlarida, rang esa
      yonidagi BELGIDA". Qorong'i navy fonda rangli raqam TO'G'RI yechim edi —
      u yerda yorqin rang o'qiladigan variant. Oq kartochkada teskarisi:
      `#f2c84b` 1.71:1, `#22c55e` 2.03:1 berardi, ya'ni "Yig'ilish foizi" va
      "Yig'ilgan" raqamlari amalda KO'RINMASDI. Ma'no yo'qolmaydi — rang
      kartochkaning yuqori chizig'ida qoladi va yonida yorliq MATNI turadi
      (rang hech qayerda YAKKA ma'no tashimaydi).

      `tabular-nums` SAQLANADI, `dataviz` ning "katta raqam proporsional
      bo'lsin" tavsiyasiga qaramay: kartochkalar setkada yonma-yon turadi va
      raqamlar ustun bo'lib o'qiladi — o'zgaruvchan kenglikda ular "sakraydi".
    -->
    <p
      class="text-[26px] font-extrabold leading-[1.15] tabular-nums text-slate-50"
      v-text="value"
    />
    <p
      class="mt-1.5 text-[11.5px] uppercase tracking-[0.4px] text-muted"
      v-text="label"
    />
    <p
      v-if="sub.length > 0"
      class="mt-1 text-[11px] text-muted"
      v-text="sub"
    />
  </BaseCard>
</template>
