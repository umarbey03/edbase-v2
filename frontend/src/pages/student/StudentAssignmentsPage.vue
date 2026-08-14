<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  answerFormatsLabel,
  assignmentState,
  assignmentTitle,
  fetchMyAssignments,
} from '@/entities/assignment'
import SubmissionFeedbackFiles from '@/entities/assignment/ui/SubmissionFeedbackFiles.vue'
import SubmitAssignmentDialog from '@/features/assignment-submit/ui/SubmitAssignmentDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { StudentAssignmentDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseModal, DataStatus } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * O'quvchining vazifalari.
 *
 * MUHIM: qulflangan dars vazifasi ham RO'YXATDA QOLADI — o'quvchi nima
 * kutayotganini bilishi kerak. Topshirib bo'lmasligining SABABI har
 * kartochkada matn bilan yoziladi.
 *
 * "Topshirish" tugmasi SERVER qaroriga tayanadi (`canSubmit` -> `blockedReason`)
 * — sahifa gating va qayta topshirish qoidalarini o'zicha hisoblamaydi.
 */
const queryClient = useQueryClient()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'mine'],
  queryFn: ({ signal }) => fetchMyAssignments({ signal }),
})

/** Holat bir marta hisoblanadi — shablonda `assignmentState()` takror chaqirilmasin. */
const rows = computed(() =>
  (assignmentsQuery.data.value ?? []).map((item) => ({
    item,
    state: assignmentState(item),
    formats: answerFormatsLabel(item.allowedFormats),
    feedback: item.mySubmission?.feedback ?? null,
    fileCount: item.mySubmission?.files?.length ?? 0,
    /**
     * R37 · USTOZ tekshirishda biriktirgan fayllar.
     *
     * ★ SHU YERDA `?? []` bir marta bajariladi: shablonda har kartochka
     * uchun null-tekshiruv yozilmasin.
     */
    feedbackFiles: item.mySubmission?.feedbackFiles ?? [],
  })),
)

/**
 * Kattalashtirilgan rasm (R37: *"tekshirishda rasmni katta ekranda ko'rish
 * mumkin bo'lsin"*).
 *
 * ★ TALAB IKKI TOMONGA TEGISHLI: ustoz o'quvchining rasmini
 * (`GradeDialog`), o'quvchi esa ustoz qo'ygan tuzatish rasmini katta
 * ko'rishi kerak. Ikkinchisi ilgari UMUMAN yo'q edi.
 */
const zoomUrl = ref<string | null>(null)

const errorMessage = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

const submitting = ref<StudentAssignmentDto | null>(null)

