<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch, watchEffect } from 'vue'

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
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
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
  SearchSelect,
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

/* --------------------------------------------------------------- ro'yxat -- */

// Filtr o'zgarsa 1-sahifaga qaytamiz, aks holda "10-sahifada natija yo'q" holati chiqadi.
watch([effectiveSearch, roleFilter, activeFilter, groupFilter, telegramFilter], () => {
  page.value = 1
  selectedIds.value = new Set()
})

/*
  ★ TANLOV SAHIFA O'ZGARGANDA HAM TOZALANADI (yuqoridagi filtr tinglovchisidan
  ALOHIDA): u faqat FILTR o'zgarganda ishga tushadi, `PaginationBar` orqali
  sahifa qo'lda almashtirilganda emas. Tanlov ATAYLAB "hozir ko'rinib turgan
  qatorlar" bilan cheklangan (talab: *"ko'rinib turgan foydalanuvchilarni
  o'zini all select"*) — boshqa sahifadagi ID qolib ketsa, "hammasini tanlash"
  bosilganda ko'rinmas qatorlarga ham amal tegib qolardi.
*/
watch(page, () => {
  selectedIds.value = new Set()
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

/* ------------------------------------------------------------- tanlov --- */

/**
 * Ko'p tanlash — FAQAT joriy sahifadagi qatorlar bo'yicha (yuqoridagi
 * `watch(page, ...)` va filtr tinglovchisi izohiga qarang).
 *
 * ★ `Set`, MASSIV EMAS: `has`/`toggle` amallari qatorlar soniga
 * bog'liqmasdan tez ishlashi kerak (jadval 20 qator, lekin naqsh boshqa
 * joyda ko'proq qatorga ko'chirilishi mumkin). Vue `Set` ichidagi
 * o'zgarishni KUZATMAYDI — shuning uchun har o'zgarishda YANGI `Set`
 * yaratiladi (`selectedIds.value = new Set(...)`), mavjudini `.add`/`.delete`
 * qilib qo'yish reaktivlikni ishga tushirmasdi.
 */
const selectedIds = ref<Set<number>>(new Set())

/**
 * 🔴 TANLASH REJIMI DEFAULT HOLATDA O'CHIRILGAN (loyiha egasi, 2026-08-16:
 * *"hamma foydalanuvchilar default holatda select unable bo'lishi kerak.
 * birgina button bilan barchasini select qilish imkoni ochilishi kerak"*).
 *
 * ★ ILGARI FAQAT O'QUVCHI qatori o'chirilgan edi (rol bo'yicha), ENDI
 * BUTUN JADVAL — checkbox'lar UMUMAN chizilmaydi, toifasidan qat'i nazar,
 * toifa `toggleSelectionMode` bosilmaguncha. Bu ATAYLAB IKKI BOSQICHLI:
 * xodim avval "tanlashni yoqish"ni ANIQ bosishi kerak, keyingina qatorlar
 * tanlanadigan bo'ladi — tasodifan checkbox bosib ketish yo'li BUTUNLAY
 * yo'q qilingan.
 */
const selectionEnabled = ref(false)

function toggleSelectionMode(): void {
  selectionEnabled.value = !selectionEnabled.value
  // O'CHIRILGANDA TOZALANADI: yashiringan checkbox ostida "unutilgan"
  // tanlov qolib, keyingi safar yoqilganda kutilmagan qatorlar tanlangan
  // holatda chiqib qolmasin.
  if (!selectionEnabled.value) selectedIds.value = new Set()
}

function toggleSelected(user: UserDetailsDto): void {
  if (!selectionEnabled.value) return
  const next = new Set(selectedIds.value)
  if (next.has(user.id)) next.delete(user.id)
  else next.add(user.id)
  selectedIds.value = next
}

/** Joriy sahifadagi HAMMASI tanlanganmi — sarlavha katakchasi shunga qarab to'ladi. */
const allVisibleSelected = computed(
  () => users.value.length > 0 && users.value.every((user) => selectedIds.value.has(user.id)),
)

/** Ba'zisi (hammasi emas) tanlangan — sarlavha katakchasi "oraliq" holatga o'tadi. */
const someVisibleSelected = computed(
  () => !allVisibleSelected.value && users.value.some((user) => selectedIds.value.has(user.id)),
)

/**
 * Sarlavha katakchasi: "hammasini tanlash" / "joriy sahifadan bekor qilish".
 *
 * ⚠️ FAQAT joriy `users.value` bilan ishlaydi — boshqa sahifadan qolgan ID
 * bo'lishi mumkin emas (tanlov sahifa almashganda tozalanadi), shuning uchun
 * bekor qilishda butun `selectedIds` ni tozalash yetarli.
 */
function toggleSelectAllVisible(): void {
  if (!selectionEnabled.value) return
  if (allVisibleSelected.value) {
    selectedIds.value = new Set()
    return
  }
  selectedIds.value = new Set(users.value.map((user) => user.id))
}

/**
 * Sarlavha katakchasining "oraliq" (`indeterminate`) holati — bu DOM
 * xossasi, Vue uni oddiy atribut sifatida bog'lay olmaydi (`:indeterminate`
 * yozib bo'lmaydi), shuning uchun shablon `ref` orqali qo'lda o'rnatiladi.
 */
const selectAllCheckboxEl = ref<HTMLInputElement | null>(null)
watchEffect(() => {
  if (selectAllCheckboxEl.value !== null) {
    selectAllCheckboxEl.value.indeterminate = someVisibleSelected.value
  }
})

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

/* --------------------------------------------------- ko'p tanlab amal --- */

/**
 * ★ NEGA "O'CHIRISH"/"ARXIVLASH" YO'Q: backendda foydalanuvchini haqiqiy
 * o'chirish yoki arxivlash imkoniyati UMUMAN yo'q — faqat
 * `POST /users/{id}/activate` va `.../deactivate` bor
 * (`UsersController`). Loyiha egasi bilan aniqlashtirildi: ko'p tanlab
 * "o'chirish" so'ralganda ham buni YANGI, qaytarib bo'lmaydigan backend
 * imkoniyati sifatida qo'shish o'rniga, mavjud bloklash/faollashtirish
 * amali ishlatiladi — nomi ham shu haqiqatni aytadi ("Bloklash", "O'chirish"
 * EMAS), aks holda tugma va'da qilgan narsani bajarmagan bo'lardi.
 */
const bulkPending = ref(false)

async function bulkSetActive(active: boolean): Promise<void> {
  if (bulkPending.value) return

  // Faqat HOLATI ALLAQACHON MOS KELMAGANLARI so'rov yuboradi — 20 tadan
  // hammasi faol bo'lsa, "Faollashtirish" bosilganda 20 ta keraksiz so'rov
  // ketmasin.
  const targets = users.value.filter(
    (user) => selectedIds.value.has(user.id) && user.isActive !== active,
  )

  if (targets.length === 0) {
    selectedIds.value = new Set()
    return
  }

  const ok = active
    ? await confirm({
      title: 'Tanlanganlarni faollashtirish',
      message: `${targets.length} ta hisob yana platformaga kira oladi.`,
      confirmLabel: 'Faollashtirish',
      tone: 'primary',
    })
    : await confirm({
      title: 'Tanlanganlarni bloklash',
      message: `${targets.length} ta hisob platformaga kira olmaydi. Ma'lumotlari va tarixi saqlanadi.`,
      confirmLabel: 'Bloklash',
      tone: 'danger',
      details: ['Mavjud sessiyalari bekor qilinadi.'],
    })
  if (!ok) return

  bulkPending.value = true
  const results = await Promise.allSettled(
    targets.map((user) => (active ? activateUser(user.id) : deactivateUser(user.id))),
  )
  bulkPending.value = false

  const failed = results.filter((result) => result.status === 'rejected').length
  const succeeded = results.length - failed

  refresh()
  selectedIds.value = new Set()

  if (failed === 0) {
    showToast(
      active ? `${succeeded} ta hisob faollashtirildi` : `${succeeded} ta hisob bloklandi`,
    )
  } else if (succeeded === 0) {
    showToast(`${failed} ta hisobda amal bajarilmadi`, 'error')
  } else {
    showToast(`${succeeded} tasi bajarildi, ${failed} tasida xato bo‘ldi`, 'warning')
  }
}
</script>

<template>
  <div>
    <PageHeader
      title="Foydalanuvchilar"
      :subtitle="`Jami: ${total} ta hisob`"
    >
      <template #actions>
        <!--
          "Tanlashni yoqish/yopish" (loyiha egasi, 2026-08-16) — checkbox'lar
          FAQAT bu tugma bosilgandan keyin paydo bo'ladi (yuqoridagi
          `selectionEnabled` izohi).
        -->
        <BaseButton
          :variant="selectionEnabled ? 'primary' : 'secondary'"
          @click="toggleSelectionMode"
        >
          <template #icon>
            <AppIcon
              name="check-square"
              :size="16"
            />
          </template>
          {{ selectionEnabled ? 'Tanlashni yopish' : 'Tanlash' }}
        </BaseButton>
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

      <!--
        Guruh bo'yicha filtr — QIDIRUV VA TANLOV BITTA maydonda (BLOK F).
        🔴 ILGARI IKKI ALOHIDA ELEMENT edi (matn maydoni + undan pastdagi
        `<select>`) — loyiha egasi buni "qidiruv ishlamayapti" deb topdi:
        yozganda EKRANDA hech narsa o'zgarmasdi, natijani ko'rish uchun
        IKKINCHI elementni qo'lda ochish kerak edi. `SearchSelect` buni
        BITTA maydonga birlashtiradi (izohi `shared/ui/SearchSelect.vue`).
      -->
      <div>
        <SearchSelect
          v-model="groupFilter"
          :search="groupSearch"
          :options="groupOptions"
          :loading="groupsQuery.isFetching.value"
          placeholder="Guruhni qidirish"
          empty-label="Barcha guruhlar"
          label="Guruh bo‘yicha filtr"
          @update:search="groupSearch = $event"
        />
        <p
          v-if="groupSearchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Kamida {{ GROUP_SEARCH_MIN }} belgi kiriting.
        </p>
        <!--
          ⚠️ SHART AYTILISHI KERAK: server guruh bo'yicha faqat `Active`
          a'zolarni qaytaradi. Aks holda xodim chiqarilgan yoki pauzadagi
          o'quvchini "yo'qolgan" deb o'ylardi va uni qaytadan qo'shishga
          urinardi.
        -->
        <p
          v-else-if="groupFilter !== null"
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

    <!--
      Ko'p tanlab amal paneli — FAQAT biror qator tanlanganda ko'rinadi.
      Filtr panjarasi va ro'yxat orasida turadi: joylashuv "hozir nima
      tanlangan" savolini ro'yxatga eng yaqin joyda javob beradi.
    -->
    <div
      v-if="selectedIds.size > 0"
      class="mb-4 flex flex-wrap items-center gap-2.5 rounded-xl border border-line bg-ink-900 p-3"
    >
      <span
        class="text-sm font-medium text-slate-200"
        v-text="`${selectedIds.size} ta tanlandi`"
      />
      <span class="flex-1" />
      <BaseButton
        size="sm"
        variant="ghost"
        :disabled="bulkPending"
        @click="selectedIds = new Set()"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        size="sm"
        variant="secondary"
        :loading="bulkPending"
        @click="bulkSetActive(true)"
      >
        <template #icon>
          <AppIcon
            name="user-check"
            :size="13"
          />
        </template>
        Tanlanganlarni faollashtirish
      </BaseButton>
      <BaseButton
        size="sm"
        variant="danger"
        :loading="bulkPending"
        @click="bulkSetActive(false)"
      >
        <template #icon>
          <AppIcon
            name="lock"
            :size="13"
          />
        </template>
        Tanlanganlarni bloklash
      </BaseButton>
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
            v-for="(user, index) in users"
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
              <div class="flex min-w-0 flex-1 items-center gap-2.5">
                <!--
                  ⚠️ `@click.stop` — qatorning profil ochish hodisasi ishga
                  tushmasin. `v-if="selectionEnabled"` — checkbox "Tanlash"
                  tugmasi bosilmaguncha UMUMAN chizilmaydi (yuqoridagi
                  `selectionEnabled` izohi).
                -->
                <input
                  v-if="selectionEnabled"
                  type="checkbox"
                  class="size-4 shrink-0 accent-brand-500"
                  :checked="selectedIds.has(user.id)"
                  aria-label="Tanlash"
                  @click.stop
                  @change="toggleSelected(user)"
                >
                <span
                  class="shrink-0 tabular-nums text-xs text-dim"
                  v-text="(page - 1) * PAGE_SIZE + index + 1"
                />
                <p
                  class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                  v-text="user.fullName ?? '—'"
                />
              </div>
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
                <th
                  v-if="selectionEnabled"
                  class="w-9"
                >
                  <input
                    ref="selectAllCheckboxEl"
                    type="checkbox"
                    class="size-4 accent-brand-500"
                    :checked="allVisibleSelected"
                    aria-label="Hammasini tanlash"
                    @change="toggleSelectAllVisible"
                  >
                </th>
                <th class="w-10">
                  <span class="sr-only">№</span>
                </th>
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
                v-for="(user, index) in users"
                :key="user.id"
                class="cursor-pointer"
                role="button"
                tabindex="0"
                :aria-label="`${user.fullName ?? 'Foydalanuvchi'} profilini ochish`"
                @click="openProfile(user)"
                @keydown.enter.prevent="openProfile(user)"
                @keydown.space.prevent="openProfile(user)"
              >
                <td v-if="selectionEnabled">
                  <input
                    type="checkbox"
                    class="size-4 accent-brand-500"
                    :checked="selectedIds.has(user.id)"
                    aria-label="Tanlash"
                    @click.stop
                    @change="toggleSelected(user)"
                  >
                </td>
                <!--
                  ★ RAQAM SAHIFA BO'YICHA GLOBAL — `ManageGroupsPage.vue`dagi
                  xuddi shu naqsh: `index + 1` bo'lsa har sahifa "1, 2, 3..."
                  bilan qayta boshlanardi.
                -->
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * PAGE_SIZE + index + 1"
                />
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
                  v-text="formatDateTimeNumeric(user.createdAt)"
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
