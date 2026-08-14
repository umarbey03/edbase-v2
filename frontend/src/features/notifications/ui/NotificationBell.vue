<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  badgeLabel,
  fetchNotifications,
  fetchUnreadCount,
  markNotificationsRead,
  notificationIcon,
  notificationRouteName,
} from '@/entities/notification'
import { formatDateTime } from '@/shared/lib/datetime'
import type { NotificationDto } from '@/shared/types'
import { AppIcon, BaseSpinner } from '@/shared/ui'

import {
  NOTIFICATIONS_FEED_KEY,
  NOTIFICATIONS_ROOT_KEY,
  NOTIFICATIONS_UNREAD_KEY,
} from '../model/notification-queries'

/**
 * ============================================================================
 *  BILDIRISHNOMA QO'NG'IROQCHASI (R35/R36)
 * ============================================================================
 *
 * Loyiha egasi: *"notification uchun ham ishlash kerak"*.
 *
 * ── BU KOMPONENT HUB'GA ULANMAYDI ──────────────────────────────────────────
 *
 * Ulanish KARKAS darajasida (`useNotificationHub`), bu yerda esa faqat
 * TanStack Query keshidan o'qish bor. Sabab: qo'ng'iroqcha xodim
 * karkasida IKKI joyda chiziladi (mobil sarlavha va yon menyu). Agar u
 * o'zi ulansa, bitta foydalanuvchi ikkita WebSocket ushlab turardi va har
 * hodisa ikki marta kelardi. Kesh esa allaqachon ilova bo'ylab yagona.
 *
 * ── RO'YXAT FAQAT OCHILGANDA SO'RALADI ─────────────────────────────────────
 *
 * Nishondagi RAQAM har doim yangilanadi (arzon `unread-count` so'rovi),
 * RO'YXATNING O'ZI esa faqat panel ochilganda (`enabled`). Aks holda har
 * sahifada 20 ta qator tortib kelinardi va ularning 99% i hech qachon
 * ko'rinmasdi.
 */
const props = withDefaults(
  defineProps<{
    /**
     * Panel qaysi tomonga ochiladi. Yon menyuda qo'ng'iroqcha CHAP
     * chekkada turadi, ya'ni o'ngga tekislangan panel ekrandan chiqib
     * ketardi.
     */
    align?: 'left' | 'right'
  }>(),
  { align: 'right' },
)

const router = useRouter()
const queryClient = useQueryClient()

const open = ref(false)
const root = ref<HTMLElement | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)

/* ------------------------------- ma'lumot -------------------------------- */

/**
 * Nishondagi raqam.
 *
 * ★ `refetchOnWindowFocus` ATAYLAB standart holatda qoldirildi (yoqiq):
 * foydalanuvchi boshqa ilovadan qaytganda hub uzilib qolgan bo'lishi
 * mumkin va bu eng arzon "yetib olish" mexanizmi.
 */
const unreadQuery = useQuery({
  queryKey: NOTIFICATIONS_UNREAD_KEY,
  queryFn: ({ signal }) => fetchUnreadCount({ signal }),
})

const feedQuery = useQuery({
  queryKey: NOTIFICATIONS_FEED_KEY,
  queryFn: ({ signal }) => fetchNotifications({}, { signal }),
  enabled: open,
})

const unreadCount = computed(() => unreadQuery.data.value?.unreadCount ?? 0)
const badge = computed(() => badgeLabel(unreadCount.value))

const items = computed<NotificationDto[]>(() => feedQuery.data.value?.items ?? [])

const markRead = useMutation({
  mutationFn: (ids?: number[]) => markNotificationsRead(ids),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_ROOT_KEY })
  },
})

/* ------------------------------ ochish/yopish ----------------------------- */

function toggle(): void {
  open.value = !open.value
}

function close(): void {
  open.value = false
}

/**
 * TASHQARIGA BOSILGANDA yopiladi.
 *
 * ★ `mousedown`, `click` EMAS: `click` da panel ichidagi havola bosilsa
 * hodisa ildizgacha ko'tarilib, navigatsiya boshlangandan KEYIN yopish
 * ishlardi — panel bir lahza "osilib" qolardi. `mousedown` esa bosish
 * boshlanishida ishlaydi.
 *
 * ★ Tinglovchi FAQAT panel ochiq turganda ulanadi: har qo'ng'iroqcha
 * doimiy `document` tinglovchisi ushlab tursa, ular sahifa bo'ylab
 * yig'ilib qolardi.
 */
function handlePointerDown(event: MouseEvent): void {
  const container = root.value
  if (container === null) return
  if (event.target instanceof Node && container.contains(event.target)) return

  close()
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return

  close()
  // Fokus tugmaga QAYTADI — klaviatura foydalanuvchisi paneldan chiqib
  // sahifaning boshiga tushib qolmasin (`AppShell` drawer'idagi qoida).
  trigger.value?.focus()
}

watch(open, (isOpen) => {
  if (isOpen) {
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeydown)
    void nextTick(() => {
      // Ro'yxat eskirgan bo'lishi mumkin (hub uzilib turgan bo'lsa) —
      // ochilishda darhol yangilaymiz.
      void feedQuery.refetch()
    })
  } else {
    document.removeEventListener('mousedown', handlePointerDown)
    document.removeEventListener('keydown', handleKeydown)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', handlePointerDown)
  document.removeEventListener('keydown', handleKeydown)
})

/* -------------------------------- amallar --------------------------------- */

function handleMarkAll(): void {
  if (unreadCount.value === 0) return
  markRead.mutate(undefined)
}

