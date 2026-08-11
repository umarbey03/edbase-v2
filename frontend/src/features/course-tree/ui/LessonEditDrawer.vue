<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  createLesson,
  LESSON_KIND_OPTIONS,
  lessonKindLabel,
  updateLesson,
} from '@/entities/course'
import LessonAssignmentSection from '@/features/assignment-form/ui/LessonAssignmentSection.vue'
import { LessonAssetsSection } from '@/features/lesson-media'
import { isApiError, toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type {
  CourseLessonDto,
  LessonAssetDto,
  LessonKindName,
  LessonWriteRequest,
} from '@/shared/types'
import { BaseButton, BaseDrawer, BaseField } from '@/shared/ui'

/**
 * ========================================================================
 * DARS DRAWER'I — yaratish/tahrirlash (o'ngdan 85%)
 * ========================================================================
 *
 * Talab (loyiha egasi): *"dars yaratish yoki edit qilish button bosilganda
 * modal ochilishi kerak (o'ng taraf, ekranni 85% egallashi kerak)"*.
 * Ilgari bu 173 qatorli oddiy `BaseModal` edi (faqat nomi/tavsifi/davomiyligi).
 *
 * TO'RT BO'LIM va HAR BIRI ALOHIDA SAQLANADI (reja C2 qoidasi):
 *   1) dars ma'lumotlari — nomi · tavsifi · davomiyligi;
 *   2) dars turi — `Odatiy` | `Imtihon`;
 *   3) video qismlari (odatiy) / rasmlar (imtihon) — `LessonAssetsSection`;
 *   4) uy vazifasi — `LessonAssignmentSection`.
 *
 * ── 🔴 `PUT` = TO'LIQ ALMASHTIRISH ────────────────────────────────────
 *
 * `UpdateLessonRequest` ning maydonlari serverda standart qiymatga ega
 * (`Kind = Normal`!), ya'ni yuborilmagan maydon JIMGINA almashadi. Shuning
 * uchun HAR SAQLASHDA to'liq shakl yuboriladi: 1-bo'lim "Saqlash" tugmasi
 * ham joriy `kind` ni QAYTARIB yuboradi, aks holda imtihon darsi nomini
 * tahrirlash uni `Normal` ga aylantirib qo'yardi (`DAVOM_ETTIRISH.md`
 * 6-bo'lim, 1-tuzoq). Shu sababli ikkala bo'lim ham BITTA `saveLesson`
 * funksiyasidan o'tadi.
 *
 * ── 3 VA 4-BO'LIM YANGI DARSDA KO'RINMAYDI ────────────────────────────
 *
 * Fayl yuklash uchun `lessonId`, vazifa uchun esa nishon kerak — ikkalasi
 * ham dars YARATILGANDAN keyin paydo bo'ladi. Dars saqlangach drawer
 * YOPILMAYDI: server qaytargan DTO ichki holatga yoziladi va bo'limlar
 * DARHOL ochiladi (aks holda foydalanuvchi darsni yaratib, ro'yxatdan uni
 * yana topib, qaytadan ochishi kerak bo'lardi).
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    courseId: number
    moduleId: number
    /** `null` — yangi dars rejimi. */
    lesson: CourseLessonDto | null
    /** Sarlavha ostida ko'rsatiladigan modul nomi. */
    moduleName?: string
  }>(),
  { moduleName: '' },
)

const emit = defineEmits<{ close: []; saved: [] }>()

const confirm = useConfirm()

/**
 * Server qaytargan OXIRGI holat.
 *
 * Ota komponent `lesson` prop'ida ro'yxatdagi SURATNI uzatadi va u daraxt
 * qayta so'ralgandan keyin ham ALMASHMAYDI (u boshqa ob'ekt). Shuning uchun
 * drawer o'z holatini yuritadi: yaratilgan dars Id'si, tur va fayllar
 * ro'yxati shu yerda yashaydi.
 */
const savedLesson = ref<CourseLessonDto | null>(null)

const current = computed<CourseLessonDto | null>(() => savedLesson.value ?? props.lesson)
const isEdit = computed(() => current.value !== null)

const name = ref('')
const description = ref('')
/** Bo'sh satr = "kiritilmagan" (`null`), 0 EMAS. */
const durationText = ref('')
const kind = ref<LessonKindName>('Normal')
const assets = ref<LessonAssetDto[]>([])

