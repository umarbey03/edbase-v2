<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import { navSectionsForRole, roleLabel, roleTone, useAvatar } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { NotificationBell } from '@/features/notifications'
import { AppIcon, BaseAvatar, BaseBadge } from '@/shared/ui'

/**
 * Yon menyu MAZMUNI (logotip + rol menyusi + foydalanuvchi bloki).
 *
 * Alohida komponent, chunki AYNAN shu mazmun ikki joyda ko'rsatiladi:
 * desktopda doimiy ustun, telefon/planshetda esa chekka drawer. Nusxa
 * ko'chirilsa — menyu bir joyda yangilanib, ikkinchisida eskirib qolardi.
 */
const props = withDefaults(
  defineProps<{
    /**
     * `true` — ikonka-yaqqol rejim (faqat AppIcon + tap maydoni, matn
     * yashirin). Loyiha egasi, 2026-08-15: *"o'ng tarafdagi navbarga
     * toggle"* — DESKTOP doimiy ustunidagi yig'ish/yoyish tugmasi
     * (`AppShell`). Telefon/planshet drawer'ida HAR DOIM `false`: u
     * allaqachon vaqtinchalik ustma-ust panel, uni yana siqish foyda
     * bermaydi.
     */
    collapsed?: boolean
  }>(),
  { collapsed: false },
)

const emit = defineEmits<{ navigate: []; logout: []; edit: [] }>()

const auth = useAuthStore()

/** Profil rasmi — yo'q bo'lsa `BaseAvatar` ism harfini chizadi. */
const avatarUrl = useAvatar(
  computed(() => auth.user?.id ?? null),
  computed(() => auth.user?.avatarUpdatedAt ?? null),
)

const route = useRoute()

const sections = computed(() => navSectionsForRole(auth.role))

/**
 * ════════════════════════════════════════════════════════════════════════
 * OCHIQ BO'LIMLAR (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ JORIY SAHIFA TURGAN BO'LIM DOIM OCHIQ: xodim bosgan bandning
 * yo'qolib qolishi ("men qayerdaman?") menyuni ishonchsiz qilardi.
 * Shuning uchun ochiqlik `open` ro'yxatida SAQLANADI, lekin joriy
 * marshrut har doim ustun turadi.
 *
 * ★ SAQLANADI (`localStorage`): xodim har sahifa almashganda moliya
 * bo'limini qayta ochishi kerak bo'lsa, bo'limlarning butun foydasi
 * yo'qolardi.
 */
const STORAGE_KEY = 'zn.nav.open'

function readOpen(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const parsed: unknown = raw === null ? null : JSON.parse(raw)

    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : []
  } catch {
    // Buzuq qiymat — menyu baribir ishlashi kerak.
    return []
  }
}

const open = ref<string[]>(readOpen())

watch(open, (value) => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  } catch {
    // Xotira to'lgan yoki taqiqlangan — bu menyuni to'xtatmaydi.
  }
}, { deep: true })

/** Shu bo'limda joriy sahifa bormi. */
function holdsCurrent(sectionKey: string): boolean {
  const section = sections.value.find((s) => s.key === sectionKey)

  return section?.items.some((item) => item.routeName === route.name) ?? false
}

function isOpen(sectionKey: string): boolean {
  return open.value.includes(sectionKey) || holdsCurrent(sectionKey)
}

function toggle(sectionKey: string): void {
  open.value = open.value.includes(sectionKey)
    ? open.value.filter((key) => key !== sectionKey)
    : [...open.value, sectionKey]
}

/** Eski panellardagi logo osti yozuvi. */
const PANEL_LABELS: Record<string, string> = {
  Teacher: 'Ustoz paneli',
  Assistant: 'Yordamchi paneli',
  Academic: "O'quv bo'limi",
  Admin: 'Admin',
}

const panelLabel = computed(() =>
  auth.role !== null ? (PANEL_LABELS[auth.role] ?? "Ta'lim platformasi") : "Ta'lim platformasi",
)
</script>

