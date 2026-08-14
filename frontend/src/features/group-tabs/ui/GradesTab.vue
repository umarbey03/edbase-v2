<script setup lang="ts">
import { computed, ref } from 'vue'

import { AppIcon, BaseButton, BaseCard } from '@/shared/ui'

import AssignmentGradesView from './AssignmentGradesView.vue'
import LessonGradesView from './LessonGradesView.vue'

/**
 * ========================================================================
 * "Baholar" tabi (R24) — eski `#tab-grades` ("Baholar jadvali")
 * ========================================================================
 *
 * Loyiha egasi: *"baholar qismida guruh studentlari baholari jadval
 * ko'rinishida joylashsin"* va *"baholar har bitta darsga qo'yiladi"*.
 *
 * ── ★ NIMA UCHUN IKKI KO'RINISH ────────────────────────────────────────
 *
 * R24 gacha v2 da "baho" degan mustaqil obyekt YO'Q edi: baho har doim
 * TOPSHIRIQqa bog'langan (`Submission.Score`), ya'ni "baho qo'yish" —
 * topshirilgan ISHNI baholash. R24 esa bahoni DARSGA bog'laydi va buning
 * uchun yangi obyekt (`LessonGrade`) qo'shildi.
 *
 * 🔴 ESKI BAHOLARNI YANGI SHAKLGA KO'CHIRIB BO'LMAYDI: dars ↔ vazifa
 *    xaritasi MAVJUD EMAS. `Assignment` yo GURUHGA, yo KURS DARSIGA
 *    (`ModuleLessonId`) bog'lanadi — jonli darsga (`LiveSession`) hech
 *    qachon. Ya'ni "bu vazifa qaysi darsda berilgan?" degan savolga
 *    javob beradigan ma'lumot yo'q va uni taxmin qilib to'ldirish
 *    yolg'on tarix yasardi.
 *
 * ★ SHUNING UCHUN QAROR: ikkala ko'rinish ham YONMA-YON yashaydi.
 *   • "Darslar" — ASOSIY (standart) ko'rinish, R24 ning javobi;
 *   • "Vazifalar" — allaqachon qo'yilgan HAMMA bahoni ko'rsatadi.
 *
 *   Ikkinchisi olib tashlansa ustoz mavjud baholarni "Baholar" tabida
 *   umuman ko'ra olmay qolardi — sof REGRESSIYA. Birlashtirilgan bitta
 *   jadval esa (ustunlar aralash: darslar + vazifalar) "Jami" ustunini
 *   ma'nosiz qilardi va ikki xil obyektni bitta katakda ko'rsatardi.
 *
 * ★ TANLOV SAQLANMAYDI (localStorage yo'q): tab har ochilganda
 *   "Darslar" dan boshlanadi. Bu ataylab — R24 ning asosiy talabi shu
 *   ko'rinish va u standart bo'lib qolishi kerak.
 */
const props = defineProps<{
  groupId: number
  groupName: string
}>()

type Mode = 'lessons' | 'assignments'

const MODES: readonly { value: Mode; label: string; hint: string }[] = [
  {
    value: 'lessons',
    label: 'Darslar',
    hint: 'Ustunlar — guruhning darslari, katakka bosib baho qo‘yiladi.',
  },
  {
    value: 'assignments',
    label: 'Vazifalar',
    hint: 'Ustunlar — uy vazifalari, kataklar — qo‘yilgan ball (faqat ko‘rish).',
  },
]

const mode = ref<Mode>('lessons')

const hint = computed(
  () => MODES.find((item) => item.value === mode.value)?.hint ?? '',
)