const errorMessage = ref<string | null>(null)
/** Turni almashtirish nima uchun mumkin emasligi (fayllar bor). */
const kindBlocked = ref<string | null>(null)

function resetFrom(lesson: CourseLessonDto | null): void {
  name.value = lesson?.name ?? ''
  description.value = lesson?.description ?? ''
  durationText.value = lesson?.durationMin != null ? String(lesson.durationMin) : ''
  kind.value = lesson?.kind ?? 'Normal'
  assets.value = [...(lesson?.assets ?? [])]
  errorMessage.value = null
  kindBlocked.value = null
}

/*
  Drawer ochilganda (yoki BOSHQA dars uchun qayta ochilganda) holat NOLDAN
  tiklanadi: `savedLesson` ham tozalanadi, aks holda oldingi darsning Id'si
  yangi darsga "yopishib" qolardi.

  🔴 KUZATUV `lesson` OB'EKTIGA EMAS, UNING `id` SIGA BOG'LANGAN. Sabab:
  drawer ochiq turganda kurs daraxti QAYTA SO'RALADI (har fayl yuklanganda
  `saved` emit qilinadi) va `useQuery` YANGI ob'ekt qaytaradi. Ob'ektni
  kuzatsak, o'sha paytda nomni tahrirlab turgan xodimning YOZGANI serverdagi
  eski qiymat bilan almashib ketardi — ya'ni "video yuklandi" hodisasi
  formani tozalab yuborardi. Fayllar ro'yxati esa daraxtdan qayta o'qishga
  muhtoj emas: u har amalning JAVOBIDAN yangilanadi (yuklash 201 + DTO,
  o'chirish 204, tartib 200 + pozitsiyalar).
*/
watch(
  () => [props.open, props.lesson?.id ?? null],
  () => {
    savedLesson.value = null
    resetFrom(props.lesson)
  },
  { immediate: true },
)

/* ==================================================== 1-bo'lim: tekshiruv */

const trimmedName = computed(() => name.value.trim())

/*
  Davomiylik bo'sh bo'lishi MUMKIN, lekin kiritilgan bo'lsa musbat butun son
  bo'lishi kerak — aks holda server 400 qaytaradi va foydalanuvchi nima xato
  bo'lganini formadan tashqarida bilib olardi.
*/
const durationError = computed<string | null>(() => {
  const raw = durationText.value.trim()
  if (raw.length === 0) return null
  const value = Number(raw)
  if (!Number.isInteger(value) || value <= 0) return 'Daqiqa musbat butun son bo‘lishi kerak.'
  return null
})

const nameError = computed<string | null>(() =>
  trimmedName.value.length === 0 ? 'Dars nomi kiritilishi kerak.' : null,
)

const submitted = ref(false)

const canSaveBasics = computed(
  () => nameError.value === null && durationError.value === null,
)

/** Nomi/tavsifi/davomiyligi o'zgarganmi (saqlanmagan ish bormi). */
const basicsDirty = computed(() => {
  const lesson = current.value
  const duration = durationText.value.trim()
  return (
    trimmedName.value !== (lesson?.name ?? '')
    || description.value.trim() !== (lesson?.description ?? '')
    || (duration.length > 0 ? Number(duration) : null) !== (lesson?.durationMin ?? null)
  )
})

/** Tasdiq oynasi uchun "nima o'zgardi" ro'yxati. */
function basicsChanges(): string[] {
  const lesson = current.value
  const changes: string[] = []
  const duration = durationText.value.trim()

  if (trimmedName.value !== (lesson?.name ?? '')) changes.push('Dars nomi')
  if (description.value.trim() !== (lesson?.description ?? '')) changes.push('Tavsif')
  if ((duration.length > 0 ? Number(duration) : null) !== (lesson?.durationMin ?? null)) {
    changes.push('Davomiylik')
  }
  return changes
}

/* ======================================================== saqlash (umumiy) */

/**
 * So'rov tanasi. 🔴 HAMMA maydon DOIM yuboriladi (`PUT` to'liq almashtirish),
 * jumladan `kind` — u ayni paytda tahrirlanmayotgan bo'lsa ham.
 */
function buildPayload(patch: Partial<LessonWriteRequest> = {}): LessonWriteRequest {
  const text = description.value.trim()
  const duration = durationText.value.trim()
  return {
    name: trimmedName.value,
    description: text.length > 0 ? text : null,
    durationMin: duration.length > 0 ? Number(duration) : null,
    kind: kind.value,
    ...patch,
  }
}

