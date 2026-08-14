<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  collectionRateLabel,
  downloadPaymentSummaryCsv,
  fetchPaymentSummary,
  isValidIsoDate,
  monthStartIsoDate,
  periodRangeLabel,
  todayIsoDate,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { saveBlob } from '@/shared/lib/download'
import { formatMoney } from '@/shared/lib/money'
import type { PaymentSummaryParams } from '@/entities/payment'
import { AppIcon, BaseButton, DataStatus, PageHeader } from '@/shared/ui'

import { isoDateLabel, KPI_ACCENTS } from '../model/finance-view'
import FinanceAgingCard from './FinanceAgingCard.vue'
import FinanceGroupsCard from './FinanceGroupsCard.vue'
import FinanceKpiTile from './FinanceKpiTile.vue'
import FinanceMethodsCard from './FinanceMethodsCard.vue'
import FinanceTrendCard from './FinanceTrendCard.vue'

/**
 * MOLIYA DASHBOARD'I — eski ilovadagi "Moliya" bo'limining tepa qismi
 * (`Zinnur-platform/app/templates/academic.html`, 853–902-qatorlar).
 *
 * Tartib, sarlavhalar va matnlar o'sha yerdan AYNAN ko'chirilgan:
 *   h1 "Moliya" + "Tushum, qarz va chegirmalar bo'yicha umumiy manzara" (857–858)
 *   -> davr filtri va "Excel" tugmasi (861–865)
 *   -> KPI kartochkalari (869–872, qiymatlari 2668–2674)
 *   -> "Qarz yoshi" (875–880)
 *   -> "Oxirgi 12 oy" (885–889)
 *   -> "Guruhlar bo'yicha" va "To'lov usuli bo'yicha" (892–902).
 *
 * ★ NEGA SAHIFA SARLAVHASI SHU YERDA: davr filtri va eksport tugmasi eski
 * dizaynda h1 bilan BIR QATORDA turadi va ikkalasi ham shu komponentning
 * holatiga tayanadi. Ularni sahifaga chiqarsak, filtr holati sahifada
 * yashab, biznes mantiq sahifaga sizib o'tardi (FSD qoidasi: sahifa faqat
 * yig'adi).
 *
 * ★★ UCH XIL MA'NO — EKRANDA AJRATIB KO'RSATILADI:
 *   • HISOB (accrual) — `fromPeriod..toPeriod` OYLARI: reja, yig'ilgan,
 *     yig'ilish foizi, chegirmalar, guruh kesimi;
 *   • HOLAT — BUGUNGI kesim: umumiy qarz, balans, qarz yoshi. Davr filtriga
 *     BOG'LIQ EMAS;
 *   • DAVR (jurnal) — `from..to` KUNLARIDA kassaga tushgan pul.
 * Aks holda kassir davrni o'zgartirganda qarz o'zgarmasligini bug deb
 * o'ylardi, yoki "Yig'ilgan" bilan "Kassaga tushgan" ni bir xil deb hisoblab,
 * hisobotni ikki marta yozardi.
 *
 * ★ TOZALASH KERAK EMAS: bu komponent taymer ham, obuna ham ochmaydi;
 * so'rovlarni `vue-query` `AbortSignal` bilan o'zi bekor qiladi, CSV
 * uchun yaratilgan `objectURL` esa `saveBlob` ichida bekor qilinadi.
 * Shu sababli `onBeforeUnmount` yozilmagan — bo'sh hook faqat chalg'itardi.
 */

/** Kartochka tavsifi — shablonni 11 marta takrorlamaslik uchun. */
interface KpiTile {
  key: string
  label: string
  value: string
  /** Kartochkaning 3px yuqori chizig'i (raqam siyoh rangida — `FinanceKpiTile`). */
  accent: string
  sub: string
}

/*
  Standart davr — joriy oy boshi..bugun. Server BERILMAGANDA ham aynan shuni
  oladi, lekin maydonlar ATAYLAB to'ldirib ko'rsatiladi: bo'sh filtr
  ko'rgan kassir qaysi oraliqni o'qiyotganini bilmasdi.
*/
const from = ref(monthStartIsoDate())
const to = ref(todayIsoDate())

/*
  Yaroqsiz (bo'sh yoki chala) sana serverga YUBORILMAYDI — u 400 qaytarib,
  butun dashboard o'rniga xato ekrani chiqardi. Bunday holda server o'z
  standartini qo'llaydi va ekranda haqiqatan ISHLATILGAN oraliq
  (`summary.from` / `summary.to`) ko'rsatiladi.
*/
const params = computed<PaymentSummaryParams>(() => ({
  from: isValidIsoDate(from.value) ? from.value : undefined,
  to: isValidIsoDate(to.value) ? to.value : undefined,
}))