/*
  ★ CSV TUGMASI KARTOCHKA SARLAVHASIDA, EKSPORT MANTIQI ESA
  KO'RINISHNING ICHIDA. Ma'lumot faqat faol ko'rinishda mavjud (ikkinchisi
  `v-if` bilan umuman qurilmagan, ya'ni uning so'rovlari ham ketmaydi) —
  shuning uchun tugma faol ko'rinishning ochiq a'zolarini shablon havolasi
  orqali chaqiradi. Ma'lumotni bu yerga ko'chirish ikkala ko'rinishni ham
  doim yuklab turishga majbur qilardi.
*/
const lessonsView = ref<InstanceType<typeof LessonGradesView> | null>(null)
const assignmentsView = ref<InstanceType<typeof AssignmentGradesView> | null>(null)

const activeView = computed(() =>
  mode.value === 'lessons' ? lessonsView.value : assignmentsView.value,
)

const canExport = computed(() => activeView.value?.hasData === true)

function exportCsv(): void {
  activeView.value?.exportCsv()
}
</script>

<template>
  <BaseCard
    flush
    title="Baholar jadvali"
    :subtitle="hint"
  >
    <template #actions>
      <BaseButton
        size="sm"
        variant="secondary"
        :disabled="!canExport"
        @click="exportCsv"
      >
        <template #icon>
          <AppIcon
            name="download"
            :size="13"
          />
        </template>
        CSV yuklab olish
      </BaseButton>
    </template>

    <div class="p-3.5 sm:p-5">
      <!--
        Ko'rinish tanlagichi — ilovadagi boshqa segmentli tugmalar bilan
        bir xil naqshda (`AttendanceCellDialog` dagi `.seg` bloki).
        Balandligi 44px: telefonda barmoq nishoni.
      -->
      <div
        class="mb-3.5 inline-flex rounded-lg border border-line bg-ink-950 p-0.5"
        role="tablist"
        aria-label="Baholar ko‘rinishi"
      >
        <button
          v-for="item in MODES"
          :key="item.value"
          type="button"
          role="tab"
          class="min-h-11 rounded-md px-4 text-xs font-semibold transition-colors"
          :class="
            mode === item.value
              ? 'bg-brand-500 text-on-brand'
              : 'text-slate-300 hover:bg-ink-900'
          "
          :aria-selected="mode === item.value"
          @click="mode = item.value"
          v-text="item.label"
        />
      </div>

      <!--
        🔴 `v-if`/`v-else`, `v-show` EMAS: har ko'rinish o'z so'rovlar
        guruhini yuboradi (darslar uchun N ta varaq, vazifalar uchun N ta
        topshiriq ro'yxati). `v-show` bo'lsa ikkalasi ham HAR DOIM
        yuklanardi — ya'ni tab ochilishi ikki barobar so'rov qilardi va
        ustoz ko'rmaydigan ma'lumot uchun kutardi.
      -->
      <LessonGradesView
        v-if="mode === 'lessons'"
        ref="lessonsView"
        :group-id="props.groupId"
        :group-name="props.groupName"
      />
      <AssignmentGradesView
        v-else
        ref="assignmentsView"
        :group-id="props.groupId"
        :group-name="props.groupName"
      />

      <!--
        🔴 IKKI KO'RINISHNING ALOQASI OCHIQ AYTILADI. Busiz ustoz
        "Darslar" da bo'sh jadval ko'rib, avval qo'ygan baholari
        yo'qolgan deb o'ylardi — ular BOSHQA obyekt va ko'chirilmagan
        (sabab skriptdagi izohda).
      -->
      <p class="mt-2 text-[11px] text-dim">
        <template v-if="mode === 'lessons'">
          Vazifalarga qo‘yilgan eski baholar bu jadvalga
          <b class="font-semibold">ko‘chirilmaydi</b> — ular “Vazifalar”
          ko‘rinishida turadi.
        </template>
        <template v-else>
          Bu ko‘rinish faqat ko‘rsatadi; vazifa baholanadigan joy —
          “Vazifalar” tabi. Darsga baho qo‘yish uchun “Darslar” ko‘rinishiga
          o‘ting.
        </template>
      </p>
    </div>
  </BaseCard>
</template>
