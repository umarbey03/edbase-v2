<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  fetchGroups,
  GROUP_SEARCH_MIN,
  groupDisplayName,
  groupScheduleSummary,
  groupTypeLabel,
  groupTypeTone,
} from '@/entities/group'
import { fetchGroupCategories, groupCategoryLabel } from '@/entities/group-category'
import { GroupCategoryManagerDrawer } from '@/features/group-category-manage'
import { GroupEditDrawer } from '@/features/group-form'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { GroupDto, GroupTypeName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/** Guruhlar boshqaruvi (Academic/Admin): qidiruv, tur filtri, yaratish/tahrirlash. */
const router = useRouter()
const queryClient = useQueryClient()

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi (telefonda ko'rinmas jadval ham mount bo'lib, ma'lumot olardi).
  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi,
  ya'ni iPad tik holati (768px) kartochka bo'lib qoladi — `style.css` dagi
  "md va lg haqidagi asosiy qaror" izohiga qarang.
*/
const { isDesktop } = useBreakpoint()

const search = ref('')
const debouncedSearch = useDebounced(search)
const typeFilter = ref<GroupTypeName | ''>('')
const activeFilter = ref<'' | 'true' | 'false'>('')
/*
  R21b · YO'NALISH FILTRI. `''` — "Barcha yo'nalishlar".

  ★ `<select>` qiymati DOIM satr, shuning uchun holat ham satr sifatida
  saqlanadi va so'rovga `Number(...)` bilan uzatiladi. `null` bilan
  aralashtirilsa "hech biri" va "0" holatlari bir-biriga tushardi.
*/
const categoryFilter = ref('')
const page = ref(1)

const PAGE_SIZE = 20

/*
  ★ Server qidiruvda minimal uzunlik talab qiladi
  (`GroupService.MinSearchLength = 2`) va qisqa satrda 400 beradi — bunda
  `DataStatus` xato holatiga o'tib jadval butunlay yo'qolardi. Qisqa satr
  yuborilmaydi; foydalanuvchiga nima kutilayotgani aytiladi.
*/
const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= GROUP_SEARCH_MIN ? searchTerm.value : undefined,
)

watch([effectiveSearch, typeFilter, activeFilter, categoryFilter], () => {
  page.value = 1
})

const groupsQuery = useQuery({
  queryKey: ['groups', 'manage', effectiveSearch, typeFilter, activeFilter, categoryFilter, page],
  queryFn: ({ signal }) =>
    fetchGroups(
      {
        search: effectiveSearch.value,
        type: typeFilter.value === '' ? undefined : typeFilter.value,
        isActive: activeFilter.value === '' ? undefined : activeFilter.value === 'true',
        /*
          🔴 FILTR SERVERGA YUBORILADI, mijozda emas: ro'yxat sahifalangan
          (`PAGE_SIZE = 20`), ya'ni mijozdagi filtr FAQAT joriy sahifani
          ko'rardi va 21-guruh mos kelsa ham "topilmadi" deb yolg'on
          aytardi — qidiruv ham AYNI sabab bilan serverga ko'chirilgan.
        */
        categoryId: categoryFilter.value === '' ? undefined : Number(categoryFilter.value),
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

/*
  Filtr tanlagichi uchun lug'at.

  ★ FAQAT FAOL emas, HAMMASI: arxivlangan yo'nalishdagi guruhlarni topish
  ehtiyoji aynan boshqaruv ekranida bo'ladi ("IELTS ni arxivladik — unda
  nechta guruh qolgan?"). Guruh FORMASIDAGI tanlagich esa aksincha, faqat
  faollarni ko'rsatadi (yangi guruhga arxiv yorlig'i tanlanmasin).
*/
const categoriesQuery = useQuery({
  queryKey: ['group-categories', 'all'],
  queryFn: ({ signal }) => fetchGroupCategories({}, { signal }),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const groups = computed(() => groupsQuery.data.value?.items ?? [])
const total = computed(() => groupsQuery.data.value?.total ?? 0)
const totalPages = computed(() => groupsQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  groupsQuery.error.value !== null ? toUserMessage(groupsQuery.error.value) : null,
)

/*
  ★ PANELGA FAQAT `id` BERILADI, butun `GroupDto` EMAS.

  🔴 Sabab: `GroupEditDrawer` ochilganda `GET /groups/{id}` bilan YANGI
  ma'lumot oladi. Ro'yxatdagi obyekt keshdan kelgan bo'lishi mumkin va
  `PUT` = TO'LIQ ALMASHTIRISH bo'lgani uchun eskirgan qiymatlar payloadga
  tushib, boshqa xodimning o'zgarishini bekor qilardi. Prop sifatida DTO
  berilsa, "keshdan foydalanmang" qoidasi qog'ozda qolib, amalda buzilardi.
*/
const drawerOpen = ref(false)
const editingId = ref<number | null>(null)

/*
  R21b · yo'nalishlar lug'ati paneli.

  ★ BIR VAQTDA FAQAT BITTASI ochiladi va bu MAJBURIY: `BaseDrawer` ichma-ich
  ochilishini taqiqlaydi (`useModalHost` dev'da `console.warn` bilan
  ushlaydi). Ikkalasi bu yerda birodar (sibling) sifatida turadi va guruh
  formasi ochiq bo'lganda lug'at tugmasi ekranda ko'rinmaydi (panel ustidan
  yopadi), ya'ni ular hech qachon ustma-ust tushmaydi.
*/
const categoryManagerOpen = ref(false)

function openCreate(): void {
  editingId.value = null
  drawerOpen.value = true
}

function openEdit(group: GroupDto): void {
  editingId.value = group.id
  drawerOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['groups'] })
  /*
    ★ Guruh TAFSILOTI keshi ham eskiradi: `['group', id]` (guruh sahifasi,
    jadval, a'zolar) — bu boshqa kalit, ya'ni `['groups']` uni QAMRAMAYDI.
    Bo'lim saqlanganidan keyin guruh sahifasiga o'tilsa eski kurs/jadval
    ko'rinib turardi.
  */
  void queryClient.invalidateQueries({ queryKey: ['group'] })
}

function openDetail(groupId: number): void {
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}
</script>

<template>
  <div>
    <PageHeader
      title="Guruhlar"
      :subtitle="`Jami: ${total} ta guruh`"
    >
      <template #actions>
        <!--
          R21b · Yo'nalishlar lug'ati. `secondary` — u ikkilamchi amal:
          asosiy tugma ("Yangi") guruh yaratadi, bu esa uning parametrini
          tayyorlaydi.
        -->
        <BaseButton
          variant="secondary"
          @click="categoryManagerOpen = true"
        >
          <template #icon>
            <AppIcon
              name="grid"
              :size="16"
            />
          </template>
          Yo‘nalishlar
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

    <!--
      ★ SETKA `lg:grid-cols-4` -> `lg:grid-cols-5`: qidiruv ikki katakni
      egallaydi, qolgan UCHTASI — tur, holat va yo'nalish filtrlari.
      `sm` da hammasi ikki ustunga o'raladi (o'zgarmadi).
    -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-5">
      <div class="relative sm:col-span-2">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <!--
          R22: server qidiruvi guruh nomidan tashqari ustoz, kurator,
          kurator guruhi va kurs nomlarini ham qamraydi. Bu ekran AYNI
          endpointdan foydalanadi, ya'ni matn ikkalasida bir xil bo'lishi
          shart — aks holda o'quv bo'limi imkoniyatdan bexabar qolardi.
        -->
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Guruh, ustoz, kurator yoki kurs nomi"
        >
        <p
          v-if="searchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Qidirish uchun kamida {{ GROUP_SEARCH_MIN }} belgi kiriting.
        </p>
      </div>
      <select
        v-model="typeFilter"
        class="zn-input"
        aria-label="Tur bo‘yicha filtr"
      >
        <option value="">
          Barcha turlar
        </option>
        <option value="Group">
          Guruh
        </option>
        <option value="Individual">
          Individual
        </option>
        <option value="Curator">
          Kurator guruhi
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
          Arxiv
        </option>
      </select>
      <!--
        R21b · YO'NALISH filtri. Ro'yxatda ARXIVLANGANLAR ham bor (skriptdagi
        izoh): "IELTS ni arxivladik — unda nechta guruh qolgan?" degan savol
        aynan shu ekranda tug'iladi.
      -->
      <select
        v-model="categoryFilter"
        class="zn-input"
        aria-label="Yo‘nalish bo‘yicha filtr"
      >
        <option value="">
          Barcha yo‘nalishlar
        </option>
        <option
          v-for="category in categories"
          :key="category.id"
          :value="String(category.id)"
        >
          {{ category.isActive ? category.name : `${category.name} (arxiv)` }}
        </option>
      </select>
    </div>

    <DataStatus
      :pending="groupsQuery.isPending.value"
      :error="errorMessage"
      :empty="groups.length === 0"
      :retrying="groupsQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="grid"
      empty-title="Guruh topilmadi"
      empty-text="Qidiruv shartlarini o‘zgartiring yoki yangi guruh yarating."
      @retry="groupsQuery.refetch()"
    >
      <BaseCard flush>
        <!-- Telefon/planshet: kartochka -->
        <ul
          v-if="!isDesktop"
          class="divide-y divide-line"
        >
          <li
            v-for="group in groups"
            :key="group.id"
            class="p-3.5"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="groupDisplayName(group)"
              />
              <BaseBadge :tone="groupTypeTone(group.type)">
                {{ groupTypeLabel(group.type) }}
              </BaseBadge>
            </div>
            <p
              class="mt-1 text-xs text-slate-400"
              v-text="groupScheduleSummary(group)"
            />
            <p class="text-xs text-dim">
              {{ group.memberCount }} o‘quvchi · {{ group.sessionCount }} dars ·
              {{ group.teacherName ?? 'ustoz yo‘q' }}
            </p>
            <!--
              R21b · yo'nalish. Kartochkada ALOHIDA qatorda, yuqoridagi
              "· " zanjiriga qo'shilmagan: u allaqachon uch bo'lakli va
              to'rtinchisi telefonda o'ralib ketardi.
            -->
            <p
              v-if="group.categoryName !== null"
              class="text-xs text-dim"
              v-text="groupCategoryLabel(group)"
            />
            <div class="mt-2.5 flex flex-wrap items-center gap-2">
              <BaseBadge :tone="group.isActive ? 'success' : 'neutral'">
                {{ group.isActive ? 'Faol' : 'Arxiv' }}
              </BaseBadge>
              <span class="flex-1" />
              <BaseButton
                size="sm"
                variant="secondary"
                @click="openDetail(group.id)"
              >
                Ochish
              </BaseButton>
              <BaseButton
                size="sm"
                @click="openEdit(group)"
              >
                <template #icon>
                  <AppIcon
                    name="edit"
                    :size="13"
                  />
                </template>
                Tahrirlash
              </BaseButton>
            </div>
          </li>
        </ul>

        <!-- Desktop (≥1024px): jadval -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>Nomi</th>
                <th>Turi</th>
                <!-- R21b · yo'nalish TURDAN keyin: ikkalasi ham "bu qanday guruh" savoliga javob. -->
                <th>Yo‘nalish</th>
                <th>Jadval</th>
                <th>Ustoz</th>
                <th>O‘quvchi</th>
                <th>Muddat</th>
                <th>Holat</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="group in groups"
                :key="group.id"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="groupDisplayName(group)"
                />
                <td>
                  <BaseBadge :tone="groupTypeTone(group.type)">
                    {{ groupTypeLabel(group.type) }}
                  </BaseBadge>
                </td>
                <td
                  class="text-slate-400"
                  v-text="groupCategoryLabel(group)"
                />
                <td
                  class="text-slate-400"
                  v-text="groupScheduleSummary(group)"
                />
                <td
                  class="text-slate-400"
                  v-text="group.teacherName ?? '—'"
                />
                <td class="tabular-nums text-slate-400">
                  {{ group.memberCount }} / {{ group.sessionCount }} dars
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ formatDateWithYear(group.startDate) }} — {{ formatDateWithYear(group.endDate) }}
                </td>
                <td>
                  <BaseBadge :tone="group.isActive ? 'success' : 'neutral'">
                    {{ group.isActive ? 'Faol' : 'Arxiv' }}
                  </BaseBadge>
                </td>
                <td>
                  <div class="flex items-center justify-end gap-2">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="openDetail(group.id)"
                    >
                      Ochish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      @click="openEdit(group)"
                    >
                      <template #icon>
                        <AppIcon
                          name="edit"
                          :size="13"
                        />
                      </template>
                      Tahrirlash
                    </BaseButton>
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

    <GroupEditDrawer
      :open="drawerOpen"
      :group-id="editingId"
      @close="drawerOpen = false"
      @saved="refresh"
    />

    <!--
      R21b · yo'nalishlar lug'ati. Panelning O'ZI keshni yangilaydi
      (`['group-categories']` + `['groups']` + `['group']`), shuning uchun
      bu yerda `@saved` kerak emas.
    -->
    <GroupCategoryManagerDrawer
      :open="categoryManagerOpen"
      @close="categoryManagerOpen = false"
    />
  </div>
</template>
