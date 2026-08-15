<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups, GROUP_SEARCH_MIN, groupDisplayName } from '@/entities/group'
import {
  activateUser,
  deactivateUser,
  fetchUsers,
  ROLE_OPTIONS,
  roleLabel,
  roleTone,
  TELEGRAM_FILTER_OPTIONS,
  telegramFilterToParam,
  USER_SEARCH_MIN,
} from '@/entities/user'
import type { TelegramFilterValue } from '@/entities/user'
import StudentProfileDrawer from '@/features/student-profile/ui/StudentProfileDrawer.vue'
import UserFormDialog from '@/features/user-form/ui/UserFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatPhone } from '@/shared/lib/phone'
import { useDebounced } from '@/shared/lib/debounce'
import { formatDateTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { UserDetailsDto, UserRoleName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  IconButton,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/**
 * Foydalanuvchilar CRM'i (Academic/Admin).
 *
 * Bazada 1500+ foydalanuvchi bor — shuning uchun qidiruv SERVERDA, sahifalash
 * ham serverda. Qidiruv kechiktiriladi (har harfda so'rov yuborilmasin).
 *
 * ★ QATORGA BOSILSA PROFIL PANELI ochiladi (talab: *"har bir o'quvchi ustiga
 * bosilganda"*). Qator klaviatura bilan ham ishlaydi (`role="button"`,
 * `tabindex="0"`, Enter/Space) — aks holda amal faqat sichqonchaga bog'lanib,
 * klaviatura foydalanuvchisi profilga UMUMAN kira olmasdi.
 *
 * 🔴 Qator ichidagi amal tugmalarida `@click.stop` MAJBURIY: aks holda
 * "Tahrirlash" bosilganda hodisa qatorga ko'tarilib, forma bilan BIRGA profil
 * paneli ham ochilardi (ikki qatlam, foydalanuvchi yo'qoladi).
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

/*
  Kartochka ↔ jadval almashuvi CSS emas, `v-if` bilan: `hidden lg:block`
  IKKALA daraxtni ham quradi — telefon hech qachon ko'rmaydigan 8 ustunli
  jadval ham mount bo'lib, ma'lumot bilan to'lardi.

  ★ Chegara `lg` (1024px), `md` EMAS — yon menyu ham shu yerda ochiladi.
  iPad tik holati (768px) endi kartochka + gamburger bo'lib qoladi
  (`style.css` dagi "md va lg haqidagi asosiy qaror" izohiga qarang).
*/
const { isDesktop } = useBreakpoint()

const search = ref('')
const debouncedSearch = useDebounced(search)
const roleFilter = ref<UserRoleName | ''>('')
const activeFilter = ref<'' | 'true' | 'false'>('')
const telegramFilter = ref<TelegramFilterValue>('')
const page = ref(1)

const PAGE_SIZE = 20

/*
  ★ QIDIRUVNING MINIMAL UZUNLIGI serverda tekshiriladi
  (`UserService.MinSearchLength = 3`) va qisqa satr 400 bilan qaytadi.
  Ilgari bir harf yozilishi bilanoq so'rov ketardi: `DataStatus` xato holatiga
  o'tib JADVAL BUTUNLAY YO'QOLARDI va o'rniga qizil banner chiqardi — 3-harfda
  o'ziga kelsa ham, oraliqda interfeys buzilgan ko'rinardi. Endi qisqa satr
  UMUMAN yuborilmaydi, o'rniga maydon ostida nima kutilayotgani aytiladi.
*/
const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < USER_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= USER_SEARCH_MIN ? searchTerm.value : undefined,
)

/* ------------------------------------------------ guruh bo'yicha filtr --- */

/*
  GURUH TANLAGICHI: qidiruv maydoni + `select`.

  ★ TO'LIQ RO'YXAT YUKLANMAYDI: guruhlar ko'p va ularni bitta `select` ga
  solish telefonda ochilmaydigan ro'yxat berardi. Server qidiruvi ishlatiladi
  (minimal 2 belgi — `GroupService.MinSearchLength`, foydalanuvchi qidiruvidan
  BOSHQA), qidiruvsiz holatda esa faqat birinchi 25 faol guruh ko'rinadi.
  Ayni naqsh `features/assignment-form/ui/AssignmentTargetPicker.vue` da.
*/
const groupSearch = ref('')
const debouncedGroupSearch = useDebounced(groupSearch)

const groupTerm = computed(() => debouncedGroupSearch.value.trim())
const groupSearchTooShort = computed(
  () => groupTerm.value.length > 0 && groupTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveGroupSearch = computed(() =>
  groupTerm.value.length >= GROUP_SEARCH_MIN ? groupTerm.value : undefined,
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'user-filter', effectiveGroupSearch],
  queryFn: ({ signal }) =>
    fetchGroups({ search: effectiveGroupSearch.value, isActive: true, pageSize: 25 }, { signal }),
})

