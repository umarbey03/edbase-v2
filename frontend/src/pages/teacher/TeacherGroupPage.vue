<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  archiveGroup,
  fetchGroup,
  fetchGroupSchedule,
  groupDisplayName,
  groupScheduleSummary,
  groupTypeLabel,
  regenerateSchedule,
  restoreGroup,
  videoStartLabel,
} from '@/entities/group'
import { isManagerRole } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { GroupChatRoom } from '@/features/group-chat'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import ReopenDialog from '@/features/grading/ui/ReopenDialog.vue'
import {
  AttendanceTab,
  BoardTab,
  defaultGroupTab,
  GradesTab,
  GroupTabs,
  heldSummary,
  LessonsTab,
  TasksTab,
  TestsTab,
  UpNextBanner,
  visibleGroupTabs,
} from '@/features/group-tabs'
import type { GroupTabKey } from '@/features/group-tabs'
import GroupMembersPanel from '@/features/group-members/ui/GroupMembersPanel.vue'
import StudentProfileDrawer from '@/features/student-profile/ui/StudentProfileDrawer.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import type { SubmissionDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  ConfirmDeleteDialog,
  DataStatus,
  PageHeader,
  TodayPill,
} from '@/shared/ui'
import { RecordingBoard } from '@/widgets/recording-board'

/**
 * GURUH ICHI — eski `teacher.html` dagi `#group` bo'limi.
 *
 * KARKAS eski ilovadagidek: "← Orqaga", guruh nomi, o'tilgan darslar
 * xulosasi, "Bugun" tabletkasi, "keyingi dars" banneri va 8 ta TAB.
 *
 * ★ TAB ALOHIDA MARSHRUT EMAS — sahifa ichidagi holat (eski `switchTab()`).
 * Tab mazmuni `v-if` bilan chiziladi, ya'ni so'rov FAQAT tab ochilganda
 * yuboriladi (eski `LOADED` bayroqlari shu vazifani bajarardi); qaytib
 * kelganda ma'lumot keshdan olinadi.
 *
 * ★ "Guruh tahlili" kartochkalari (faol o'quvchilar / o'rtacha davomat /
 * o'rtacha baho / xavf ostida) ATAYLAB YO'Q — eski ilovada ular T3
 * o'zgarishida HAMMA uchun o'chirilgan edi (`group-analytics` ->
 * `display:none`), ya'ni bugungi foydalanuvchi ularni ko'rmaydi.
 */
const route = useRoute()
const router = useRouter()

const rawId = route.params['groupId']
const groupId = Number(Array.isArray(rawId) ? rawId[0] : rawId)
const isValidId = Number.isInteger(groupId) && groupId > 0

const groupQuery = useQuery({
  queryKey: ['group', groupId],
  queryFn: ({ signal }) => fetchGroup(groupId, { signal }),
  enabled: isValidId,
})

/**
 * Jadval sahifa sarlavhasi uchun ham kerak ("O'tilgan darslar — ...").
 * Kalit "Darslar" tabidagi bilan bir xil — so'rov bir marta yuboriladi.
 */
const scheduleQuery = useQuery({
  queryKey: ['group', groupId, 'schedule'],
  queryFn: ({ signal }) => fetchGroupSchedule(groupId, { signal }),
  enabled: isValidId,
})

const group = computed(() => groupQuery.data.value ?? null)
const schedule = computed(() => scheduleQuery.data.value ?? [])

/**
 * Bu sahifani ustoz va kurator ham ochadi (guruhlar ro'yxatidan), lekin
 * o'zgartirish amallari faqat o'quv bo'limi va adminda — server ham shunday
 * qulflagan (`[Authorize(Roles = "Academic,Admin")]`). Bu yerda faqat
 * TUGMALAR yashiriladi; qoida takrorlanmaydi.
 */
const auth = useAuthStore()
const canManage = computed(() => auth.role !== null && isManagerRole(auth.role))

/*
  TABLAR ROLGA QARAB: kuratorda Testlar/Reyting yo'q (eski qoida), o'quv
  bo'limi/adminda esa "O'quvchilar" BIRINCHI (yangi talab, `tabs.ts` izohi).
  Ustoz/kuratorda tartib TEGILMAGAN.
*/
const tabs = computed(() => visibleGroupTabs(auth.role))

/*
  Standart tab ham rolga mos: `defaultGroupTab` ko'rinadigan tablarning
  birinchisini beradi, ya'ni o'quv bo'limi guruhga kirganda darhol o'quvchilar
  ro'yxatini ko'radi. Rol router guard'ida (`auth.bootstrap()`) allaqachon
  aniqlangan bo'ladi, shuning uchun boshlang'ich qiymat to'g'ri chiqadi.
*/
const activeTab = ref<GroupTabKey>(defaultGroupTab(auth.role))