interface SaveInput {
  payload: LessonWriteRequest
  /** Tur almashtirish so'rovimi — 409 xabari boshqacha ko'rsatiladi. */
  kindChange: boolean
}

const saveMutation = useMutation({
  mutationFn: (input: SaveInput) => {
    const lesson = current.value
    return lesson === null
      ? createLesson(props.courseId, props.moduleId, input.payload)
      : updateLesson(props.courseId, props.moduleId, lesson.id, input.payload)
  },
  onSuccess: (result: CourseLessonDto) => {
    /*
      Serverdan kelgan DTO holatga yoziladi: `id` (yangi darsda), `kind` va
      `assets` AYNAN server aytgan qiymatda bo'lishi kerak — klientdagi
      taxmin bilan ajralib ketmasin.
    */
    savedLesson.value = result
    kind.value = result.kind
    // Yaratishda `assets` bo'sh keladi; tahrirlashda server ro'yxatini olamiz.
    assets.value = [...(result.assets ?? [])]
    errorMessage.value = null
    kindBlocked.value = null
    submitted.value = false
    emit('saved')
  },
  onError: (error: Error, input: SaveInput) => {
    /*
      🔴 409 — DOMAIN INVARIANTI: `Normal` darsda faqat video, `Exam` darsda
      faqat rasm bo'ladi. Server matni NECHTA fayl borligini va nima qilish
      kerakligini aytadi — o'z so'zimiz bilan qayta yozsak, aynan shu foydali
      qism yo'qolardi.

      Turni almashtirish rad etilganda ekrandagi segment tugma AVVALGI holatga
      qaytariladi: aks holda UI "imtihon" deb ko'rsatib turib, serverda dars
      `Normal` bo'lib qolardi.
    */
    if (input.kindChange) {
      kind.value = current.value?.kind ?? 'Normal'
      if (isApiError(error) && error.status === 409) {
        kindBlocked.value = toUserMessage(error)
        return
      }
    }
    errorMessage.value = toUserMessage(error)
  },
})

async function saveBasics(): Promise<void> {
  submitted.value = true
  if (!canSaveBasics.value || saveMutation.isPending.value) return

  const changes = basicsChanges()

  /*
    B2 jadvali: ma'lumotni ALMASHTIRUVCHI saqlash -> HAR DOIM `primary`
    tasdiq, o'zgargan maydonlar ro'yxati bilan. YARATISHDA tasdiq
    so'ralmaydi — yangi yozuv hech narsani almashtirmaydi va formani ikki
    qadamli qilib yuborishning ma'nosi yo'q.
  */
  if (isEdit.value) {
    if (changes.length === 0) return
    const ok = await confirm({
      title: 'Darsni saqlash',
      message:
        'Dars ma’lumotlari almashtiriladi. O‘quvchilar yangi matnni darhol ko‘radi.',
      confirmLabel: 'Saqlash',
      tone: 'primary',
      details: changes,
    })
    if (!ok) return
  }

  errorMessage.value = null
  saveMutation.mutate({ payload: buildPayload(), kindChange: false })
}

/* ==================================================== 2-bo'lim: dars turi */

const assetWord = computed(() => (kind.value === 'Exam' ? 'rasm' : 'video'))

/**
 * Turni almashtirish.
 *
 * ── 🔴 NEGA FAYL BOR BO'LSA SO'ROV UMUMAN YUBORILMAYDI ────────────────
 *
 * Invariant tufayli darsdagi HAMMA fayl eski turga tegishli, ya'ni yangi
 * turda birortasi ham qabul qilinmaydi — server javobi 409 bo'lishi
 * ANIQ (`ModuleLesson.ChangeKind`: `existingAssetCount > 0` -> istisno).
 * Foydalanuvchidan ALDANIB tugamaydigan amalni tasdiqlashini so'rash
 * ("davom etaymi?" -> "yo'q, bo'lmaydi") interfeysni yolg'onchi qiladi.
 * Shuning uchun sabab DARHOL, aniq raqam bilan aytiladi va nima qilish
 * kerakligi ko'rsatiladi.
 *
 * ⚠️ 409 ISHLOVCHISI SAQLANADI (`onError`): fayl boshqa xodim tomonidan
 * shu orada yuklangan bo'lishi mumkin — o'shanda server matni ko'rsatiladi.
 * Ya'ni klient tekshiruvi qoidaning NUSXASI emas, ARZON to'siq.
 */