function handleSubmitted(): void {
  // Javob topshirilishi gating'ni ham o'zgartiradi (keyingi dars ochilishi
  // mumkin) — shuning uchun butun ro'yxat qayta so'raladi.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}
</script>

<template>
  <!--
    ★ `@container` SAHIFA ILDIZIDA (ilgari ro'yxat `<div>` ida edi).

    Sabab TEXNIK: element O'ZINI so'rovga sola olmaydi — `@container` va
    `@sm:`/`@2xl:` bitta tugunda tursa, so'rov YUQORIDAGI konteynerga
    murojaat qiladi, u esa yo'q edi. Ro'yxat `<div>` i ildiz bilan bir xil
    kenglikda (ikkalasi ham `<main>` ustunini to'ldiradi), shuning uchun
    o'lchov qiymati o'zgarmaydi — faqat endi setka ham, kartochka ichki
    bo'shlig'i ham SHU konteynerdan o'qiydi.
  -->
  <div class="@container">
    <!--
      `PageHeader` o'rniga `StudentSubHeader`: bu sahifa endi "O'quv" tabining
      ichida yashaydi va o'quvchiga u yerga qaytish yo'li ko'rinib turishi
      kerak (Mini App karkasida "orqaga" tugmasi yo'q).
    -->
    <StudentSubHeader
      title="Vazifalarim"
      subtitle="Topshirish kerak bo‘lgan va baholangan ishlar"
    />

    <DataStatus
      :pending="assignmentsQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="assignmentsQuery.isFetching.value"
      empty-icon="clipboard"
      empty-title="Vazifa yo‘q"
      empty-text="Ustoz vazifa bergach shu yerda ko‘rinadi."
      @retry="assignmentsQuery.refetch()"
    >
      <!--
        ★ `@container` + `@sm:` — ichki bo'shliq EKRANGA emas, USTUN
        kengligiga qarab o'sadi. Ilgari `sm:p-4` edi: u oyna 640px dan
        kengaygandagina yoqilardi, holbuki kartochka 520px lik ustunda
        yashaydi va uning kengligi oynadan mustaqil. Ya'ni "ustun keng
        bo'lsa nafas kengroq" degan niyat oyna kengligiga bog'lab qo'yilgan
        edi — panel desktopda kengaysa ham natija to'g'ri bo'lishi uchun
        o'lchov endi konteynerniki.

        ★ 2026-08-13: `space-y-3` O'RNIGA SETKA. Karkas ustuni 1600px
        bo'lgach bitta ustundagi vazifa kartochkasi ~1536px ga cho'zilardi:
        chap chekkada bitta sarlavha, o'ng chekkada bitta tugma, orada bir
        metr bo'sh joy. Bo'shliqni `gap-3` beradi (avvalgi `space-y-3` bilan
        AYNAN bir xil 12px), ya'ni bitta ustunda ko'rinish o'zgarmaydi.

        Chegaralar kartochkaning matn hajmidan: bu yerda tavsif, ustoz
        izohi va blok sababi bor, ya'ni eng kam qulay kenglik ~320px —
        2 ustun uchun 42rem, 3 ustun uchun 64rem. TO'RTINCHI USTUN
        ATAYLAB YO'Q: 1536px da 3 ustun ~505px beradi va 12px lik izoh
        matni uchun bu qulay o'lchov; 4 ustunda (~375px) uzun izohlar
        kartochkani cho'zib, satrlar notekis bo'lib ketardi.

        ★ Telefon: 42rem = 672px, karkas ustuni esa `lg` gacha 520px bilan
        qulflangan — birorta so'rov yonmaydi, ro'yxat bitta ustun.
      -->
      <div class="grid gap-3 @2xl:grid-cols-2 @5xl:grid-cols-3">
        <!--
          ★ HOVER FAQAT CHEGARADA: kartochkaning o'zi bosilmaydi (harakat
          "Topshirish" tugmasida), fon o'zgarishi esa bosilishga yolg'on
          va'da berardi.
        -->
        <article
          v-for="row in rows"
          :key="row.item.id"
          class="flex flex-col rounded-xl border border-line bg-ink-900 p-3.5 transition-colors hover:border-line-strong @sm:p-4"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <h3
              class="min-w-0 flex-1 text-sm font-semibold text-slate-100"
              v-text="assignmentTitle(row.item.title, row.item.id)"
            />
            <BaseBadge :tone="row.state.tone">
              {{ row.state.label }}
            </BaseBadge>
          </div>

          <p
            v-if="row.item.description !== null && row.item.description.length > 0"
            class="mt-1.5 text-xs text-slate-400"
            v-text="row.item.description"
          />

          <dl class="mt-2.5 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div
              v-if="row.item.moduleLessonName !== null"
              class="inline-flex min-w-0 items-center gap-1.5"
            >
              <AppIcon
                name="file-text"
                :size="13"
              />
              <span
                class="truncate"
                v-text="row.item.moduleLessonName"
              />
            </div>
            <div
              v-if="row.item.groupName !== null"
              class="inline-flex min-w-0 items-center gap-1.5"
            >
              <AppIcon
                name="users"
                :size="13"
              />
              <span
                class="truncate"
                v-text="row.item.groupName"
              />
            </div>
            <div
              v-if="row.item.dueAt !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span
                class="tabular-nums"
                v-text="formatDateTime(row.item.dueAt)"
              />
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="star"
                :size="13"
              />
              <span class="tabular-nums">{{ row.item.maxScore }} ball</span>
            </div>
            <div
              v-if="row.formats.length > 0"
              class="text-dim"
            >
              Javob turi: {{ row.formats }}
            </div>
          </dl>

          <!-- Yuborilgan javobning qisqacha holati. -->
          <p
            v-if="row.item.mySubmission !== null"
            class="mt-2.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400"
          >
            <span class="tabular-nums">
              {{ row.item.mySubmission.attemptNumber }}-urinish ·
              {{ formatDateTime(row.item.mySubmission.submittedAt) }}
            </span>
            <span
              v-if="row.fileCount > 0"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="paperclip"
                :size="13"
              />
              {{ row.fileCount }} ta fayl
            </span>
            <span
              v-if="row.item.mySubmission.isLate"
              class="text-amber-400"
            >kechikkan</span>
          </p>

          <!-- Nega topshira olmaslik sababi — qulflangan darsda ENG muhim ma'lumot. -->
          <p
            v-if="row.state.blockedReason !== null"
            class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
          >
            <AppIcon
              :name="row.item.lessonUnlocked ? 'alert' : 'lock'"
              :size="14"
              class="mt-px"
            />
            <span v-text="row.state.blockedReason" />
          </p>

          <!-- Ustoz qayta topshirishga ruxsat bergan bo'lsa — sababi ko'rsatiladi. -->
          <p
            v-if="(row.item.mySubmission?.resubmitNote ?? '').length > 0"
            class="mt-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
          >
            <span class="font-semibold">Qayta yuborish so‘raldi: </span>
            <span v-text="row.item.mySubmission?.resubmitNote" />
          </p>

          <p
            v-if="row.feedback !== null && row.feedback.length > 0"
            class="mt-2 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-300"
          >
            <span class="font-semibold text-slate-200">Ustoz izohi: </span>
            <span v-text="row.feedback" />
          </p>

          <!--
            ★ R37 · USTOZ BIRIKTIRGAN FAYLLAR (tuzatilgan varaq, namuna
            talaffuz, PDF sharh). O'chirish tugmasi YO'Q — bu ustozning
            sharhi, o'quvchining javobi emas.
          -->
          <div
            v-if="row.feedbackFiles.length > 0"
            class="mt-2"
          >
            <h3 class="mb-1.5 text-[11px] font-bold uppercase tracking-wide text-slate-400">
              Ustoz biriktirgan fayllar
            </h3>
            <SubmissionFeedbackFiles
              :files="row.feedbackFiles"
              @zoom="(url) => (zoomUrl = url)"
            />
          </div>

          <!--
            ★ `flex flex-col` + `@2xl:mt-auto`: ko'p ustunli setkada bir
            satrdagi kartochkalar teng balandlikka cho'ziladi, tugma esa
            matn tugagan joyda "osilib" qolardi — endi u kartochka
            TAGIGA yopishadi va tugmalar qatori bir chiziqda turadi.

            `mt-auto` KONTEYNER SO'ROVI OSTIDA: bitta ustunda kartochka
            balandligi kontent bo'yicha, ya'ni bo'sh joy YO'Q va `mt-auto`
            0 ga aylanib, hozirgi 12px lik oraliqni yeb qo'yardi. Telefon
            yo'lida esa 42rem lik so'rov hech qachon yonmaydi.
          -->
          <div
            v-if="row.state.blockedReason === null"
            class="mt-3 flex justify-end @2xl:mt-auto @2xl:pt-3"
          >
            <!--
              ★ `tap-expand`: `size="sm"` 36px baland, WCAG 2.5.5 esa 44px
              so'raydi. `BaseButton` o'lchov xaritasi butun ilovaniki —
              uni surish har panelda joylashuvni siljitardi. Shuning uchun
              faqat bosiladigan maydon kengaytiriladi (36 + 2×6 = 48px),
              ko'rinish o'zgarmaydi.
            -->
            <BaseButton
              class="tap-expand"
              size="sm"
              @click="submitting = row.item"
            >
              <template #icon>
                <AppIcon
                  name="send"
                  :size="14"
                />
              </template>
              {{ row.item.mySubmission !== null ? 'Qayta yuborish' : 'Topshirish' }}
            </BaseButton>
          </div>
        </article>
      </div>
    </DataStatus>

    <SubmitAssignmentDialog
      :assignment="submitting"
      @close="submitting = null"
      @submitted="handleSubmitted"
    />

    <!--
      Kattalashtirilgan rasm — `GradingQueueOverlay` va `GradeDialog` dagi
      AYNI naqsh (`BaseModal wide` + `max-h-[75dvh] object-contain`).
    -->
    <BaseModal
      :open="zoomUrl !== null"
      title="Rasm"
      wide
      @close="zoomUrl = null"
    >
      <img
        v-if="zoomUrl !== null"
        :src="zoomUrl"
        alt="Kattalashtirilgan rasm"
        class="mx-auto max-h-[75dvh] w-auto rounded-lg object-contain"
      >
    </BaseModal>
  </div>
</template>