const groupError = computed(() =>
  groupQuery.error.value !== null ? toUserMessage(groupQuery.error.value) : null,
)

/* ------------------------------------------------ guruh hayot sikli amallari */

const queryClient = useQueryClient()

const actionError = ref<string | null>(null)
/** Jadval qayta tuzilgach natija SHU YERDA ko'rsatiladi (nechta dars o'zgardi). */
const actionNote = ref<string | null>(null)

function refreshGroup(): void {
  void queryClient.invalidateQueries({ queryKey: ['group', groupId] })
  void queryClient.invalidateQueries({ queryKey: ['groups'] })
}

const archiveMutation = useMutation({
  mutationFn: () => (group.value?.isActive === true ? archiveGroup(groupId) : restoreGroup(groupId)),
  onSuccess: () => {
    actionNote.value = null
    refreshGroup()
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

/*
  Jadvalni qayta tuzish ATAYLAB tasdiqlanadi: u o'nlab kelajakdagi darsni
  almashtiradi. Server o'tgan, jonli va yakunlangan darslarga tegmaydi —
  buni tasdiqlash matnida aytamiz, aks holda "davomat tarixim o'chib
  ketadimi?" degan qo'rquv bilan hech kim bosmaydi.
*/
const regenerateOpen = ref(false)
const regenerateError = ref<string | null>(null)

const regenerateMutation = useMutation({
  mutationFn: () => regenerateSchedule(groupId),
  onSuccess: (summary) => {
    regenerateOpen.value = false
    actionError.value = null
    actionNote.value = summary.scheduleTouched
      ? `Jadval qayta tuzildi: +${summary.created} yangi, −${summary.deleted} o‘chirildi, ${summary.preserved} dars saqlab qolindi.`
      : 'Jadvalga tegilmadi — o‘zgarish topilmadi.'
    refreshGroup()
  },
  onError: (error: Error) => {
    regenerateError.value = toUserMessage(error)
  },
})

/* ------------------------------------- o'quvchi profili va to'lov holati */

/**
 * ★ INTEGRATSIYADA ULANDI (`wave2/groups` qoldirgan `TODO(wave2/users)`).
 *
 * Ilgari bu ikki ishlovchi faqat "keyinchalik ulanadi" degan izoh chiqarardi:
 * `features/student-profile` qardosh branch'da yozilayotgan edi va uni bu
 * yerdan import qilish ikki branch'ni bir-biriga bog'lab merge'ni to'sardi.
 * Ikkala branch ham merge qilingandan keyin bu bog'liqlik yo'q — panel
 * ULANDI, aks holda beshta ikonkadan IKKITASI hech narsa qilmasdi.
 *
 * 🔴 IKKI IKONKA BITTA PANELNI ochadi (profil ichida to'lov bo'limi ham bor).
 * Ular ATAYLAB birlashtirilmadi: "To'lov holati" xodimning odatiy yo'li va
 * eski ilovada alohida tugma bo'lgan. To'lov bo'limining KO'RINISHI rolga
 * bog'liq va u SERVERDA kesiladi (`finance === null` -> bo'lim umuman
 * render qilinmaydi), ya'ni ustoz "To'lov holati" ni bossa ham moliyani
 * ko'rmaydi — panel shunchaki profilni ko'rsatadi.
 */
const studentProfileId = ref<number | null>(null)
const studentProfileOpen = ref(false)

function openStudentProfile(studentId: number): void {
  studentProfileId.value = studentId
  studentProfileOpen.value = true
}

/*
  "To'lov holati" ikonkasi ayni panelni ochadi — to'lov bo'limi shu panelning
  ichida. Alohida oyna yasash ma'lumotni ikki joyda ko'rsatardi.
*/
const openStudentWallet = openStudentProfile

/* -------------------------------------------------- baholash oynalari */

/**
 * Baholash va qaytarish oynalari SAHIFA darajasida: "Vazifalar" tabi
 * ularni faqat CHAQIRADI (hodisa bilan). Shunda `features/group-tabs`
 * boshqa feature'dan (`features/grading`) import qilmaydi — FSD'da
 * feature'lar bir-biriga bog'lanmaydi, ularni sahifa yig'adi.
 */
const grading = ref<{ submission: SubmissionDto; maxScore: number } | null>(null)
const reopening = ref<SubmissionDto | null>(null)

function refreshSubmissions(): void {
  void queryClient.invalidateQueries({ queryKey: ['assignment-submissions'] })
  void queryClient.invalidateQueries({ queryKey: ['group', groupId, 'assignments'] })
  void queryClient.invalidateQueries({ queryKey: ['group', groupId, 'grade-matrix'] })
}
</script>

<template>
  <div>
    <button
      type="button"
      class="mb-3 inline-flex min-h-11 items-center gap-1.5 rounded-lg pr-3 text-xs font-medium text-slate-400 transition-colors hover:text-slate-100"
      @click="router.push({ name: 'teacher-groups' })"
    >
      <AppIcon
        name="arrow-left"
        :size="15"
      />
      Orqaga
    </button>

    <DataStatus
      :pending="isValidId && groupQuery.isPending.value"
      :error="isValidId ? groupError : 'Guruh manzili noto‘g‘ri.'"
      :empty="isValidId && group === null && !groupQuery.isPending.value && groupError === null"
      :skeleton-rows="1"
      empty-title="Guruh topilmadi"
      @retry="groupQuery.refetch()"
    >
      <template v-if="group !== null">
        <PageHeader
          :title="groupDisplayName(group)"
          :subtitle="heldSummary(schedule)"
        >
          <template
            v-if="canManage"
            #actions
          >
            <BaseBadge :tone="group.isActive ? 'success' : 'neutral'">
              {{ group.isActive ? 'Faol' : 'Arxiv' }}
            </BaseBadge>
            <BaseButton
              size="sm"
              variant="secondary"
              @click="regenerateOpen = true"
            >
              <template #icon>
                <AppIcon
                  name="refresh"
                  :size="13"
                />
              </template>
              Jadvalni qayta tuzish
            </BaseButton>
            <BaseButton
              size="sm"
              :variant="group.isActive ? 'danger' : 'primary'"
              :loading="archiveMutation.isPending.value"
              @click="archiveMutation.mutate()"
            >
              {{ group.isActive ? 'Arxivlash' : 'Tiklash' }}
            </BaseButton>
          </template>
        </PageHeader>

        <!--
          Guruh haqidagi qisqa qator — eski ilovada bu joyda faqat o'tilgan
          darslar soni turardi. Ustoz uchun QISQA holat saqlangan; o'quv
          bo'limi esa shu sahifadan guruhni boshqaradi, shuning uchun unga
          to'liq kartochka pastroqda ko'rsatiladi.
        -->
        <p class="-mt-2 mb-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-slate-400">
          <span>{{ groupTypeLabel(group.type) }}</span>
          <span class="text-dim">·</span>
          <span class="tabular-nums">{{ groupScheduleSummary(group) }}</span>
          <span class="text-dim">·</span>
          <span>Ustoz: {{ group.teacherName ?? '—' }}</span>
          <span class="text-dim">·</span>
          <span>Kurator: {{ group.assistantName ?? group.curatorGroupName ?? '—' }}</span>
        </p>

        <TodayPill />

        <div
          v-if="actionNote !== null"
          class="mb-4 rounded-lg border border-brand-500/30 bg-brand-500/10 p-3.5 text-sm text-brand-200"
          v-text="actionNote"
        />
        <div
          v-if="actionError !== null"
          class="mb-4 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
          role="alert"
          v-text="actionError"
        />

        <!-- O'quv bo'limi uchun to'liq guruh xulosasi (ustozda ko'rinmaydi). -->
        <BaseCard
          v-if="canManage"
          class="mb-4"
        >
          <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-xs sm:grid-cols-4">
            <div>
              <dt class="text-dim">
                Kurs
              </dt>
              <dd
                class="mt-0.5 truncate font-medium text-slate-200"
                v-text="group.courseName ?? '—'"
              />
            </div>
            <div>
              <dt class="text-dim">
                Davomiylik
              </dt>
              <dd class="mt-0.5 font-medium tabular-nums text-slate-200">
                {{ group.courseMonths }} oy
              </dd>
            </div>
            <!--
              Video darslar QAYSI qismdan boshlanishi — guruh-daraja sozlama
              (bir kurs, ko'p guruh: yarim yildan qo'shilgan guruh 1-moduldan
              boshlamaydi). Kurssiz guruhda ma'nosi yo'q, shuning uchun
              chiziqcha ko'rsatiladi.
            -->
            <div class="col-span-2">
              <dt class="text-dim">
                Video darslar boshlanishi
              </dt>
              <dd
                class="mt-0.5 truncate font-medium text-slate-200"
                v-text="group.courseId === null ? '—' : videoStartLabel(group)"
              />
            </div>
            <div class="col-span-2">
              <dt class="text-dim">
                Muddat
              </dt>
              <dd class="mt-0.5 font-medium tabular-nums text-slate-200">
                {{ formatDateWithYear(group.startDate) }} — {{ formatDateWithYear(group.endDate) }}
              </dd>
            </div>
            <div>
              <dt class="text-dim">
                O‘quvchilar
              </dt>
              <dd
                class="mt-0.5 font-medium tabular-nums text-slate-200"
                v-text="group.memberCount"
              />
            </div>
            <div>
              <dt class="text-dim">
                Darslar
              </dt>
              <dd
                class="mt-0.5 font-medium tabular-nums text-slate-200"
                v-text="group.sessionCount"
              />
            </div>
          </dl>
        </BaseCard>

        <UpNextBanner :group-id="groupId" />

        <!-- ============================== TABLAR ============================ -->
        <GroupTabs
          v-model="activeTab"
          :tabs="tabs"
        />

        <AttendanceTab
          v-if="activeTab === 'att'"
          :group-id="groupId"
          :group-name="groupDisplayName(group)"
        />
        <GradesTab
          v-else-if="activeTab === 'grades'"
          :group-id="groupId"
          :group-name="groupDisplayName(group)"
        />
        <LessonsTab
          v-else-if="activeTab === 'lessons'"
          :group-id="groupId"
        />
        <TasksTab
          v-else-if="activeTab === 'tasks'"
          :group-id="groupId"
          :student-count="group.memberCount"
          @grade="grading = $event"
          @reopen="reopening = $event"
        />
        <TestsTab v-else-if="activeTab === 'tests'" />
        <BoardTab
          v-else-if="activeTab === 'board'"
          :group-id="groupId"
        />
        <GroupMembersPanel
          v-else-if="activeTab === 'students'"
          :group-id="groupId"
          :can-manage="canManage"
          @open-profile="openStudentProfile"
          @open-wallet="openStudentWallet"
        />
        <!--
          "Chat" tabi — guruhning DOIMIY umumiy chati. Ilgari bu yerda
          "v2 da bunday chat yo'q" degan placeholder turardi; endi server
          tomonida ham jadval, ham `/api/v1/group-chat` bor.

          Kanal BERILMAYDI (`channel` prop'i yo'q): server xodimga qaysi oqim
          tegishli bo'lsa o'shanisini o'zi tanlaydi — ustozga `Teacher`,
          kuratorga `Curator`. Klient bu tanlovni TAKRORLAMAYDI, aks holda
          ikki joyda ikki xil qoida paydo bo'lardi.
        -->
        <GroupChatRoom
          v-else-if="activeTab === 'chat'"
          :group-id="groupId"
          :group-name="groupDisplayName(group)"
        />
        <!--
          "Yozuvlar" — eski `academic.html` dagi guruh ichidagi `#t-recordings`
          tabi (663–674-qatorlar). Guruh OLDINDAN tanlangani uchun widget'ga
          `fixedGroupId` beriladi va guruh tanlagichi chizilmaydi — eski
          ilovada ham bu tabda faqat qidiruv bo'lgan.
        -->
        <RecordingBoard
          v-else
          :fixed-group-id="groupId"
        />
      </template>
    </DataStatus>

    <GradeDialog
      :submission="grading?.submission ?? null"
      :max-score="grading?.maxScore ?? 0"
      @close="grading = null"
      @graded="
        () => {
          grading = null
          refreshSubmissions()
        }
      "
    />

    <ReopenDialog
      :submission="reopening"
      @close="reopening = null"
      @reopened="refreshSubmissions"
    />

    <!--
      Qayta tuzish tasdiqlanadi: amal o'nlab kelajakdagi darsni almashtiradi.
      Matnda NIMA saqlanishi aniq aytiladi — aks holda foydalanuvchi davomat
      tarixidan qo'rqib, kerak bo'lganda ham bosmaydi.
    -->
    <ConfirmDeleteDialog
      :open="regenerateOpen"
      title="Jadvalni qayta tuzish"
      message="Kelajakdagi rejalashtirilgan darslar guruhning joriy jadvali bo‘yicha qayta yaratiladi. O‘tgan, jonli, yakunlangan va bekor qilingan darslarga tegilmaydi."
      confirm-label="Qayta tuzish"
      :pending="regenerateMutation.isPending.value"
      :error="regenerateError"
      @close="regenerateOpen = false"
      @confirm="regenerateMutation.mutate()"
    />

    <!--
      O'QUVCHI PROFILI PANELI — ENG OXIRIDA e'lon qilinadi.

      🔴 TARTIB MUHIM: teleport langarlari komponentlar E'LON QILINGAN
      tartibda yaratiladi va hammasi `z-50` da turadi. Panel ekranning 85% ini
      egallaydi, ya'ni u yuqoridagi tasdiq oynalarining ORTIDA qolmasligi
      kerak (`ManageUsersPage` da ayni sabab bilan ayni tartib).

      `@changed` — Telegram uzilganda a'zolar ro'yxatidagi ma'lumot ham
      eskiradi, shuning uchun ro'yxat qaytadan so'raladi.
    -->
    <StudentProfileDrawer
      :open="studentProfileOpen"
      :user-id="studentProfileId"
      @close="studentProfileOpen = false"
      @changed="refreshGroup"
    />
  </div>
</template>