const datesIncomplete = computed(
  () => !isValidIsoDate(from.value) || !isValidIsoDate(to.value),
)

/*
  `queryKey` davr filtriga BOG'LANGAN: har oraliq alohida keshlanadi, orqaga
  qaytilganda so'rov takrorlanmaydi va bir vaqtda kelgan bir xil so'rovlar
  birlashtiriladi (dedup).
*/
const summaryQuery = useQuery({
  queryKey: ['payments', 'summary', params],
  queryFn: ({ signal }) => fetchPaymentSummary(params.value, { signal }),
})

const summary = computed(() => summaryQuery.data.value ?? null)

/*
  Xato matnini O'ZIMIZ yig'maymiz: `toUserMessage` 400 dagi
  `problem.errors` (masalan `from > to`) va 409 dagi `detail` ni to'g'ri
  o'qiydi — server nima deganini foydalanuvchi ko'radi.
*/
const errorMessage = computed(() =>
  summaryQuery.error.value !== null ? toUserMessage(summaryQuery.error.value) : null,
)

function retry(): void {
  void summaryQuery.refetch()
}

/* ------------------------------------------------------------- eksport --- */

const exportError = ref<string | null>(null)

/*
  ★ `http.download` (entity ichida) — brauzer navigatsiyasi `Authorization`
  sarlavhasini yubormaydi va oddiy havola 401 olardi. Fayl nomi serverning
  `Content-Disposition` idan olinadi.
*/
const exportMutation = useMutation({
  mutationFn: () => downloadPaymentSummaryCsv(params.value),
  onSuccess: (file) => {
    exportError.value = null
    saveBlob(file.blob, file.fileName)
  },
  onError: (error: Error) => {
    exportError.value = toUserMessage(error)
  },
})

/* ------------------------------------------------------------- yorliqlar - */

/** HISOB raqamlari qaysi oylarga tegishli. */
const periodsLabel = computed(() => {
  const data = summary.value
  return data === null ? '' : periodRangeLabel(data.fromPeriod, data.toPeriod)
})

/** Server HAQIQATAN ishlatgan kunlar oralig'i (biz so'raganimiz emas). */
const rangeLabel = computed(() => {
  const data = summary.value
  return data === null ? '' : `${isoDateLabel(data.from)} — ${isoDateLabel(data.to)}`
})

const asOfLabel = computed(() => {
  const data = summary.value
  return data === null ? '' : formatDateTime(data.asOf)
})

/**
 * Asosiy KPI — eski ilovadagi OLTITA kartochka, o'sha TARTIBDA va o'sha
 * NOMLAR bilan (2668–2674).
 *
 * ★ "Yig'ilgan" uchun `periodCollected` olinadi, `collected` EMAS: eski
 * ilovada bu raqam "Rejadagi tushum" bilan bir xil o'lchovda edi (oylik
 * hisob) va "Yig'ilish foizi" aynan shundan chiqadi. Kassaga tushgan pul
 * pastdagi "Kassa jurnali" blokida alohida ko'rsatiladi.
 *
 * ★ Eskisidagi "o'tgan oyga nisbatan o'sish" izohi YO'Q: yangi shartnomada
 * bunday maydon yo'q va uni `months[]` dan hisoblab chiqarish tanlangan
 * davrga mos kelmasligi mumkin edi — noto'g'ri foiz hisobotni yolg'on
 * qilardi, shuning uchun o'ylab topilmadi.
 */
const mainTiles = computed<KpiTile[]>(() => {
  const data = summary.value
  if (data === null) return []
  const kpi = data.kpi
  return [
    {
      key: 'billed',
      label: 'Rejadagi tushum',
      value: formatMoney(kpi.billed),
      accent: KPI_ACCENTS.planned,
      sub: periodsLabel.value,
    },
    {
      key: 'periodCollected',
      label: 'Yig‘ilgan',
      value: formatMoney(kpi.periodCollected),
      accent: KPI_ACCENTS.collected,
      sub: 'shu oylarga tegishli',
    },
    {
      key: 'collectionRate',
      label: 'Yig‘ilish foizi',
      value: collectionRateLabel(kpi.collectionRate),
      accent: KPI_ACCENTS.rate,
      sub: '',
    },
    {
      key: 'outstanding',
      label: 'Umumiy qarz',
      value: formatMoney(kpi.outstanding),
      accent: KPI_ACCENTS.debt,
      sub: `${kpi.debtorStudents} qarzdor · bugungi holat`,
    },
    {
      key: 'discounts',
      label: 'Chegirmalar',
      value: formatMoney(kpi.discounts),
      accent: KPI_ACCENTS.discounts,
      sub: '',
    },
    {
      key: 'studentBalance',
      label: 'Balansdagi pul',
      value: formatMoney(kpi.studentBalance),
      accent: KPI_ACCENTS.balance,
      sub: 'oldindan to‘lovlar',
    },
  ]
})

