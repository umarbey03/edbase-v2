<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { reviewVerdictLabel, reviewVerdictTone } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { SessionReviewDto, SessionReviewVerdictName } from '@/shared/types'
import { BaseBadge, BaseButton, BaseField, BaseModal, BaseSpinner } from '@/shared/ui'

import { deleteSessionReview, fetchSessionReview, saveSessionReview } from '../api/session-review-api'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS SIFATI TAHLILI — MODAL OYNA (talablar R29 va R30)
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
 * ★ SHAKLI `RecordingPlayerModal` DAN: `props` + `@close`, ochilganda
 * DANGASA (lazy) yuklash. `sessionId: null` — oyna yopiq. Sabab
 * o'shanikidek: ro'yxatdagi har qator uchun oldindan so'rov yuborish 30
 * ta ortiqcha chaqiruv bo'lardi.
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
    /** `null` — oyna yopiq. Ochilganda dars id'si beriladi. */
    sessionId: number | null
    /** Sarlavhada ko'rinadigan dars nomi (bo'sh bo'lsa umumiy matn). */
    title?: string
  }>(),
  { title: '' },
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
 * Server chegarasi (`SessionReview.MaxBodyLength`).
 *
 * ⚠️ Bu yerda TAKRORLANADI, chunki server uni javobda bermaydi (sozlama
 * emas, domain doimiysi). Oshib ketsa server `409` beradi va matn
 * ko'rsatiladi — ya'ni mijozdagi chegara faqat QULAYLIK: foydalanuvchi
 * 4000 belgi yozib bo'lgach xato olishi kerak emas.
 */
const BODY_MAX = 4000

const review = ref<SessionReviewDto | null>(null)
const pending = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

/** Tahrirlash rejimi (faqat o'quv bo'limi ochadi). */
const editing = ref(false)
const draftVerdict = ref<SessionReviewVerdictName>('NotReviewed')
const draftBody = ref('')

const canEdit = computed(() => review.value?.canEdit ?? false)
const bodyTooLong = computed(() => draftBody.value.trim().length > BODY_MAX)

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
  draftBody.value = review.value?.body ?? ''
  editing.value = true
}

async function save(): Promise<void> {
  const sessionId = props.sessionId
  if (sessionId === null || bodyTooLong.value) return

  saving.value = true
  error.value = null
  try {
    review.value = await saveSessionReview(sessionId, {
      verdict: draftVerdict.value,
      body: draftBody.value.trim(),
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
  DANGASA YUKLASH: so'rov faqat oyna OCHILGANDA ketadi
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
      draftBody.value = ''
      return
    }
    void load(id)
  },
)
</script>

<template>
  <BaseModal
    :open="props.sessionId !== null"
    :title="props.title.length > 0 ? `Dars tahlili — ${props.title}` : 'Dars tahlili'"
    wide
    @close="emit('close')"
  >
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

      <div class="mt-3">
        <BaseField
          label="Tahlil matni"
          :error="bodyTooLong ? `Tahlil ${BODY_MAX} belgidan oshmasin.` : null"
          :hint="`${draftBody.length} / ${BODY_MAX}`"
        >
          <textarea
            v-model="draftBody"
            class="zn-input"
            rows="8"
            :maxlength="BODY_MAX"
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
      </div>

      <!--
        `whitespace-pre-line` — matn xodim yozgan qatorlar bilan saqlanadi
        (ro'yxat, bosqichlar). Usiz butun tahlil bitta bo'g'ma abzatsga
        aylanardi.
      -->
      <p
        class="mt-3 whitespace-pre-line text-sm leading-relaxed text-slate-200"
        v-text="review.body"
      />
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
            :disabled="bodyTooLong"
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
  </BaseModal>
</template>