<template>
  <div class="flex h-full min-h-0 flex-col bg-ink-900">
    <!-- Logotip (eski `.logo`) -->
    <div
      class="flex shrink-0 items-center gap-3 border-b border-line px-4.5 py-5"
      :class="{ 'justify-center px-2': props.collapsed }"
    >
      <!--
        Indigo gradient plitka — ekran suratlaridagi belgi. Gradient
        TOKENLAR orqali (`from-brand-500 to-brand-700`), qotib qolgan
        HEX'siz: brend rangi almashsa plitka o'z-o'zidan moslashadi.
      -->
      <span
        class="flex size-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-base font-bold text-on-brand shadow-xs"
        aria-hidden="true"
      >
        Z
      </span>
      <div
        v-if="!props.collapsed"
        class="min-w-0"
      >
        <!--
          R19 — brend nomi BITTA rangda. Ilgari "Zin" tanadan rang meros olardi
          (`slate-100`), "-Nur" esa `text-brand-500` edi: bitta so'z ikki xil
          rangda chizilardi. Endi butun so'z aksent tokenida.

          ★ Matn O'ZGARMADI — "ZIN-NUR" aynan shundayligicha qoladi, faqat
          ikkiga bo'lingan `<span>` bitta rangli qatlamga yig'ildi.
        -->
        <p class="truncate text-lg font-bold tracking-tight text-brand-500">
          ZIN-NUR
        </p>
        <!--
          Logo ostida ROL yoziladi — eski panellarda ham shunday edi
          (`{{ 'Yordamchi' if role=='assistant' else 'Ustoz' }} paneli`,
          `{{ 'Admin' if role=='admin' else "O'quv bo'limi" }}`). Umumiy
          "Ta'lim platformasi" matni xodimga qaysi panelda turganini
          aytmasdi — ayniqsa bir odam ikki rolda ishlaganda.
        -->
        <p
          class="mt-0.5 truncate text-[10px] uppercase tracking-[1.5px] text-dim"
          v-text="panelLabel"
        />
      </div>

      <!--
        R35/R36 — qo'ng'iroqcha DESKTOP yon menyusining tepasida.

        🔴 `hidden lg:flex` SHART: bu komponent IKKI joyda chiziladi —
        desktopdagi doimiy ustunda va telefondagi drawer'da. Drawer
        versiyasida qo'ng'iroqcha ko'rsatilsa, u `AppShell` sarlavhasidagi
        qo'ng'iroqcha bilan IKKILANARDI (ikkalasi ham telefonda). Bu klass
        uni faqat desktopga qoldiradi, ya'ni har o'lchamda AYNAN BITTA
        qo'ng'iroqcha ko'rinadi.

        ★ Panel CHAPGA tekislanadi: yon menyu ekranning chap chekkasida,
        o'ngga tekislangan panel menyuning tor ustunidan chiqib, kontent
        ustiga noto'g'ri tomondan tushardi.

        ★ SIQILGAN HOLATDA YASHIRILADI: tor ustunda (72px) qo'ng'iroqcha
        logotip plitkasi bilan to'qnashardi. Xabarlar SIQIQ holatda ham
        kelaveradi — bildirishnoma nishoni yo'qolmaydi, faqat yig'ilgan
        paytda ko'rinmay turadi (yoyilganda darrov ko'rinadi).
      -->
      <NotificationBell
        v-if="!props.collapsed"
        align="left"
        class="ml-auto hidden shrink-0 lg:flex"
      />
    </div>

    <!-- Menyu (eski `.nav`) -->
    <nav
      class="scrollbar-slim min-h-0 flex-1 overflow-y-auto p-2.5"
      aria-label="Asosiy menyu"
    >
      <!--
        `active-class` da `!` SHART: aktiv va oddiy sinflar spetsifikligi bir xil,
        g'olibni CSS'dagi tartib hal qiladi — `text-slate-400` keyinroq chiqib,
        aktiv menyu elementi kulrang bo'lib qolardi.

        ★ FAOL ELEMENT — TO'LIQ INDIGO FON + OQ MATN (ekran suratlaridagidek),
        ilgari 16% tint + indigo matn edi. Yorug' temada tint variant juda
        bo'sh chiqadi: 274 ta kulrang matn orasida "hozir qaysi bo'limdaman"
        savoli bir qarashda javob olmasdi. Oq matn indigo fonda 5.9:1.

        `hover:bg-brand-600!` — faol elementning o'zi ustiga kelganda ham
        indigo qoladi (aks holda `hover:bg-ink-800` uni oqartirib yuborardi).
      -->
      <template
        v-for="section in sections"
        :key="section.key"
      >
        <!--
          ═══════════ BO'LIM SARLAVHASI ═══════════
          ★ YIG'ILGAN REJIMDA KO'RSATILMAYDI: 56px lik ustunda sarlavha
          matni sig'maydi va faqat ikonkalar qatorini uzardi. U yerda
          bandlar YASSI chiziladi — bo'lim ochiq-yopiqligi ma'nosini
          yo'qotadi.
        -->
        <!--
          ★ OCHILADIGANI — TUGMA, OCHILMAYDIGANI — ODDIY SARLAVHA
          (loyiha egasi, 2026-08-18: *"o'quv bo'limi tablarini drop down
          qilish shart emas"*). Bosilmaydigan sarlavhani tugma qilib
          qo'yish klaviatura bilan yuruvchi foydalanuvchini hech narsa
          qilmaydigan elementga majburlardi.
        -->
        <component
          :is="section.collapsible ? 'button' : 'p'"
          v-if="section.label.length > 0 && !props.collapsed"
          :type="section.collapsible ? 'button' : undefined"
          class="mb-0.5 mt-3 flex w-full items-center gap-2.5 rounded-xl px-3 py-2 text-[11px] font-bold uppercase tracking-wide text-slate-500"
          :class="section.collapsible
            ? 'transition-colors hover:bg-ink-800 hover:text-slate-300'
            : 'cursor-default'"
          :aria-expanded="section.collapsible ? isOpen(section.key) : undefined"
          @click="section.collapsible ? toggle(section.key) : undefined"
        >
          <AppIcon
            v-if="section.icon !== null"
            :name="section.icon"
            :size="15"
          />
          <span
            class="truncate"
            v-text="section.label"
          />
          <AppIcon
            v-if="section.collapsible"
            name="chevron-down"
            :size="14"
            class="ml-auto shrink-0 transition-transform"
            :class="isOpen(section.key) ? 'rotate-180' : ''"
          />
        </component>

        <RouterLink
          v-for="item in section.items"
          v-show="!section.collapsible || props.collapsed || isOpen(section.key)"
          :key="item.routeName"
          :to="{ name: item.routeName }"
          class="mb-0.5 flex min-h-11 items-center gap-2.5 rounded-xl py-2.5 text-sm text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          :class="props.collapsed
            ? 'justify-center px-0'
            : (section.label.length > 0 ? 'pl-6 pr-3' : 'px-3')"
          active-class="bg-brand-500! font-semibold text-on-brand! shadow-xs hover:bg-brand-600! hover:text-on-brand!"
          :title="props.collapsed ? item.label : undefined"
          @click="emit('navigate')"
        >
          <AppIcon
            :name="item.icon"
            :size="17"
          />
          <span
            v-if="!props.collapsed"
            class="truncate"
            v-text="item.label"
          />
        </RouterLink>
      </template>
    </nav>

    <!-- Foydalanuvchi bloki (eski `.userbox`) -->
    <div class="shrink-0 border-t border-line px-4 py-3.5">
      <div
        class="flex items-center gap-2.5"
        :class="{ 'flex-col gap-2': props.collapsed }"
      >
        <!--
          ★ AVATAR VA ISM — BOSILADIGAN (2026-08-15): xodim uchun bu
          profilni tahrirlashning YAGONA kirish nuqtasi. Talab "har
          qanday userlar" degan edi, o'quvchi karkasidagi profil varag'i
          esa bu yerda yo'q.

          Alohida "Tahrirlash" tugmasi QO'YILMADI: yon menyuning eng
          pastida joy tor va uchinchi ikonka (chiqish yonida) tasodifan
          bosiladigan bo'lardi.

          ★ SIQIQ HOLATDA FAQAT AVATAR: ism/badge 72px ustunda sig'maydi.
          Kim ekanligi avatar harfidan va sichqoncha bosilganda ochiladigan
          profil oynasidan bilinadi — sarlavha (`title`) ham qo'shildi.
        -->
        <button
          type="button"
          class="flex min-w-0 flex-1 items-center gap-2.5 rounded-xl px-1 py-1 text-left transition-colors hover:bg-ink-800"
          :class="{ 'flex-none justify-center': props.collapsed }"
          :title="props.collapsed ? `${auth.displayName} — profilni tahrirlash` : 'Profilni tahrirlash'"
          @click="emit('edit')"
        >
          <BaseAvatar
            :name="auth.displayName"
            :src="avatarUrl"
            size="sm"
          />
          <span
            v-if="!props.collapsed"
            class="min-w-0 flex-1"
          >
            <span
              class="block truncate text-[13px] font-semibold text-slate-100"
              v-text="auth.displayName"
            />
            <BaseBadge
              v-if="auth.role !== null"
              :tone="roleTone(auth.role)"
            >
              {{ roleLabel(auth.role) }}
            </BaseBadge>
          </span>
        </button>
        <button
          type="button"
          class="tap-target flex items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          title="Chiqish"
          @click="emit('logout')"
        >
          <AppIcon
            name="logout"
            :size="18"
          />
        </button>
      </div>
    </div>
  </div>
</template>