/**
 * KASSA JURNALI — eski ilovada BO'LMAGAN blok (o'sha tizimda pul harakati
 * jurnali yo'q edi). Backend endi uni beradi va u kassir uchun kunlik
 * hisobotning ASOSIY raqami, shuning uchun qo'shildi — lekin ALOHIDA
 * sarlavha ostida, yuqoridagi hisob raqamlari bilan chalkashmasin.
 */
const journalTiles = computed<KpiTile[]>(() => {
  const data = summary.value
  if (data === null) return []
  const kpi = data.kpi
  return [
    {
      key: 'collected',
      label: 'Kassaga tushgan',
      value: formatMoney(kpi.collected),
      accent: KPI_ACCENTS.collected,
      sub: `${kpi.paymentCount} ta to‘lov · ${kpi.payingStudents} o‘quvchi`,
    },
    {
      key: 'refunded',
      label: 'Qaytarilgan',
      value: formatMoney(kpi.refunded),
      accent: KPI_ACCENTS.debt,
      sub: '',
    },
    {
      key: 'netCollected',
      label: 'Sof tushum',
      value: formatMoney(kpi.netCollected),
      accent: KPI_ACCENTS.planned,
      sub: 'tushgan − qaytarilgan',
    },
    {
      key: 'balanceUsed',
      label: 'Balansdan yopilgan',
      value: formatMoney(kpi.balanceUsed),
      accent: KPI_ACCENTS.balance,
      // Bu YANGI pul emas — oldin to'langan puldan oy yopilgani.
      sub: 'yangi tushum emas',
    },
    {
      key: 'waived',
      label: 'Kechirilgan',
      value: formatMoney(kpi.waived),
      accent: KPI_ACCENTS.waived,
      sub: '',
    },
  ]
})
</script>