/**
 * Tanlangan guruh — Id VA nomi bilan saqlanadi.
 *
 * ★ NEGA NOM HAM: xodim guruhni tanlab, keyin qidiruvni o'zgartirsa tanlangan
 * guruh natijalar ro'yxatidan chiqib ketadi. Faqat Id saqlansak `select` bo'sh
 * ko'rinardi, filtr esa AMALDA ishlab turardi — "ro'yxat nega qisqa?" degan
 * chalkashlik. Nom saqlangani uchun tanlov ro'yxatga qaytariladi (pastda).
 */
const groupFilter = ref<{ id: number; name: string } | null>(null)

const groupOptions = computed(() => {
  const list = (groupsQuery.data.value?.items ?? []).map((group) => ({
    id: group.id,
    name: groupDisplayName(group),
  }))
  const picked = groupFilter.value
  if (picked !== null && !list.some((option) => option.id === picked.id)) {
    return [picked, ...list]
  }
  return list
})

function onGroupFilterChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  if (value.length === 0) {
    groupFilter.value = null
    return
  }
  const id = Number(value)
  groupFilter.value = groupOptions.value.find((option) => option.id === id) ?? {
    id,
    name: `Guruh #${id}`,
  }
}

/* --------------------------------------------------------------- ro'yxat -- */

// Filtr o'zgarsa 1-sahifaga qaytamiz, aks holda "10-sahifada natija yo'q" holati chiqadi.
watch([effectiveSearch, roleFilter, activeFilter, groupFilter, telegramFilter], () => {
  page.value = 1
})

