<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { fetchGroups, groupDisplayName, GROUP_SEARCH_MIN } from '@/entities/group'
/*
  ★ CHUQUR IMPORT (barrel `@/entities/group` emas): bu ikki yorliq shu
  to'lqinda qo'shildi va `entities/group/index.ts` ni boshqa agent parallel
  tahrirlayapti — bitta re-export faylida ikki tomonlama o'zgarish keraksiz
  konfliktni tug'dirardi. Fayl ichida `GroupCard.vue` ham aynan shunday
  chuqur yo'l bilan olinadi, ya'ni naqsh yangi emas.
*/
import { groupCuratorLabel, groupWeekdaysLabel } from '@/entities/group/model/types'
import GroupCard from '@/entities/group/ui/GroupCard.vue'
/*
  ★ CHUQUR IMPORT EMAS, BARREL — `entities/group-category` SHU to'lqinda
  yaratilgan yangi papka, ya'ni uning `index.ts` iga boshqa agent tegmayapti
  va konflikt xavfi yo'q (yuqoridagi `entities/group` bilan farqi shu).
*/
import { fetchGroupCategories, groupCategoryLabel } from '@/entities/group-category'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatClock } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { GroupTypeName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
  TodayPill,
} from '@/shared/ui'

/**
 * Ustoz/kurator guruhlari — eski `teacher.html` dagi `#groups`.
 *
 * Ro'yxatni backend rolga qarab cheklaydi (ustoz -> `TeacherId`, kurator ->
 * `AssistantId` yoki kurator guruhi), shuning uchun bu yerda qo'shimcha
 * ruxsat filtri YO'Q.
 *
 * Eski ekrandan ko'chirilgan uch element: "Bugun" tabletkasi, "Jami: N ta
 * guruh" hisoblagichi va nom bo'yicha qidiruv.
 *
 * ═══════════════════════════════════════════════════════════════════════
 * R20 + R21a (2026-08-13 talablari) — UCHTA QARORNI BEKOR QILDI
 * ═══════════════════════════════════════════════════════════════════════
 *
 * 1) KARTOCHKA SETKASI -> JADVAL. Loyiha egasi ustunlarni aynan sanadi:
 *    nom · vaqt · kun · davomiylik · o'quvchi soni · biriktirilgan kurator ·
 *    holat. Kartochkada bu ma'lumot BOR edi, lekin ustunlar bo'ylab
 *    solishtirib bo'lmasdi ("qaysi guruhim eng katta?"). Kartochka
 *    TELEFONDA saqlanadi — ilovadagi umumiy `useBreakpoint` naqshi
 *    (`lg` da `v-if`/`v-else`, `hidden lg:block` EMAS: u ikkala daraxtni
 *    ham quradi).
 *
 * 2) MIJOZDAGI QIDIRUV -> SERVER. Ilgari qidiruv mijozda edi, sababi
 *    izohda shunday yozilgandi: "server kamida 2 belgi talab qiladi va
 *    bitta harf yozilganda ro'yxat sakrab ketardi". Endi sahifalash
 *    qo'shildi — mijozdagi filtr FAQAT joriy sahifani ko'rardi va
 *    "topilmadi" deb yolg'on aytardi. 2 belgi shartini `ManageGroupsPage`
 *    dagi qo'riqchi hal qiladi: qisqa satr SERVERGA UMUMAN yuborilmaydi
 *    (aks holda 400 -> `DataStatus` xato holati -> ro'yxat yo'qoladi).
 *
 * 3) `pageSize: 50` + sahifalagichsiz -> 20 + `PaginationBar`. 50 ta chegara
 *    jadval ko'rinishida ancha ko'proq ko'zga tashlanadi: kartochka setkasida
 *    "pastda yana bor" tuyg'usi bor edi, jadvalda esa ro'yxat jimgina
 *    kesilardi.
 */
const router = useRouter()
const auth = useAuthStore()

/*
  Kartochka ↔ jadval chegarasi `lg` (1024px) — ilovadagi barcha jadvallar
  bilan bir xil (`ManageGroupsPage`, `style.css` dagi "md va lg haqidagi
  asosiy qaror" izohi). iPad tik holati (768px) ataylab kartochka bo'lib
  qoladi: 7 ustun + amal tugmasi u yerda siqilib ketardi.
*/
const { isDesktop } = useBreakpoint()