<template>
  <section>
    <PageHeader
      title="Moliya"
      subtitle="Tushum, qarz va chegirmalar bo‘yicha umumiy manzara"
    >
      <template #actions>
        <!--
          ★ NEGA QO'SHIMCHA O'RAM VA KENGLIK CHEGARASI.

          `PageHeader` amallar konteynerini `shrink-0` bilan chizadi: u
          qatorga sig'masa yangi qatorga o'tadi, lekin SIQILMAYDI. Ikkita
          sana (2 × 152px) + «Excel» ≈ 400px bo'lib, 320–390px telefonda
          sahifa ichidagi joydan (288–358px) keng — natijada BUTUN sahifa
          yon skrollga tushardi. `PageHeader` umumiy komponent, unga
          tegilmadi; chegara shu yerda qo'yiladi.

          ★ `100vw − 3rem`: 2rem — `AppShell` `main` paddingi (`px-4`),
          qolgan 1rem — desktop brauzerni tor qilib qo'yganda paydo
          bo'ladigan klassik skroll paneli uchun zaxira (`100vw` uni
          hisobga olmaydi). Telefonda skroll paneli ustma-ust chizilgani
          uchun bu 16px shunchaki bo'sh joy bo'lib qoladi.

          ★ 560px (`xs`) dan yuqorida chegara BUTUNLAY olib tashlanadi va
          maydonlar avvalgi 9.5rem kengligiga qaytadi — desktop va planshet
          ko'rinishi eski dizayndagidek qoladi.
        -->
        <div class="flex max-w-[calc(100vw-3rem)] flex-wrap items-center gap-2 xs:max-w-none">
          <input
            v-model="from"
            class="zn-input w-[calc(50%-0.25rem)] xs:w-[9.5rem]"
            type="date"
            aria-label="Davr boshi"
          >
          <input
            v-model="to"
            class="zn-input w-[calc(50%-0.25rem)] xs:w-[9.5rem]"
            type="date"
            aria-label="Davr oxiri"
          >
          <BaseButton
            variant="ghost"
            :loading="exportMutation.isPending.value"
            @click="exportMutation.mutate()"
          >
            <template #icon>
              <AppIcon
                name="download"
                :size="15"
              />
            </template>
            Excel
          </BaseButton>
        </div>
      </template>
    </PageHeader>

    <p
      v-if="datesIncomplete"
      class="mb-3.5 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3.5 py-2.5 text-xs text-amber-200"
    >
      Sana to‘liq kiritilmagan — hisobot standart davrda (joriy oy boshi —
      bugun) ko‘rsatilyapti.
    </p>

    <p
      v-if="exportError !== null"
      class="mb-3.5 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3.5 py-2.5 text-xs text-rose-200"
      role="alert"
      v-text="exportError"
    />

    <DataStatus
      :pending="summaryQuery.isPending.value"
      :error="errorMessage"
      :empty="summary === null"
      :retrying="summaryQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="chart"
      empty-title="Ma’lumot yo‘q."
      empty-text="Tanlangan davr uchun moliya ma’lumoti topilmadi."
      @retry="retry"
    >
      <div v-if="summary !== null">
        <!-- ------------------------------------------ asosiy raqamlar -->
        <div class="grid grid-cols-[repeat(auto-fit,minmax(170px,1fr))] gap-3.5">
          <FinanceKpiTile
            v-for="tile in mainTiles"
            :key="tile.key"
            :label="tile.label"
            :value="tile.value"
            :accent="tile.accent"
            :sub="tile.sub"
          />
        </div>

        <!--
          ★ MA'NO FARQI EKRANDA. Buni yozmasak, davr o'zgarganda qarz
          o'zgarmasligi bug deb tushunilardi va "Yig'ilgan" bilan
          "Kassaga tushgan" bir xil raqam deb o'qilardi.
        -->
        <div
          class="mt-3.5 rounded-xl border border-line bg-ink-900 px-4 py-3 text-[11.5px] leading-relaxed text-muted"
        >
          <p>
            <b class="font-semibold text-slate-300">Hisob ({{ periodsLabel }}):</b>
            Rejadagi tushum, Yig‘ilgan, Yig‘ilish foizi, Chegirmalar va
            «Guruhlar bo‘yicha» — shu oylarga yozilgan summalar.
          </p>
          <p class="mt-1">
            <b class="font-semibold text-slate-300">Bugungi holat ({{ asOfLabel }}):</b>
            Umumiy qarz, Balansdagi pul va «Qarz yoshi» — davr filtriga
            bog‘liq emas, sana o‘zgartirilsa ham o‘zgarmaydi.
          </p>
          <p class="mt-1">
            <b class="font-semibold text-slate-300">Kassa ({{ rangeLabel }}):</b>
            quyidagi blok va «To‘lov usuli bo‘yicha» — shu kunlarda kassaga
            haqiqatan tushgan pul.
          </p>
        </div>

        <!-- ---------------------------------------------- kassa jurnali -->
        <h2 class="mt-6 text-[15px] font-semibold sm:text-base">
          Kassa jurnali
        </h2>
        <p
          class="mb-3.5 mt-0.5 text-xs text-muted"
          v-text="`${rangeLabel} kunlarida kassaga tushgan pul`"
        />
        <div class="grid grid-cols-[repeat(auto-fit,minmax(170px,1fr))] gap-3.5">
          <FinanceKpiTile
            v-for="tile in journalTiles"
            :key="tile.key"
            :label="tile.label"
            :value="tile.value"
            :accent="tile.accent"
            :sub="tile.sub"
          />
        </div>

        <!-- --------------------------------------------------- qarz yoshi -->
        <FinanceAgingCard
          class="mt-4"
          :buckets="summary.aging"
        />

        <!-- ------------------------------------------------ oxirgi 12 oy -->
        <FinanceTrendCard
          class="mt-4"
          :months="summary.months"
        />

        <!-- ---------------------------------------------------- kesimlar -->
        <!--
          ★ `min(100%,340px)` — `minmax(340px,1fr)` EMAS. `auto-fit` da
          minimal yo'lak QAT'IY o'lcham: konteyner undan tor bo'lsa setka
          KENGAYIB chiqadi va butun sahifa yon skrollga tushardi. 320px
          ekranda sahifa ichi atigi 288px (`AppShell` `px-4`), 360px da
          328px — ikkalasi ham 340px dan kichik, ya'ni eng keng tarqalgan
          telefonlarda buzilardi. `min(100%,…)` yo'lakni konteynerdan
          oshirmaydi, kengroq ekranda esa AVVALGIDEK 340px da ikkiga
          bo'linadi — desktop ko'rinishi o'zgarmaydi.
        -->
        <div class="mt-4 grid grid-cols-[repeat(auto-fit,minmax(min(100%,340px),1fr))] gap-4">
          <FinanceGroupsCard
            :groups="summary.groups"
            :periods="periodsLabel"
          />
          <FinanceMethodsCard
            :methods="summary.methods"
            :range="rangeLabel"
          />
        </div>
      </div>
    </DataStatus>
  </section>
</template>
