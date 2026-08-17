<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'

import { fetchAnalysisCriteria } from '@/entities/analysis-criterion'
import { reviewVerdictLabel, reviewVerdictTone } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatWeekdayDateTime } from '@/shared/lib/datetime'
import type { AnalysisCriterionDto, SessionReviewDto, SessionReviewVerdictName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseDrawer, BaseField, BaseModal, BaseSpinner } from '@/shared/ui'

import { deleteSessionReview, fetchSessionReview, saveSessionReview } from '../api/session-review-api'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS SIFATI TAHLILI — O'NGDAN CHIQUVCHI PANEL (talablar R29 va R30)
 * ════════════════════════════════════════════════════════════════════════
 *
 * R29 (o'quv bo'limi): *"video recording yozuvlar bo'limidagi videolarda
 * o'quv bo'limi sifat nazorati tahlili xulosalari ham bo'lsin, uni
 * bosganda modal window orqali ochilsin"*.
 * R30 (ustoz): *"darslarim bo'limida qo'shimcha button orqali teacher
 * o'zining dars tahlilini ko'ra olsin modal window orqali"*.
 *
 * ★ BITTA KOMPONENT, IKKI SAHIFA. Talablar ikkita, lekin ma'lumot bitta va
 * oyna ham bitta bo'lishi kerak: ikkita komponent bo'lsa, ustoz ko'radigan
 * matn bilan o'quv bo'limi yozadigan matn vaqt o'tib boshqacha
 * ko'rinardi. Farq FAQAT `canEdit` da va u SERVERDAN keladi.
 *
 * ★ `BaseModal` EMAS, `BaseDrawer` (loyiha egasi, 2026-08-15): *"tahlil
 * qilish modali o'ng tarafdan ekranni 85% qismini egallab turishi
 * kerak"* — `BaseDrawer` aynan shu talab uchun qurilgan komponent
 * (`StudentProfileDrawer`/`LessonEditDrawer` bilan BIR XIL naqsh).
 *
 * ★ MATN UCH BO'LIMGA BO'LINGAN (loyiha egasi): "Ijobiy tomonlar" va
 * "Kamchiliklar" — IKKI ALOHIDA kartochka, YONMA-YON; ostida "Xulosa va
 * yechimlar". Bu eski yagona `Body` maydonini ALMASHTIRDI (backend:
 * `SessionReview.Plus`/`Minus`/`Conclusion`) — Ijobiy/Kamchilik ixtiyoriy,
 * Xulosa esa MAJBURIY (eski `Body`ning to'g'ridan-to'g'ri vorisi).
 *
 * ════════════════════════════════════════════════════════════════════════
 * 🔴 BU OYNA O'QUVCHIGA HECH QACHON KO'RSATILMAYDI — VA BU YERDA EMAS,
 *    SERVERDA HAL QILINADI.
 *
 * Matn ustoz haqidagi ichki baho ("tushuntirish sust", "vaqtni noto'g'ri
 * taqsimlagan"). O'quvchi endpointga to'g'ridan-to'g'ri kelsa ham `403`
 * oladi va javob tanasida tahlil matnining ZARRASI ham bo'lmaydi
 * (`SessionReviewService` ning birinchi qatori). Bu komponentni o'quvchi
 * ekranida ishlatmang, lekin ishlatilib qolsa ham hech narsa oshkor
 * bo'lmaydi — himoya ikki qatlamli.
 * ════════════════════════════════════════════════════════════════════════
 */
const props = withDefaults(
  defineProps<{
    /** `null` — panel yopiq. Ochilganda dars id'si beriladi. */
    sessionId: number | null
    /** Sarlavhada ko'rinadigan dars nomi (bo'sh bo'lsa umumiy matn). */
    title?: string
    /**
     * Guruh nomi va jadval vaqti — CHAQIRUVCHIdan (ro'yxat qatoridan
     * allaqachon bor), server javobini KUTMASDAN ko'rsatiladi. Loyiha
     * egasi: *"tahlilda sana... guruh nomi... bo'lishi kerak"*.
     *
     * ★ NIMA UCHUN PROP, DTO'DAN EMAS: tahlil HALI YOZILMAGAN bo'lsa
     * (`review === null`) serverdan hech qanday DTO kelmaydi — kontekst
     * o'shanda ham ko'rinishi kerak (aks holda bo'sh holatda "qaysi
     * dars?" degan savol javobsiz qolardi). Ustoz ismi esa (`teacherName`)
     * FAQAT tahlil DTO'sida keladi — chaqiruvchida bunday ma'lumot yo'q.
     */
    groupName?: string
    /** ISO sana-vaqt — dars jadval bo'yicha qachon boshlangan. */
    scheduledStart?: string
    /**
     * `BaseModal` sifatida ochiladi, `BaseDrawer` EMAS (2026-08-17).
     *
     * ★ NIMA UCHUN KERAK: `TeacherReviewsDrawer` — bu komponentning O'ZI
     * `BaseDrawer`. Ichma-ich drawer TAQIQLANGAN (`BaseDrawer.vue`
     * izohi, `useModalHost` dev'da ogohlantiradi) — "Tahlillar" jadvalidagi
     * "Ko'rish" tugmasi bosilganda shu bayroq bilan `BaseModal` ochiladi.
     * Boshqa barcha chaqiruv joylari (`RecordingCard`, ustozning
     * "Darslarim" ro'yxati) hech qachon drawer ICHIDA emas, shuning uchun
     * ular uchun standart (`false`) o'zgarmaydi.
     */
    asModal?: boolean
  }>(),
  { title: '', groupName: '', scheduledStart: '', asModal: false },
)

const emit = defineEmits<{
  close: []
  /**
   * Tahlil o'zgardi (yozildi yoki o'chirildi) — ro'yxat nishonini
   * yangilash uchun. Ota komponent so'rovni QAYTA yuboradi: nishon
   * server ma'lumotidan chizilishi kerak, mahalliy taxmindan emas.
   */
  saved: []
}>()

const VERDICTS: readonly SessionReviewVerdictName[] = ['NotReviewed', 'Approved', 'HasIssue']

/**
 * Server chegarasi (`SessionReview.MaxSectionLength`) — har bir bo'lim
 * ("Ijobiy", "Kamchilik", "Xulosa") uchun ALOHIDA-ALOHIDA.
 *
 * ⚠️ Bu yerda TAKRORLANADI, chunki server uni javobda bermaydi (sozlama
 * emas, domain doimiysi). Oshib ketsa server `409` beradi va matn
 * ko'rsatiladi — ya'ni mijozdagi chegara faqat QULAYLIK.
 */
const SECTION_MAX = 2000

const review = ref<SessionReviewDto | null>(null)
const pending = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

/** Tahrirlash rejimi (faqat o'quv bo'limi ochadi). */
const editing = ref(false)
const draftVerdict = ref<SessionReviewVerdictName>('NotReviewed')
const draftPlus = ref('')
const draftMinus = ref('')
const draftConclusion = ref('')

/**
 * MEZON ASOSIDAGI BALLASH (R29/R30 kengaytmasi) — erkin matn ustiga
 * QO'SHILADI, uni ALMASHTIRMAYDI. Shuning uchun `criteria` bo'sh bo'lishi
 * ("hali mezon sozlanmagan") ham NORMAL holat — forma o'shanda faqat
 * erkin matn bilan qoladi (`renderScores`dagi bo'sh holat bilan AYNI).
 */
const criteria = ref<AnalysisCriterionDto[]>([])
const criteriaPending = ref(false)
/** Mezon Id -> ball. Tahrirlash boshlanganda to'ldiriladi (`loadCriteria`). */
const draftScores = reactive<Record<number, number>>({})

const scoreSum = computed(() =>
  criteria.value.reduce((sum, c) => sum + clampScore(draftScores[c.id], c.maxScore), 0),
)
const scoreMax = computed(() => criteria.value.reduce((sum, c) => sum + c.maxScore, 0))

function clampScore(value: number | undefined, max: number): number {
  const v = value ?? 0
  if (Number.isNaN(v)) return 0
  return Math.min(Math.max(v, 0), max)
}

async function loadCriteria(): Promise<void> {
  criteriaPending.value = true
  try {
    criteria.value = await fetchAnalysisCriteria()
  } catch {
    // Mezon ro'yxati ochilmasa ham forma erkin matn bilan ishlayveradi —
    // ballash IXTIYORIY qism, majburiy emas.
    criteria.value = []
  } finally {
    criteriaPending.value = false
  }
}

const canEdit = computed(() => review.value?.canEdit ?? false)

const plusTooLong = computed(() => draftPlus.value.trim().length > SECTION_MAX)
const minusTooLong = computed(() => draftMinus.value.trim().length > SECTION_MAX)
const conclusionTooLong = computed(() => draftConclusion.value.trim().length > SECTION_MAX)
const conclusionEmpty = computed(() => draftConclusion.value.trim().length === 0)

const canSave = computed(
  () => !plusTooLong.value && !minusTooLong.value && !conclusionTooLong.value && !conclusionEmpty.value,
)

/**
 * Tahrirlash tugmasi umuman ko'rinadimi.
 *
 * ⚠️ Tahlil YO'Q bo'lsa `review` ham `null`, ya'ni `canEdit` ni undan
 * o'qib bo'lmaydi. Shuning uchun bo'sh holatda tugma DOIM ko'rsatiladi va
 * ruxsatni SERVER hal qiladi (`403` bo'lsa matn chiqadi). Bu ONGLI:
 * "yozish mumkinmi" ni bilish uchun qo'shimcha endpoint ochish bir
 * bayroq uchun juda qimmat bo'lardi.
 */
const canStartWriting = computed(() => review.value === null || canEdit.value)

async function load(sessionId: number): Promise<void> {
  pending.value = true
  error.value = null
  editing.value = false
  try {
    review.value = await fetchSessionReview(sessionId)
  } catch (cause) {
    /*
      `toUserMessage` — yagona manba:
        • 403 — o'quvchi yoki begona guruhning darsi (server sababni yozadi);
        • 404 — dars o'chirilgan.
      Matnni o'zimiz yig'sak, serverning aniq maslahati yo'qolardi.
    */
    error.value = toUserMessage(cause)
    review.value = null
  } finally {
    pending.value = false
  }
}

function startEditing(): void {
  draftVerdict.value = review.value?.verdict ?? 'NotReviewed'
  draftPlus.value = review.value?.plus ?? ''
  draftMinus.value = review.value?.minus ?? ''
  draftConclusion.value = review.value?.conclusion ?? ''
  editing.value = true

  // Avvalgi ballar (bo'lsa) mezon Id'si bo'yicha tiklanadi; qolganlari 0
  // dan boshlanadi — mockup'dagi `renderScores` bilan AYNI standart.
  for (const key of Object.keys(draftScores)) delete draftScores[Number(key)]
  for (const score of review.value?.scores ?? []) {
    if (score.criterionId !== null) draftScores[score.criterionId] = score.score
  }

  void loadCriteria()
}

async function save(): Promise<void> {
  const sessionId = props.sessionId
  if (sessionId === null || !canSave.value) return

  saving.value = true
  error.value = null
  try {
    review.value = await saveSessionReview(sessionId, {
      verdict: draftVerdict.value,
      conclusion: draftConclusion.value.trim(),
      plus: draftPlus.value.trim().length > 0 ? draftPlus.value.trim() : null,
      minus: draftMinus.value.trim().length > 0 ? draftMinus.value.trim() : null,
      scores: criteria.value.map((c) => ({
        criterionId: c.id,
        score: clampScore(draftScores[c.id], c.maxScore),
      })),
    })
    editing.value = false
    emit('saved')
  } catch (cause) {
    // 409 — bo'sh yoki uzun matn (domain qoidasi); 403 — ustoz yozmoqchi.
    error.value = toUserMessage(cause)
  } finally {
    saving.value = false
  }
}

async function remove(): Promise<void> {
  const sessionId = props.sessionId
  if (sessionId === null) return

  saving.value = true
  error.value = null
  try {
    await deleteSessionReview(sessionId)
    review.value = null
    editing.value = false
    emit('saved')
  } catch (cause) {
    error.value = toUserMessage(cause)
  } finally {
    saving.value = false
  }
}

/*
  DANGASA YUKLASH: so'rov faqat panel OCHILGANDA ketadi
  (`RecordingPlayerModal` bilan AYNI naqsh). Yopilganda holat tozalanadi —
  aks holda keyingi dars ochilganda bir zum ESKI tahlil ko'rinib qolardi
  va bu eng chalg'ituvchi xato bo'lardi (ustoz begona bahoni o'ziniki deb
  o'qib qolishi mumkin).
*/
watch(
  () => props.sessionId,
  (id) => {
    if (id === null) {
      review.value = null
      error.value = null
      editing.value = false
      draftPlus.value = ''
      draftMinus.value = ''
      draftConclusion.value = ''
      return
    }
    void load(id)
  },
)
</script>

<template>
  <component
    :is="props.asModal ? BaseModal : BaseDrawer"
    :open="props.sessionId !== null"
    :title="props.title.length > 0 ? `Dars tahlili — ${props.title}` : 'Dars tahlili'"
    v-bind="props.asModal ? { wide: true } : {}"
    @close="emit('close')"
  >
    <!--
      ★ KONTEKST QATORI — sana/guruh CHAQIRUVCHIDAN (prop, doim bor),
      ustoz ismi esa `review`dan (faqat tahlil yuklangach). Loyiha egasi:
      "tahlilda sana, ustoz nomi, guruh nomi, dars nomi... bo'lishi kerak"
      — dars nomi allaqachon panel SARLAVHASIDA (yuqorida).
    -->
    <div
      v-if="props.groupName.length > 0 || props.scheduledStart.length > 0 || review?.teacherName"
      class="mb-4 flex flex-wrap items-center gap-x-4 gap-y-1.5 rounded-xl border border-line bg-ink-950 px-3.5 py-2.5 text-xs text-slate-300"
    >
      <span
        v-if="props.scheduledStart.length > 0"
        class="inline-flex items-center gap-1.5"
      >
        <AppIcon
          name="calendar"
          :size="13"
        />
        {{ formatWeekdayDateTime(props.scheduledStart) }}
      </span>
      <span
        v-if="props.groupName.length > 0"
        class="inline-flex items-center gap-1.5"
      >
        <AppIcon
          name="users"
          :size="13"
        />
        {{ props.groupName }}
      </span>
      <span
        v-if="review?.teacherName"
        class="inline-flex items-center gap-1.5"
      >
        <AppIcon
          name="user"
          :size="13"
        />
        {{ review.teacherName }}
      </span>
    </div>

    <div
      v-if="pending"
      class="flex h-32 items-center justify-center"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="error !== null && !editing"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center text-sm text-rose-200"
      role="alert"
      v-text="error"
    />

    <!-- ------------------------------------------------------ tahrirlash -->
    <form
      v-else-if="editing"
      novalidate
      @submit.prevent="save"
    >
      <BaseField
        label="Xulosa"
        hint="«Ko‘rilmagan» — qoralama: matn saqlanadi, lekin yakuniy xulosa qo‘yilmaydi."
      >
        <select
          v-model="draftVerdict"
          class="zn-input"
        >
          <option
            v-for="verdict in VERDICTS"
            :key="verdict"
            :value="verdict"
          >
            {{ reviewVerdictLabel(verdict) }}
          </option>
        </select>
      </BaseField>

      <!-- ---------------------------------------- mezon asosidagi ballash -->
      <div
        v-if="criteriaPending"
        class="mt-3 flex justify-center py-4"
      >
        <BaseSpinner />
      </div>

      <div
        v-else-if="criteria.length > 0"
        class="mt-3 rounded-xl border border-line bg-ink-950 p-3.5"
      >
        <div class="mb-2.5 flex items-center justify-between">
          <span class="text-xs font-semibold text-slate-300">Mezon bo‘yicha baholash</span>
          <span
            class="rounded-lg bg-ink-800 px-2 py-1 text-xs font-bold text-brand-300"
            v-text="`${scoreSum} / ${scoreMax}`"
          />
        </div>
        <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <div
            v-for="c in criteria"
            :key="c.id"
            class="flex items-center justify-between gap-2 rounded-lg border border-line bg-ink-900 px-3 py-1.5"
          >
            <div class="min-w-0 pr-2">
              <p
                class="truncate text-xs font-semibold text-slate-200"
                :title="c.name"
                v-text="c.name"
              />
              <p
                class="text-[10px] text-slate-500"
                v-text="`maks ${c.maxScore}`"
              />
            </div>
            <input
              type="number"
              min="0"
              :max="c.maxScore"
              class="w-16 shrink-0 rounded-md border border-line bg-ink-950 px-1.5 py-1 text-center text-xs font-bold text-slate-100 focus:ring-1 focus:ring-brand-500"
              :value="draftScores[c.id] ?? 0"
              @input="draftScores[c.id] = clampScore(Number(($event.target as HTMLInputElement).value), c.maxScore)"
            >
          </div>
        </div>
      </div>

      <!-- ------------------------------------- ijobiy / kamchilik, yonma-yon -->
      <div class="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
        <div class="rounded-2xl border-2 border-emerald-500/25 bg-emerald-500/[0.06] p-3.5">
          <BaseField
            label="Ijobiy tomonlar"
            :error="plusTooLong ? `${SECTION_MAX} belgidan oshmasin.` : null"
            :hint="`${draftPlus.length} / ${SECTION_MAX}`"
          >
            <textarea
              v-model="draftPlus"
              class="zn-input"
              rows="6"
              :maxlength="SECTION_MAX"
              placeholder="Kuchli jihatlar..."
            />
          </BaseField>
        </div>

        <div class="rounded-2xl border-2 border-rose-500/25 bg-rose-500/[0.06] p-3.5">
          <BaseField
            label="Kamchiliklar"
            :error="minusTooLong ? `${SECTION_MAX} belgidan oshmasin.` : null"
            :hint="`${draftMinus.length} / ${SECTION_MAX}`"
          >
            <textarea
              v-model="draftMinus"
              class="zn-input"
              rows="6"
              :maxlength="SECTION_MAX"
              placeholder="Yaxshilash kerak jihatlar..."
            />
          </BaseField>
        </div>
      </div>

      <!-- ------------------------------------------------ xulosa va yechimlar -->
      <div class="mt-3">
        <BaseField
          label="Xulosa va yechimlar"
          :error="conclusionTooLong ? `${SECTION_MAX} belgidan oshmasin.` : null"
          :hint="`${draftConclusion.length} / ${SECTION_MAX}`"
        >
          <textarea
            v-model="draftConclusion"
            class="zn-input"
            rows="6"
            :maxlength="SECTION_MAX"
            placeholder="Masalan: kirish qismi cho‘zildi, amaliy topshiriqqa vaqt qolmadi..."
          />
        </BaseField>
      </div>

      <p
        v-if="error !== null"
        class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
        role="alert"
        v-text="error"
      />
    </form>

    <!-- ------------------------------------------------------ o'qish -->
    <div v-else-if="review !== null">
      <div class="flex flex-wrap items-center gap-2">
        <BaseBadge :tone="reviewVerdictTone(review.verdict)">
          {{ reviewVerdictLabel(review.verdict) }}
        </BaseBadge>
        <!--
          ★ MUALLIF ISMI — BEZAK EMAS: ustoz o'zi haqidagi bahoni
          o'qiyotganda "kim aytdi" savoliga javob bo'lmasa, tushuntirish
          so'rash yoki e'tiroz bildirish yo'li yopiq bo'lardi.
        -->
        <span
          class="text-xs text-slate-400"
          v-text="`${review.authorName} · ${formatDateTime(review.updatedAt ?? review.createdAt)}`"
        />
        <BaseBadge
          v-if="review.scorePercent !== null"
          :tone="review.scorePercent >= 70 ? 'success' : review.scorePercent >= 40 ? 'warning' : 'danger'"
        >
          {{ `${review.totalScore} / ${review.totalMaxScore} (${review.scorePercent}%)` }}
        </BaseBadge>
      </div>

      <!-- ---------------------------------------- mezon ballari (o'qish) -->
      <div
        v-if="review.scores.length > 0"
        class="mt-3 grid grid-cols-1 gap-2 rounded-xl border border-line bg-ink-950 p-3.5 sm:grid-cols-2"
      >
        <div
          v-for="(s, i) in review.scores"
          :key="`${s.criterionId ?? 'x'}-${i}`"
          class="flex items-center justify-between rounded-lg border border-line bg-ink-900 px-3 py-1.5"
        >
          <span
            class="truncate text-xs text-slate-300"
            :title="s.criterionName"
            v-text="s.criterionName"
          />
          <span
            class="shrink-0 text-xs font-bold text-slate-100"
            v-text="`${s.score} / ${s.maxScore}`"
          />
        </div>
      </div>

      <!-- ------------------------------------- ijobiy / kamchilik, yonma-yon -->
      <div
        v-if="(review.plus?.length ?? 0) > 0 || (review.minus?.length ?? 0) > 0"
        class="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2"
      >
        <div
          v-if="(review.plus?.length ?? 0) > 0"
          class="rounded-2xl border-2 border-emerald-500/25 bg-emerald-500/[0.06] p-3.5"
        >
          <h4 class="mb-1.5 text-xs font-bold uppercase tracking-wide text-emerald-300">
            Ijobiy tomonlar
          </h4>
          <p
            class="whitespace-pre-line text-sm leading-relaxed text-slate-200"
            v-text="review.plus"
          />
        </div>

        <div
          v-if="(review.minus?.length ?? 0) > 0"
          class="rounded-2xl border-2 border-rose-500/25 bg-rose-500/[0.06] p-3.5"
        >
          <h4 class="mb-1.5 text-xs font-bold uppercase tracking-wide text-rose-300">
            Kamchiliklar
          </h4>
          <p
            class="whitespace-pre-line text-sm leading-relaxed text-slate-200"
            v-text="review.minus"
          />
        </div>
      </div>

      <!--
        ★ XULOSA HAMISHA KO'RINADI (MAJBURIY maydon, `whitespace-pre-line`
        — matn xodim yozgan qatorlar bilan saqlanadi).
      -->
      <div class="mt-3 rounded-2xl border border-line bg-ink-950 p-3.5">
        <h4 class="mb-1.5 text-xs font-bold uppercase tracking-wide text-slate-400">
          Xulosa va yechimlar
        </h4>
        <p
          class="whitespace-pre-line text-sm leading-relaxed text-slate-200"
          v-text="review.conclusion"
        />
      </div>
    </div>

    <!-- ------------------------------------------------------ bo'sh holat -->
    <p
      v-else
      class="rounded-xl border border-line bg-ink-950 px-5 py-8 text-center text-sm text-slate-400"
    >
      Bu dars uchun tahlil hali yozilmagan.
    </p>

    <template #footer>
      <div class="flex flex-1 flex-wrap items-center gap-2">
        <template v-if="editing">
          <BaseButton
            size="sm"
            :loading="saving"
            :disabled="!canSave"
            @click="save"
          >
            Saqlash
          </BaseButton>
          <BaseButton
            size="sm"
            variant="ghost"
            @click="editing = false"
          >
            Bekor qilish
          </BaseButton>
        </template>

        <template v-else-if="!pending && error === null">
          <BaseButton
            v-if="canStartWriting"
            size="sm"
            @click="startEditing"
          >
            {{ review === null ? 'Tahlil yozish' : 'Tahrirlash' }}
          </BaseButton>
          <BaseButton
            v-if="review !== null && canEdit"
            size="sm"
            variant="ghost"
            :loading="saving"
            @click="remove"
          >
            O‘chirish
          </BaseButton>
        </template>
      </div>

      <BaseButton
        size="sm"
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </component>
</template>
