<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import LessonDashboard from '@/features/lesson-dashboard/ui/LessonDashboard.vue'
import {
  lockMessage,
  useStudentCourse,
} from '@/features/student-course/model/useStudentCourse'
import { AppIcon, BaseButton } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS SAHIFASI — `/oquv/dars/:lessonId` (2026-10-29, audit 2.4-d)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Ilgari dars O'quv sahifasining ustida `BaseModal` bo'lib ochilardi.
 * Telefonda bu ikki muammo berardi:
 *   • tizimning "orqaga" tugmasi (Android, Telegram `BackButton`) modalni
 *     yopmasdi — tarixda modal yo'q, u O'quv sahifasidan chiqib ketardi;
 *   • darsga havola berib bo'lmasdi (kurator "7-darsni ko'ring" deb
 *     yubora olmasdi).
 * Endi har dars — o'z marshruti, mazmuni esa O'SHA `LessonDashboard`
 * (nusxa emas).
 *
 * ★ DARS QAYERDAN OLINADI: alohida "bitta dars" endpoint'i YO'Q va kerak
 * ham emas — `useStudentCourse` daraxti gating natijasi (`unlocked`,
 * `lockReason`) bilan birga keladi va 5 daqiqa keshda turadi. O'quv
 * sahifasidan kelinganda so'rov umuman ketmaydi; havoladan to'g'ridan-to'g'ri
 * kirilganda esa O'quv sahifasi yuboradigan AYNI ikki so'rov ketadi.
 *
 * ★ QULF SERVERDA: bu yerda qulflangan darsning mazmuni ko'rsatilmaydi,
 * lekin bu HIMOYA emas — server qulflangan darsda `assets` va
 * `description` ni baribir bo'sh yuboradi (`CourseLessonDto` izohi).
 * Sahifa faqat SABABni aytadi (yo'lakchadagi toast bilan ayni matn).
 *
 * ★ "O'QUV" BANDI YONIB TURADI: `useStudentNav.isActive` qoidasi
 * `student-` nomli va `/oquv` ostidagi har marshrutni "O'quv" ga bog'laydi.
 * Mehmon `StudentShell` dagi `GUEST_ROUTES` orqali Tariflarga qaytariladi,
 * mustaqil o'quvchi esa kirabveradi (bu `GROUP_ONLY_ROUTES` da YO'Q).
 */
const route = useRoute()
const router = useRouter()
const course = useStudentCourse()

/** Marshrut `(\d+)` bilan cheklangan, ya'ni bu yerda doim butun son. */
const lessonId = computed(() => Number(route.params['lessonId']))

const entry = computed(() => course.lessonsById.value.get(lessonId.value) ?? null)

function goToLearn(): void {
  void router.push({ name: 'student-learn' })
}

const subtitle = computed(() =>
  entry.value === null ? '' : `${entry.value.number}-dars · ${entry.value.module.name ?? ''}`,
)
</script>

<template>
  <!--
    ★ `lg:max-w-5xl`: ilgari dars `BaseModal xl` (`sm:max-w-5xl`) ichida edi.
    1600px lik ustunda video cheksiz kengayib, ekranga sig'masdi — modal
    kengligi saqlandi, ko'rinish desktopda o'zgarmadi. Telefonda (520px
    ustun) bu chegara umuman ta'sir qilmaydi.
  -->
  <div class="lg:max-w-5xl">
    <div
      v-if="course.isPending.value"
      class="space-y-3"
      aria-busy="true"
    >
      <div class="mb-4 mt-2 h-11 w-2/3 animate-pulse rounded-xl bg-ink-800" />
      <div class="aspect-video animate-pulse rounded-xl border border-line bg-ink-900" />
      <div class="h-24 animate-pulse rounded-xl border border-line bg-ink-900" />
    </div>

    <template v-else-if="course.error.value !== null">
      <StudentSubHeader title="Dars" />
      <div
        class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
        role="alert"
      >
        <p
          class="text-sm text-rose-200"
          v-text="course.error.value"
        />
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          :loading="course.isFetching.value"
          @click="course.refetch()"
        >
          Qayta urinish
        </BaseButton>
      </div>
    </template>

    <!--
      Topilmadi: eskirgan havola, boshqa kursning darsi yoki o'chirilgan
      dars. Server bu o'quvchiga bermagan darsni sahifa "taxmin" qilmaydi.
    -->
    <template v-else-if="entry === null">
      <StudentSubHeader title="Dars topilmadi" />
      <div class="rounded-xl border border-line bg-ink-900 px-[18px] py-[26px] text-center">
        <AppIcon
          name="book"
          :size="28"
          class="mx-auto text-slate-400"
        />
        <p class="mt-3 text-[15px] font-bold">
          Bunday dars yo‘q
        </p>
        <p class="mt-[7px] text-[13px] leading-relaxed text-slate-400">
          Havola eskirgan yoki bu dars sizning kursingizga kirmaydi.
        </p>
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          @click="goToLearn"
        >
          O‘quv bo‘limiga qaytish
        </BaseButton>
      </div>
    </template>

    <template v-else>
      <StudentSubHeader
        :title="entry.lesson.name ?? 'Dars'"
        :subtitle="subtitle"
      />

      <!--
        QULFLANGAN DARS — havoladan kirilgan bo'lishi mumkin (yo'lakchada
        qulflangan tugun sahifaga olib bormaydi, toast chiqaradi). Sabab
        yo'lakchadagi bilan AYNI matn (`lockMessage`).
      -->
      <div
        v-if="!entry.lesson.unlocked"
        class="rounded-xl border border-line bg-ink-900 px-[18px] py-[26px] text-center"
      >
        <span class="mx-auto flex size-14 items-center justify-center rounded-full bg-ink-800 text-slate-400">
          <AppIcon
            name="lock"
            :size="26"
          />
        </span>
        <p class="mt-3 text-[15px] font-bold">
          Dars hali yopiq
        </p>
        <p
          class="mx-auto mt-[7px] max-w-sm text-[13px] leading-relaxed text-slate-400"
          v-text="lockMessage(entry.lesson.lockReason)"
        />
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          @click="goToLearn"
        >
          O‘quv bo‘limiga qaytish
        </BaseButton>
      </div>

      <LessonDashboard
        v-else
        :lesson="entry.lesson"
      />
    </template>
  </div>
</template>