async function selectKind(next: LessonKindName): Promise<void> {
  if (next === kind.value || saveMutation.isPending.value) return
  kindBlocked.value = null

  // YANGI dars: hech narsa saqlanmagan, tur shunchaki tanlanadi va
  // yaratishda birga yuboriladi.
  if (current.value === null) {
    kind.value = next
    return
  }

  const count = assets.value.length
  if (count > 0) {
    kindBlocked.value =
      `Dars turini o‘zgartirib bo‘lmaydi: darsda ${count} ta ${assetWord.value} bor va `
      + `yangi turda u qabul qilinmaydi. Avval shu ${count} ta faylni o‘chiring, keyin `
      + 'turni almashtiring. (Fayllar avtomatik o‘chirilmaydi — yuklangan video yoki '
      + 'rasm jimgina yo‘qolmasligi kerak.)'
    return
  }

  const ok = await confirm({
    title: 'Dars turini almashtirish',
    message:
      `Dars turi “${lessonKindLabel(kind.value)}” dan “${lessonKindLabel(next)}” ga `
      + 'o‘tadi. Bu darsga qanday fayl biriktirilishini belgilaydi: odatiy darsda '
      + 'VIDEO, imtihon darsida RASM.',
    confirmLabel: 'Almashtirish',
    // `warning` — yon ta'siri katta amal (keyinchalik fayl turi cheklanadi).
    tone: 'warning',
    details: [`${lessonKindLabel(kind.value)} → ${lessonKindLabel(next)}`],
  })
  if (!ok) return

  // Optimistik: segment darhol o'tadi (tugma "javob berdi"), xato bo'lsa
  // `onError` uni qaytaradi.
  kind.value = next
  errorMessage.value = null
  saveMutation.mutate({ payload: buildPayload({ kind: next }), kindChange: true })
}

/* =============================================== 3-bo'lim: fayllar ro'yxati */

/**
 * Fayl yuklandi / o'chirildi / tartibi almashdi.
 *
 * `saved` DARHOL emit qilinadi: server holati ALLAQACHON o'zgardi va kurs
 * daraxtidagi nishonlar ("3 video") eskirdi. Drawer esa OCHIQ qoladi —
 * xodim ketma-ket bir necha qism yuklaydi.
 */
function onAssetsChanged(next: LessonAssetDto[]): void {
  assets.value = next
  emit('saved')
}

/* ======================================================== yopish */

async function requestClose(): Promise<void> {
  if (saveMutation.isPending.value) return

  /*
    Saqlanmagan forma TASODIFAN yo'qolmasin. Fayl yuklash esa bo'lim ichida
    to'xtaydi (`useUploadQueue` `onScopeDispose` da `abort` qiladi) — shuning
    uchun ogohlantirish matni fayllarni ham eslatadi.
  */
  if (basicsDirty.value) {
    const ok = await confirm({
      title: 'Yopilsinmi?',
      message:
        'Dars ma’lumotlarida saqlanmagan o‘zgarishlar bor. Panel yopilsa ular '
        + 'yo‘qoladi. Davom etayotgan fayl yuklash ham to‘xtaydi.',
      confirmLabel: 'Yopish',
      tone: 'warning',
      details: basicsChanges(),
    })
    if (!ok) return
  }
  emit('close')
}
</script>