const search = ref('')
const debouncedSearch = useDebounced(search)

/*
  ★ HOLAT FILTRI SUKUT BO'YICHA "faol" — loyiha egasining talabi
  ("default holatda filterda faqat [faol] chiqarilsin"). Ustoz o'nlab
  tugagan guruhni emas, BUGUN o'qitayotganini ko'radi; arxiv bir bosishda.
*/
const activeFilter = ref<'' | 'true' | 'false'>('true')

/*
  ★ TUR FILTRIDA `Curator` YO'Q, garchi `GroupType` da UCHINCHI qiymat
  sifatida mavjud bo'lsa ham. Ikki sabab: (a) loyiha egasi aynan ikkitasini
  sanadi — "guruh / individual"; (b) kurator guruhi — bu O'QUV bo'limining
  tashkiliy birligi, ustoz uni o'z darslari qatorida filtrlamaydi.
  Filtr bo'sh ("Barcha turlar") bo'lganda kurator guruhlari BARIBIR
  ro'yxatda turadi — ya'ni hech narsa yashirilmayapti, faqat tanlov qisqa.
*/
const typeFilter = ref<GroupTypeName | ''>('')

/*
  ★ R21b · YO'NALISH FILTRI (kategoriya). `''` — "Barcha yo'nalishlar".

  Talab: *"guruh category bo'yicha ... bu category parametr sifatida guruh
  uchun qo'shilishi kerak"*. Ustoz uchun bu R21a dagi tur/holat filtrlari
  bilan bir qatorda turadi — uchalasi ham AYNI savolga javob beradi:
  "qaysi guruhlarimni ko'rmoqchiman".

  ★ `<select>` qiymati DOIM satr, shuning uchun holat ham satr sifatida
  saqlanadi va so'rovga `Number(...)` bilan uzatiladi.
*/
const categoryFilter = ref('')

const page = ref(1)

const PAGE_SIZE = 20

/*
  ★ Server shartnomasi: `GroupService.MinSearchLength = 2`, qisqa satrda 400.
  Shuning uchun qisqa satr YUBORILMAYDI va foydalanuvchiga nima kutilayotgani
  aytiladi — aks holda birinchi harfdayoq jadval xato ekraniga almashardi.
*/
const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= GROUP_SEARCH_MIN ? searchTerm.value : undefined,
)

// Filtr o'zgarsa 5-sahifada qolib ketmaslik uchun boshiga qaytariladi.
watch([effectiveSearch, typeFilter, activeFilter, categoryFilter], () => {
  page.value = 1
})