/**
 * Qatorni bosish: o'qildi deb belgilanadi va tegishli sahifaga o'tiladi.
 *
 * ★ NAVIGATSIYA "O'QILDI" JAVOBINI KUTMAYDI: kutilsa, tarmoq sekin
 * bo'lgan paytda foydalanuvchi bosgandan keyin bir soniya hech nima
 * bo'lmagandek turardi. Belgilash idempotent, ya'ni u yiqilsa ham eng
 * yomoni — nishon raqami keyingi yangilanishgacha eski qoladi.
 */
function handleOpenItem(item: NotificationDto): void {
  if (!item.read) markRead.mutate([item.id])

  close()

  const name = notificationRouteName(item.kind)

  // Marshrut mavjud bo'lmasa (rol boshqa panelda) — jimgina qolamiz:
  // `router.push` noma'lum nom bilan istisno tashlaydi va u konsolni
  // bekorga to'ldirardi.
  if (router.hasRoute(name)) void router.push({ name })
}
</script>

<template>
  <div
    ref="root"
    class="relative"
  >
    <button
      ref="trigger"
      type="button"
      class="tap-target relative flex size-10 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
      :aria-label="
        unreadCount > 0 ? `Bildirishnomalar (${unreadCount} ta o'qilmagan)` : 'Bildirishnomalar'
      "
      :aria-expanded="open"
      aria-haspopup="true"
      @click="toggle"
    >
      <AppIcon
        name="bell"
        :size="19"
      />

      <!--
        Nishon — `99+` chegarasi bilan (`badgeLabel`). Uch xonali raqam
        doirani cho'zib, appbar joylashuvini buzardi: ustoz 50 ta ishni
        bir o'tirishda baholaydi.
      -->
      <span
        v-if="badge !== ''"
        class="absolute -right-0.5 -top-0.5 flex min-w-4.5 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold leading-4 text-white tabular-nums"
        aria-hidden="true"
        v-text="badge"
      />
    </button>

    <!--
      Ochiluvchi panel. `Teleport` ISHLATILMAYDI: qo'ng'iroqcha yopishqoq
      sarlavha ichida turadi va u yerda `z-30` allaqachon bor, ya'ni
      panel `absolute` bilan ham ustida chiziladi. Teleport bo'lsa
      joylashuvni JS bilan hisoblash kerak bo'lardi va u scroll paytida
      surilib ketardi.
    -->
    <div
      v-if="open"
      class="absolute top-[calc(100%+6px)] z-40 w-[min(20rem,calc(100vw-2rem))] overflow-hidden rounded-xl border border-line bg-ink-900 shadow-lg"
      :class="props.align === 'left' ? 'left-0' : 'right-0'"
      role="dialog"
      aria-label="Bildirishnomalar"
    >
      <div class="flex items-center justify-between gap-2 border-b border-line px-3.5 py-2.5">
        <p class="text-sm font-semibold">
          Bildirishnomalar
        </p>
        <button
          v-if="unreadCount > 0"
          type="button"
          class="rounded-lg px-2 py-1 text-xs font-semibold text-brand-500 transition-colors hover:bg-ink-800 disabled:opacity-50"
          :disabled="markRead.isPending.value"
          @click="handleMarkAll"
        >
          Hammasini o'qildi
        </button>
      </div>

      <div class="max-h-[min(24rem,60vh)] overflow-y-auto scrollbar-slim">
        <div
          v-if="feedQuery.isPending.value"
          class="flex justify-center py-8 text-dim"
        >
          <BaseSpinner size="sm" />
        </div>

        <p
          v-else-if="feedQuery.isError.value"
          class="px-3.5 py-6 text-center text-sm text-dim"
        >
          Bildirishnomalarni yuklab bo'lmadi.
        </p>

        <p
          v-else-if="items.length === 0"
          class="px-3.5 py-8 text-center text-sm text-dim"
        >
          Hozircha bildirishnoma yo'q.
        </p>

        <ul v-else>
          <li
            v-for="item in items"
            :key="item.id"
          >
            <!--
              ★ `v-text` ISHLATILADI, `v-html` EMAS. `body` ichida
              USTOZNING IZOHI bor — ya'ni foydalanuvchi yozgan matn.
              `v-html` bu yerda to'g'ridan-to'g'ri XSS yo'li bo'lardi.
              Server ham sof matn yuboradi (Telegram HTML mutlaqo boshqa
              jadvalda) — ikkala tomon bir xil kelishuvda.
            -->
            <button
              type="button"
              class="flex w-full items-start gap-2.5 border-b border-line px-3.5 py-3 text-left transition-colors last:border-b-0 hover:bg-ink-800"
              :class="item.read ? '' : 'bg-brand-500/10'"
              @click="handleOpenItem(item)"
            >
              <span
                class="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-lg"
                :class="item.read ? 'bg-ink-800 text-slate-400' : 'bg-brand-500/15 text-brand-500'"
              >
                <AppIcon
                  :name="notificationIcon(item.kind)"
                  :size="15"
                />
              </span>

              <span class="min-w-0 flex-1">
                <span
                  class="block truncate text-sm"
                  :class="item.read ? 'font-medium text-slate-300' : 'font-semibold text-slate-100'"
                  v-text="item.title"
                />
                <span
                  class="mt-0.5 block line-clamp-2 text-xs leading-snug text-dim"
                  v-text="item.body"
                />
                <span
                  class="mt-1 block text-[11px] text-dim tabular-nums"
                  v-text="formatDateTime(item.createdAt)"
                />
              </span>

              <span
                v-if="!item.read"
                class="mt-1.5 size-2 shrink-0 rounded-full bg-brand-500"
                aria-hidden="true"
              />
            </button>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>