<template>
  <BaseDrawer
    :open="props.open"
    :title="isEdit ? 'Darsni tahrirlash' : 'Yangi dars'"
    :subtitle="props.moduleName"
    persistent
    @close="requestClose"
  >
    <div class="space-y-4">
      <!-- ============================================ 1) DARS MA'LUMOTLARI -->
      <section class="rounded-xl border border-line bg-ink-950 p-4">
        <h3 class="text-sm font-semibold text-slate-100">
          Dars ma’lumotlari
        </h3>

        <div class="mt-3">
          <BaseField
            label="Dars nomi"
            :error="submitted ? nameError : null"
          >
            <input
              v-model="name"
              class="zn-input js-modal-autofocus"
              maxlength="200"
            >
          </BaseField>
        </div>

        <div class="mt-3">
          <BaseField
            label="Tavsif"
            hint="Qulflangan darsda o‘quvchiga KO‘RSATILMAYDI — faqat sarlavha ko‘rinadi."
          >
            <textarea
              v-model="description"
              class="zn-input min-h-24 resize-y"
              rows="3"
            />
          </BaseField>
        </div>

        <div class="mt-3 sm:max-w-48">
          <BaseField
            label="Davomiylik (daqiqa)"
            hint="Ixtiyoriy."
            :error="durationError"
          >
            <input
              v-model="durationText"
              class="zn-input"
              type="number"
              min="1"
              inputmode="numeric"
            >
          </BaseField>
        </div>

        <p
          v-if="errorMessage !== null"
          class="mt-3 text-xs text-rose-400"
          role="alert"
          v-text="errorMessage"
        />

        <div class="mt-4 flex justify-end">
          <BaseButton
            size="sm"
            :loading="saveMutation.isPending.value"
            @click="saveBasics"
          >
            {{ isEdit ? 'Saqlash' : 'Darsni yaratish' }}
          </BaseButton>
        </div>
      </section>

      <!-- ==================================================== 2) DARS TURI -->
      <section class="rounded-xl border border-line bg-ink-950 p-4">
        <h3 class="text-sm font-semibold text-slate-100">
          Dars turi
        </h3>
        <p class="mt-0.5 text-[11px] leading-relaxed text-dim">
          Tur darsga qanday fayl biriktirilishini belgilaydi: odatiy darsda VIDEO,
          imtihon darsida RASM. Fayl biriktirilgandan keyin turni almashtirish uchun
          avval fayllarni o‘chirish kerak.
        </p>

        <!--
          Segment tugma. Faol holat `bg-brand-500 text-on-brand` (auditda
          5.90:1), nofaol `text-slate-400` (oq sirtda 5.20:1).
        -->
        <div
          class="mt-2.5 inline-flex rounded-xl border border-line-strong bg-ink-900 p-1"
          role="group"
          aria-label="Dars turi"
        >
          <button
            v-for="option in LESSON_KIND_OPTIONS"
            :key="option.value"
            type="button"
            class="min-h-9 rounded-lg px-3.5 text-xs font-semibold transition-colors"
            :class="
              kind === option.value
                ? 'bg-brand-500 text-on-brand'
                : 'text-slate-400 hover:text-slate-100'
            "
            :aria-pressed="kind === option.value"
            :title="option.hint"
            :disabled="saveMutation.isPending.value"
            @click="selectKind(option.value)"
          >
            {{ option.label }}
          </button>
        </div>

        <p
          v-if="kindBlocked !== null"
          class="mt-2.5 rounded-lg border border-amber-500/25 bg-amber-500/10 p-2.5 text-[11px] leading-relaxed text-amber-200"
          role="alert"
          v-text="kindBlocked"
        />
        <p
          v-else
          class="mt-2 text-[11px] text-dim"
        >
          Hozirgi tur: <span class="font-medium">{{ lessonKindLabel(kind) }}</span>
          <span v-if="!isEdit"> (dars yaratilganda saqlanadi)</span>
        </p>
      </section>

      <!-- ============================================== 3) MEDIA (fayllar) -->
      <section class="rounded-xl border border-line bg-ink-950 p-4">
        <LessonAssetsSection
          v-if="current !== null"
          :lesson-id="current.id"
          :lesson-kind="kind"
          :assets="assets"
          @update:assets="onAssetsChanged"
        />
        <template v-else>
          <h3 class="text-sm font-semibold text-slate-100">
            Video qismlari
          </h3>
          <p class="mt-1 text-[11px] leading-relaxed text-dim">
            Fayl yuklash uchun avval darsni saqlang — fayl mavjud darsga bog‘lanadi.
            Saqlangandan keyin bu bo‘lim shu yerda ochiladi (panel yopilmaydi).
          </p>
        </template>
      </section>

      <!-- ================================================= 4) UY VAZIFASI -->
      <section class="rounded-xl border border-line bg-ink-950 p-4">
        <h3 class="mb-3 text-sm font-semibold text-slate-100">
          Uy vazifasi
        </h3>
        <LessonAssignmentSection
          v-if="current !== null"
          :lesson-id="current.id"
          :enabled="props.open"
          @changed="emit('saved')"
        />
        <p
          v-else
          class="text-[11px] leading-relaxed text-dim"
        >
          Vazifa darsga biriktiriladi, ya'ni avval dars saqlanishi kerak.
        </p>
      </section>
    </div>

    <template #footer>
      <BaseButton
        variant="secondary"
        :disabled="saveMutation.isPending.value"
        @click="requestClose"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseDrawer>
</template>
