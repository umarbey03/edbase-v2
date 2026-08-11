<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchCourseTree } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import { BaseField } from '@/shared/ui'

/**
 * "VIDEO DARSLAR QAYSI QISMDAN BOSHLANADI" — kurs darsini tanlash.
 *
 * NEGA GURUH DARAJASIDA: bitta kursga ko'p guruh biriktiriladi va yarim
 * yildan keyin ochilgan guruh kursni 1-moduldan boshlamaydi. `null` —
 * kursni boshidan boshlaydi (eng ko'p uchraydigan holat).
 *
 * MANBA: `GET /courses/{id}` daraxti (modul → dars). Naqsh
 * `TestLessonPicker` dan olingan (`<optgroup>` bilan modul bo'yicha
 * guruhlash) — farqi shundaki KURS BU YERDA TANLANMAYDI: u guruhning kursi,
 * ya'ni tashqaridan keladi.
 *
 * QIDIRUV KLIENTDA: daraxt bitta so'rovda TO'LIQ keladi va darslar bo'yicha
 * server qidiruvi umuman yo'q. Yuzlab darsli kursda `<select>` ni aylantirib
 * chiqish qiyin, shuning uchun ustida filtr maydoni turadi.
 *
 * 🔴 KURS TANLANMAGAN BO'LSA TANLAGICH O'CHIQ: kurssiz guruhda dars Id'si
 * yuborilsa server 400 beradi ("Guruhga kurs biriktirilmagan").
 */
const props = withDefaults(
  defineProps<{
    /** Tanlangan dars Id'si (`null` — kurs boshidan). */
    modelValue: number | null
    /** Guruhning kursi. `null` bo'lsa tanlagich o'chiq. */
    courseId: number | null
    /** Panel ochiqmi — yopiq panelda daraxt so'ralmasin (og'ir javob). */
    enabled: boolean
    /**
     * Server bergan nom ("Harf moduli · Harflar 2"). Daraxt hali
     * yuklanmaganda yoki tanlangan dars filtrdan tushib qolganda
     * `<select>` bo'sh ko'rinmasligi uchun kerak.
     */
    selectedLabel?: string
  }>(),
  { selectedLabel: '' },
)

const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

const search = ref('')

const treeQuery = useQuery({
  queryKey: ['course', 'group-video-start', computed(() => props.courseId)],
  queryFn: ({ signal }) => fetchCourseTree(props.courseId ?? 0, { signal }),
  enabled: computed(() => props.enabled && props.courseId !== null),
})

const modules = computed(() => treeQuery.data.value?.modules ?? [])

const lessonCount = computed(() =>
  modules.value.reduce((total, module) => total + (module.lessons?.length ?? 0), 0),
)

/**
 * Filtr HAM modul nomi, HAM dars nomi bo'yicha ishlaydi: xodim ko'pincha
 * "3-modul" deb qidiradi, dars nomini eslamaydi.
 */
const filteredModules = computed(() => {
  const needle = search.value.trim().toLowerCase()
  if (needle.length === 0) return modules.value

  return modules.value
    .map((module) => {
      const moduleMatches = (module.name ?? '').toLowerCase().includes(needle)
      const lessons = (module.lessons ?? []).filter(
        (lesson) => moduleMatches || (lesson.name ?? '').toLowerCase().includes(needle),
      )
      return { ...module, lessons }
    })
    .filter((module) => module.lessons.length > 0)
})

/**
 * Tanlangan darsning "Modul · Dars" nomi DARAXTDAN olinadi.
 *
 * 🔴 NEGA `selectedLabel` PROP'I YETMAYDI: u SERVERDAGI qiymatning nomi.
 * Foydalanuvchi boshqa darsni tanlagan zahoti prop eskiradi va ekranda
 * ESKI dars nomi turib qolardi ("saqlanmagan holat to'g'ri ko'rinmaydi"
 * xatosi). Daraxt yuklanmagan bo'lsa prop zaxira sifatida ishlatiladi.
 */
const displayLabel = computed(() => {
  const value = props.modelValue
  if (value === null) return ''
  for (const module of modules.value) {
    const lesson = (module.lessons ?? []).find((item) => item.id === value)
    if (lesson !== undefined) {
      return `${module.name ?? `Modul #${module.id}`} · ${lesson.name ?? `Dars #${lesson.id}`}`
    }
  }
  return props.selectedLabel
})

/**
 * Tanlangan dars filtrdan tushib qolgan bo'lsa `<select>` qiymati mos
 * `<option>` topmasdi va brauzer BO'SH qatorni ko'rsatardi — foydalanuvchi
 * "tanlov o'chib ketdi" deb o'ylardi. Shuning uchun u alohida qator bilan
 * qaytariladi (naqsh `GroupFormDialog` dagi `missingCourseOption` bilan bir xil).
 */
const orphanOption = computed(() => {
  const value = props.modelValue
  if (value === null) return null
  const present = filteredModules.value.some((module) =>
    (module.lessons ?? []).some((lesson) => lesson.id === value),
  )
  if (present) return null
  return displayLabel.value.length > 0 ? displayLabel.value : `Tanlangan dars #${value}`
})

const treeError = computed(() =>
  treeQuery.error.value !== null ? toUserMessage(treeQuery.error.value) : null,
)

const hint = computed(() => {
  if (props.courseId === null) {
    return 'Avval guruhga kurs biriktiring — dars faqat shu kursdan tanlanadi.'
  }
  if (treeQuery.isFetching.value && lessonCount.value === 0) return 'Kurs darslari yuklanmoqda…'
  if (lessonCount.value === 0) return 'Bu kursda hali dars yo‘q — avval kurs kontentini to‘ldiring.'
  return 'Bo‘sh qoldirilsa guruh kursni boshidan boshlaydi.'
})

function onChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  emit('update:modelValue', value.length > 0 ? Number(value) : null)
}
</script>

<template>
  <div>
    <BaseField
      label="Video darslar qaysi qismdan boshlanadi"
      :hint="hint"
      :error="treeError"
    >
      <select
        class="zn-input"
        :value="props.modelValue ?? ''"
        :disabled="props.courseId === null"
        @change="onChange"
      >
        <option value="">
          Kurs boshidan
        </option>
        <option
          v-if="orphanOption !== null"
          :value="props.modelValue ?? ''"
        >
          {{ orphanOption }}
        </option>
        <optgroup
          v-for="module in filteredModules"
          :key="module.id"
          :label="module.name ?? `Modul #${module.id}`"
        >
          <option
            v-for="lesson in module.lessons ?? []"
            :key="lesson.id"
            :value="lesson.id"
          >
            {{ lesson.position }}. {{ lesson.name ?? `Dars #${lesson.id}` }}
          </option>
        </optgroup>
      </select>
    </BaseField>

    <!--
      Filtr TANLAGICHDAN KEYIN turadi: u ikkinchi darajali yordamchi, va
      darslari kam kursda umuman kerak emas. 12 tadan ko'p dars bo'lganda
      ko'rinadi — chegaraga sabab: `<select>` ochilganda ~12 qator ekranga
      sig'adi, undan keyin skroll boshlanadi.
    -->
    <input
      v-if="props.courseId !== null && lessonCount > 12"
      v-model="search"
      class="zn-input mt-2 text-[13px]"
      type="search"
      aria-label="Dars yoki modul nomi bo‘yicha filtr"
      placeholder="Dars/modul nomi bo‘yicha filtr…"
    >

    <p
      v-if="displayLabel.length > 0"
      class="mt-1.5 text-[11px] text-slate-400"
    >
      Tanlangan: {{ displayLabel }}
    </p>
  </div>
</template>