const usersQuery = useQuery({
  queryKey: [
    'users',
    'list',
    effectiveSearch,
    roleFilter,
    activeFilter,
    groupFilter,
    telegramFilter,
    page,
  ],
  queryFn: ({ signal }) =>
    fetchUsers(
      {
        search: effectiveSearch.value,
        role: roleFilter.value === '' ? undefined : roleFilter.value,
        isActive: activeFilter.value === '' ? undefined : activeFilter.value === 'true',
        groupId: groupFilter.value?.id,
        telegramLinked: telegramFilterToParam(telegramFilter.value),
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

const users = computed(() => usersQuery.data.value?.items ?? [])
const total = computed(() => usersQuery.data.value?.total ?? 0)
const totalPages = computed(() => usersQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  usersQuery.error.value !== null ? toUserMessage(usersQuery.error.value) : null,
)

/* --------------------------- yaratish/tahrirlash --------------------------- */

const dialogOpen = ref(false)
const editing = ref<UserDetailsDto | null>(null)

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(user: UserDetailsDto): void {
  editing.value = user
  dialogOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['users'] })
}

/* ------------------------------------------------------------- profil --- */

const profileOpen = ref(false)
const profileUserId = ref<number | null>(null)
const profileName = ref('')

function openProfile(user: UserDetailsDto): void {
  profileUserId.value = user.id
  profileName.value = user.fullName ?? ''
  profileOpen.value = true
}

/* ----------------------------------------------------- bloklash/tiklash --- */

/**
 * Qaysi qator kutayotgani.
 *
 * ★ NEGA ALOHIDA REF: `toggleMutation.isPending` BITTA mutatsiya uchun
 * umumiy — ilgari u har qatordagi tugmani birdan yuklanish holatiga
 * o'tkazardi ("hammasi bosildi" degan taassurot). Endi loader FAQAT bosilgan
 * qatorda ko'rinadi.
 */
const togglingId = ref<number | null>(null)

const toggleMutation = useMutation({
  mutationFn: (user: UserDetailsDto) =>
    user.isActive ? deactivateUser(user.id) : activateUser(user.id),
  onSuccess: refresh,
  onSettled: () => {
    togglingId.value = null
  },
})

/**
 * Bloklash — QAYTARILADIGAN, lekin foydalanuvchini tizimdan CHIQARADIGAN amal
 * (`danger` tasdiq, B2 jadvali). Faollashtirish ham ma'lumotni almashtiradi,
 * shuning uchun u ham tasdiqlanadi — lekin `primary` tonda.
 */
async function toggleActive(user: UserDetailsDto): Promise<void> {
  if (toggleMutation.isPending.value) return

  const name = user.fullName ?? 'Foydalanuvchi'
  const ok = user.isActive
    ? await confirm({
      title: 'Hisobni bloklash',
      message: `${name} platformaga kira olmaydi. Ma'lumotlari va tarixi saqlanadi.`,
      confirmLabel: 'Bloklash',
      tone: 'danger',
      details: ['Mavjud sessiyalari bekor qilinadi.'],
    })
    : await confirm({
      title: 'Hisobni faollashtirish',
      message: `${name} yana platformaga kira oladi.`,
      confirmLabel: 'Faollashtirish',
      tone: 'primary',
    })
  if (!ok) return

  togglingId.value = user.id
  toggleMutation.mutate(user)
}

/** Telegram ustuni: nom bo'lmasa ham ulanish holati ko'rinishi kerak. */
function telegramText(user: UserDetailsDto): string {
  if (user.telegramId === null) return '—'
  return user.telegramUsername === null ? 'Ulangan' : `@${user.telegramUsername}`
}
</script>

<template>
  <div>
    <PageHeader
      title="Foydalanuvchilar"
      :subtitle="`Jami: ${total} ta hisob`"
    >
      <template #actions>
        <BaseButton @click="openCreate">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi
        </BaseButton>
      </template>
    </PageHeader>

    <!-- Filtrlar: telefonda ustun, sm dan boshlab yonma-yon -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
      <div class="relative sm:col-span-2 lg:col-span-2">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Ism yoki email bo‘yicha qidirish"
        >
        <p
          v-if="searchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Qidirish uchun kamida {{ USER_SEARCH_MIN }} belgi kiriting.
        </p>
      </div>
      <select
        v-model="roleFilter"
        class="zn-input"
        aria-label="Rol bo‘yicha filtr"
      >
        <option value="">
          Barcha rollar
        </option>
        <option
          v-for="option in ROLE_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>
      <select
        v-model="activeFilter"
        class="zn-input"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option value="true">
          Faol
        </option>
        <option value="false">
          Bloklangan
        </option>
      </select>

      <!-- Guruh qidiruvi (BLOK F) -->
      <div>
        <input
          v-model="groupSearch"
          class="zn-input"
          placeholder="Guruhni qidirish"
          aria-label="Guruhni qidirish"
        >
        <p
          v-if="groupSearchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Kamida {{ GROUP_SEARCH_MIN }} belgi kiriting.
        </p>
      </div>

      <!-- Guruh bo'yicha filtr -->
      <div>
        <select
          class="zn-input"
          aria-label="Guruh bo‘yicha filtr"
          :value="groupFilter?.id ?? ''"
          @change="onGroupFilterChange"
        >
          <option value="">
            Barcha guruhlar
          </option>
          <option
            v-for="option in groupOptions"
            :key="option.id"
            :value="option.id"
          >
            {{ option.name }}
          </option>
        </select>
        <!--
          ⚠️ SHART AYTILISHI KERAK: server guruh bo'yicha faqat `Active`
          a'zolarni qaytaradi. Aks holda xodim chiqarilgan yoki pauzadagi
          o'quvchini "yo'qolgan" deb o'ylardi va uni qaytadan qo'shishga
          urinardi.
        -->
        <p
          v-if="groupFilter !== null"
          class="mt-1 text-[11px] text-dim"
        >
          Faqat FAOL a‘zolar ko‘rsatiladi (chiqarilgan, pauzadagi va
          ko‘chirilganlar kirmaydi).
        </p>
      </div>

      <!-- Telegram bo'yicha filtr (uch holat) -->
      <select
        v-model="telegramFilter"
        class="zn-input"
        aria-label="Telegram bo‘yicha filtr"
      >
        <option
          v-for="option in TELEGRAM_FILTER_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>
    </div>

    <DataStatus
      :pending="usersQuery.isPending.value"
      :error="errorMessage"
      :empty="users.length === 0"
      :retrying="usersQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="users"
      empty-title="Foydalanuvchi topilmadi"
      empty-text="Qidiruv shartlarini o‘zgartirib ko‘ring."
      @retry="usersQuery.refetch()"
    >
      <BaseCard flush>
        <!-- Telefon/planshet: kartochka ro'yxati -->
        <ul
          v-if="!isDesktop"
          class="divide-y divide-line"
        >
          <li
            v-for="user in users"
            :key="user.id"
            class="cursor-pointer p-3.5 transition-colors hover:bg-ink-800"
            role="button"
            tabindex="0"
            :aria-label="`${user.fullName ?? 'Foydalanuvchi'} profilini ochish`"
            @click="openProfile(user)"
            @keydown.enter.prevent="openProfile(user)"
            @keydown.space.prevent="openProfile(user)"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="user.fullName ?? '—'"
              />
              <BaseBadge :tone="roleTone(user.role ?? '')">
                {{ roleLabel(user.role ?? '—') }}
              </BaseBadge>
            </div>
            <p
              class="mt-1 truncate text-xs text-slate-400"
              v-text="user.email ?? '—'"
            />
            <p
              v-if="user.phone !== null"
              class="text-xs text-dim"
              v-text="formatPhone(user.phone)"
            />
            <div class="mt-2.5 flex flex-wrap items-center gap-2">
              <BaseBadge :tone="user.isActive ? 'success' : 'danger'">
                {{ user.isActive ? 'Faol' : 'Bloklangan' }}
              </BaseBadge>
              <BaseBadge :tone="user.telegramId === null ? 'neutral' : 'accent'">
                {{ user.telegramId === null ? 'Telegram yo‘q' : 'Telegram' }}
              </BaseBadge>
              <span class="flex-1" />
              <!--
                🔴 `gap-3` — `IconButton` ning ko'rinmas teginish maydoni har
                tomondan 6px kengayadi (24-tuzoq): kichikroq oraliqda barmoq
                yonidagi tugmani bosardi.
                🔴 `@click.stop` — qatorning profil ochish hodisasi ishga
                tushmasin.
              -->
              <div class="flex items-center gap-3">
                <IconButton
                  icon="edit"
                  label="Tahrirlash"
                  @click.stop="openEdit(user)"
                />
                <IconButton
                  :icon="user.isActive ? 'lock' : 'user-check'"
                  :label="user.isActive ? 'Bloklash' : 'Faollashtirish'"
                  :tone="user.isActive ? 'danger' : 'success'"
                  :loading="toggleMutation.isPending.value && togglingId === user.id"
                  @click.stop="toggleActive(user)"
                />
              </div>
            </div>
          </li>
        </ul>

        <!-- Desktop (≥1024px): jadval. Gorizontal skroll SHU konteynerda. -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>Ism</th>
                <th>Email</th>
                <th>Telefon</th>
                <th>Telegram</th>
                <th>Rol</th>
                <th>Holat</th>
                <th>Qo‘shilgan</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="user in users"
                :key="user.id"
                class="cursor-pointer"
                role="button"
                tabindex="0"
                :aria-label="`${user.fullName ?? 'Foydalanuvchi'} profilini ochish`"
                @click="openProfile(user)"
                @keydown.enter.prevent="openProfile(user)"
                @keydown.space.prevent="openProfile(user)"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="user.fullName ?? '—'"
                />
                <td
                  class="text-slate-400"
                  v-text="user.email ?? '—'"
                />
                <td
                  class="text-slate-400"
                  v-text="formatPhone(user.phone) || '—'"
                />
                <!--
                  🔴 Telegram nomi FAQAT ko'rsatish uchun: bo'shatilgan nom
                  boshqa odamga o'tadi, ya'ni u shaxsni ANIQLAMAYDI (profil
                  panelida `telegramId` ham beriladi).
                -->
                <td
                  class="text-slate-400"
                  v-text="telegramText(user)"
                />
                <td>
                  <BaseBadge :tone="roleTone(user.role ?? '')">
                    {{ roleLabel(user.role ?? '—') }}
                  </BaseBadge>
                </td>
                <td>
                  <BaseBadge :tone="user.isActive ? 'success' : 'danger'">
                    {{ user.isActive ? 'Faol' : 'Bloklangan' }}
                  </BaseBadge>
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTime(user.createdAt)"
                />
                <td>
                  <div class="flex items-center justify-end gap-3">
                    <IconButton
                      icon="edit"
                      label="Tahrirlash"
                      size="sm"
                      @click.stop="openEdit(user)"
                    />
                    <IconButton
                      :icon="user.isActive ? 'lock' : 'user-check'"
                      :label="user.isActive ? 'Bloklash' : 'Faollashtirish'"
                      :tone="user.isActive ? 'danger' : 'success'"
                      size="sm"
                      :loading="toggleMutation.isPending.value && togglingId === user.id"
                      @click.stop="toggleActive(user)"
                    />
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </BaseCard>
    </DataStatus>

    <UserFormDialog
      :open="dialogOpen"
      :user="editing"
      @close="dialogOpen = false"
      @saved="refresh"
    />

    <!--
      ★ PROFIL PANELI FORMADAN KEYIN e'lon qilinadi: `Teleport to="body"`
      langarlari komponentlar E'LON QILINGAN tartibda yaratiladi va hammasi
      `z-50` da turadi, ya'ni keyingisi ustiga chiqadi. Ikkisi bir vaqtda
      ochilmaydi (forma qator tugmasidan, panel qatorning o'zidan), lekin
      tartib ATAYLAB shunday — panel ekranning 85% ini egallaydi va u ostda
      qolib qolmasligi kerak.
    -->
    <StudentProfileDrawer
      :open="profileOpen"
      :user-id="profileUserId"
      :fallback-name="profileName"
      @close="profileOpen = false"
      @changed="refresh"
    />
  </div>
</template>