const groupsQuery = useQuery({
  queryKey: ['groups', 'mine', effectiveSearch, typeFilter, activeFilter, categoryFilter, page],
  queryFn: ({ signal }) =>
    fetchGroups(
      {
        search: effectiveSearch.value,
        type: typeFilter.value === '' ? undefined : typeFilter.value,
        isActive: activeFilter.value === '' ? undefined : activeFilter.value === 'true',
        /*
          🔴 SERVERDA FILTRLANADI (qidiruv bilan AYNI sabab, yuqoridagi
          2-band): ro'yxat `PAGE_SIZE = 20` bilan sahifalangan, ya'ni
          mijozdagi filtr FAQAT joriy sahifani ko'rardi va 21-guruh mos
          kelsa ham "Guruh topilmadi" degan yolg'on javob berardi.
        */
        categoryId: categoryFilter.value === '' ? undefined : Number(categoryFilter.value),
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

/*
  Filtr tanlagichi uchun lug'at — FAQAT FAOL yo'nalishlar.

  ★ `ManageGroupsPage` DAN FARQI ATAYLAB: u yerda arxivlanganlar ham
  ko'rsatiladi ("IELTS ni arxivladik — unda nechta guruh qolgan?" — bu o'quv
  bo'limining savoli). Ustozga esa arxiv yorliqlari shovqin: u bugun
  o'qitayotgan guruhlarini saralaydi.

  ⚠️ Chekinish: ustozning guruhida ARXIVLANGAN yo'nalish turgan bo'lsa, uni
  tanlagich orqali filtrlab bo'lmaydi. Bu qabul qilindi — yorliq jadvalda
  baribir KO'RINADI (`groupCategoryLabel`), ya'ni ma'lumot yashirilmaydi.
*/
const categoriesQuery = useQuery({
  queryKey: ['group-categories', 'active'],
  queryFn: ({ signal }) => fetchGroupCategories({ isActive: true }, { signal }),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const groups = computed(() => groupsQuery.data.value?.items ?? [])
const total = computed(() => groupsQuery.data.value?.total ?? 0)
const totalPages = computed(() => groupsQuery.data.value?.totalPages ?? 1)

/*
  Bo'sh holat matni ikki xil: "hech qachon biriktirilmagan" va "filtr hech
  nimani topmadi". Filtrlash endi SERVERDA bo'lgani uchun ro'yxat uzunligidan
  buni bilib bo'lmaydi — shartlarning o'zi tekshiriladi.

  ★ Sukutdagi holat ham FILTR (faqat faol). Ya'ni faqat arxiv guruhi bor
  ustozga "Guruh biriktirilmagan" chiqadi — bu chekinish ataylab qabul
  qilindi: matnlarni o'zgartirmaslik sharti bor, holat tanlagichi esa
  darhol yuqorida turibdi.
*/
const isDefaultFilter = computed(
  () =>
    effectiveSearch.value === undefined &&
    typeFilter.value === '' &&
    // R21b · yangi filtr ham SHU ro'yxatga qo'shildi: aks holda yo'nalish
    // tanlangan holatda bo'sh natija "Guruh biriktirilmagan" deb
    // ko'rsatilardi va ustoz filtrni aybdor deb o'ylamasdi.
    categoryFilter.value === '' &&
    activeFilter.value === 'true',
)

const errorMessage = computed(() =>
  groupsQuery.error.value !== null ? toUserMessage(groupsQuery.error.value) : null,
)

function openGroup(groupId: number): void {
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}
</script>

<template>
  <div>
    <!--
      Sarlavha ROLGA qarab o'zgaradi, chunki yon menyudagi band ham shunday
      nomlanadi ("Kurator guruhlari") — menyu bir nom, sahifa boshqa nom
      ko'rsatsa foydalanuvchi qayerdaligini yo'qotadi.
    -->
    <PageHeader
      :title="auth.role === 'Assistant' ? 'Kurator guruhlari' : 'Guruhlarim'"
      subtitle="Sizga biriktirilgan guruhlar"
    />
    <TodayPill />

    <div class="mb-3.5 flex flex-wrap items-center gap-3">
      <span
        class="rounded-[20px] border border-brand-500/20 bg-brand-500/14 px-3 py-1 text-[13px] font-semibold text-brand-500"
      >
        Jami: {{ total }} ta guruh
      </span>
      <div class="relative min-w-[180px] flex-1 sm:max-w-xs">
        <!--
          R22: qidiruv endi guruh nomi bilan CHEKLANMAGAN — server ustoz,
          kurator, kurator guruhi va kurs nomlarini ham qaraydi. Yorliq va
          placeholder shu qamrovni AYTIB turadi: aks holda foydalanuvchi
          ustoz ismini yozib ko'rmasdi va imkoniyat ko'rinmas bo'lib
          qolardi.
        -->
        <label
          class="sr-only"
          for="group-search"
        >
          Guruh, ustoz, kurator yoki kurs nomi bo‘yicha qidirish
        </label>
        <AppIcon
          class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
          name="search"
          :size="15"
        />
        <input
          id="group-search"
          v-model="search"
          class="zn-input pl-9 text-[13px]"
          type="search"
          placeholder="Guruh, ustoz, kurator, kurs…"
        >
      </div>
      <!--
        Ikki filtr qidiruv bilan BIR QATORDA: uchalasi ham ro'yxatni
        toraytiradi, ya'ni bitta guruh. Telefonda qator o'raladi
        (`flex-wrap`) va tanlagichlar yonma-yon yarim kenglikda turadi.
      -->
      <select
        v-model="activeFilter"
        class="zn-input w-auto min-w-[128px] flex-none text-[13px]"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="true">
          Faol
        </option>
        <option value="false">
          Arxiv
        </option>
        <option value="">
          Barcha holatlar
        </option>
      </select>
      <select
        v-model="typeFilter"
        class="zn-input w-auto min-w-[128px] flex-none text-[13px]"
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
      </select>
      <!--
        R21b · YO'NALISH filtri (ATF, Grammatika, CEFR, IELTS).
        ★ Faqat FAOL yo'nalishlar — sabab skriptdagi izohda.
      -->
      <select
        v-model="categoryFilter"
        class="zn-input w-auto min-w-[128px] flex-none text-[13px]"
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
          {{ category.name }}
        </option>
      </select>
    </div>

    <p
      v-if="searchTooShort"
      class="mb-3 text-[11px] text-dim"
    >
      Qidirish uchun kamida {{ GROUP_SEARCH_MIN }} belgi kiriting.
    </p>

    <DataStatus
      :pending="groupsQuery.isPending.value"
      :error="errorMessage"
      :empty="groups.length === 0"
      :retrying="groupsQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="users"
      :empty-title="isDefaultFilter ? 'Guruh biriktirilmagan' : 'Guruh topilmadi.'"
      :empty-text="
        isDefaultFilter ? 'O‘quv bo‘limi sizni guruhga biriktirgach shu yerda ko‘rinadi.' : ''
      "
      @retry="groupsQuery.refetch()"
    >
      <!-- Telefon/planshet: kartochka setkasi (eski ko'rinish o'zgarmadi). -->
      <div
        v-if="!isDesktop"
        class="grid gap-3 sm:grid-cols-2"
      >
        <GroupCard
          v-for="group in groups"
          :key="group.id"
          :group="group"
          @open="openGroup"
        />
      </div>

      <!--
        Desktop (≥1024px): jadval. Ustunlar tartibi loyiha egasi sanagan
        tartibda — nom, vaqt, kun, davomiylik, o'quvchi, kurator, holat.
        Sakkizinchi (nomsiz) ustun — amal tugmasi; `ManageGroupsPage` da ham
        shunday, jadval qatoridan tafsilotga o'tish yo'li kerak.
      -->
      <BaseCard
        v-else
        flush
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th>Guruh nomi</th>
                <!--
                  R21b · YO'NALISH ustuni. Loyiha egasi sanagan yetti
                  ustunga SAKKIZINCHI qo'shildi va u NOMDAN keyin turadi:
                  filtrlangan ro'yxatda "nima bo'yicha filtrladim" savoliga
                  javob qatorning o'zida ko'rinishi kerak, aks holda
                  tanlagichga qayta qarash kerak bo'lardi.
                -->
                <th>Yo‘nalish</th>
                <th>Vaqti</th>
                <th>Kunlari</th>
                <th>Davomiyligi</th>
                <th>O‘quvchi</th>
                <th>Biriktirilgan kurator</th>
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
                <td
                  class="text-slate-400"
                  v-text="groupCategoryLabel(group)"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatClock(group.startTime)"
                />
                <td
                  class="text-slate-400"
                  v-text="groupWeekdaysLabel(group)"
                />
                <td class="tabular-nums text-slate-400">
                  {{ group.durationMinutes }} daq.
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ group.memberCount }}
                </td>
                <td
                  class="text-slate-400"
                  v-text="groupCuratorLabel(group)"
                />
                <td>
                  <BaseBadge :tone="group.isActive ? 'success' : 'neutral'">
                    {{ group.isActive ? 'Faol' : 'Arxiv' }}
                  </BaseBadge>
                </td>
                <td>
                  <!--
                    Matn kartochkadagi bilan AYNAN bir xil ("Batafsil") —
                    ikki ko'rinish bitta amalni ikki nom bilan atamasin.
                  -->
                  <button
                    type="button"
                    class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
                    @click="openGroup(group.id)"
                  >
                    Batafsil
                    <AppIcon
                      name="chevron-right"
                      :size="14"
                    />
                  </button>
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

      <!--
        Telefonda ham sahifalash kerak: `pageSize` 20 ga tushdi, ya'ni 25
        guruhli ustoz kartochka ko'rinishida ham qolganini ko'ra olishi shart.
      -->
      <PaginationBar
        v-if="!isDesktop"
        :page="page"
        :total-pages="totalPages"
        :total="total"
        @update:page="page = $event"
      />
    </DataStatus>
  </div>
</template>
