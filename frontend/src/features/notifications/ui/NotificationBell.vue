<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  badgeLabel,
  deleteNotifications,
  fetchNotifications,
  fetchUnreadCount,
  markNotificationsRead,
  notificationIcon,
  notificationRouteName,
} from '@/entities/notification'
import { formatDateTime } from '@/shared/lib/datetime'
import { useMinWidth } from '@/shared/lib/useBreakpoint'
import { useConfirm } from '@/shared/lib/useConfirm'
import { useModalHost } from '@/shared/lib/useModalHost'
import type { NotificationDto, NotificationPageDto, NotificationUnreadDto } from '@/shared/types'
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
 *
 * ── IKKI JOYLASHUV: "OSILGAN RO'YXAT" va "MARKAZDAGI QATLAM" (2026-08-15) ──
 *
 * Loyiha egasi: *"telefon holatida butun kenglikning 90-95 foizini
 * egallagan holda o'rtada chiqsin"*.
 *
 * Shuning uchun panel IKKI xil chiziladi:
 *   • ≥640px — qo'ng'iroqchaga OSILGAN ochiluvchi ro'yxat (`absolute`),
 *     eski xatti-harakat o'zgarmadi;
 *   • <640px — ekran MARKAZIDAGI qatlam (`fixed`, kenglik 92vw), fon
 *     pardasi, skroll qulfi va fokus tuzog'i bilan.
 *
 * ★ ALMASHISH `Teleport :disabled` ORQALI, ikki nusxa shablon bilan EMAS:
 * qatorlar, belgilash rejimi va tugmalar ikki marta yozilsa, ulardan biri
 * albatta yangilanmay qolardi.
 *
 * ★ TELEFONDA `fixed` KERAK: `absolute` panel qo'ng'iroqchaga nisbatan
 * joylashadi va uni ekran MARKAZIGA qo'yib bo'lmaydi (qo'ng'iroqcha o'ng
 * chekkada). `translate-x` bilan markazlash ham ishlamaydi:
 * `animate-fade-up` oxirida `transform: none` qo'yadi va markazlash
 * animatsiya tugashi bilan yo'qolardi. Shuning uchun `inset-x-[4vw]` —
 * transformsiz markazlash.
 */
const props = withDefaults(
  defineProps<{
    /**
     * Panel qaysi tomonga ochiladi. Yon menyuda qo'ng'iroqcha CHAP
     * chekkada turadi, ya'ni o'ngga tekislangan panel ekrandan chiqib
     * ketardi.
     *
     * ⚠️ FAQAT keng ekranga taalluqli: telefonda panel markazda va
     * tekislash umuman qo'llanmaydi.
     */
    align?: 'left' | 'right'
  }>(),
  { align: 'right' },
)

const router = useRouter()
const queryClient = useQueryClient()
const confirm = useConfirm()

const open = ref(false)
const root = ref<HTMLElement | null>(null)
const trigger = ref<HTMLButtonElement | null>(null)
const panel = ref<HTMLElement | null>(null)

/**
 * ≥640px — "osilgan ro'yxat" joylashuvi (`ConfirmDialog` ham AYNI shu
 * chegarada varaqadan dialogga o'tadi, ya'ni ilovada bitta chegara).
 */
const isWide = useMinWidth('sm')
const isCompact = computed(() => !isWide.value)

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

/** Amal xatosi — panelning O'ZIDA ko'rsatiladi (sabab quyida, `remove` izohida). */
const actionError = ref<string | null>(null)

const markRead = useMutation({
  mutationFn: (ids?: number[]) => markNotificationsRead(ids),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_ROOT_KEY })
  },
})

/* ------------------------------- belgilash -------------------------------- */

/**
 * BELGILASH REJIMI — iOS "Tanlash / Select" naqshi.
 *
 * Talab: *"agar hech nima belgilanmagan bo'lsa hammasini o'chirish
 * buttoni ko'rinmasligi kerak, belgilash qismlari ham xuddi shunday"*.
 *
 * Shuning uchun UCH bosqichli ko'rinish:
 *   1. odatdagi holat — belgilash katakchalari YO'Q, har qatorda
 *      o'chirish tugmasi bor;
 *   2. "Tanlash" bosilgan — katakchalar chapdan sirg'alib chiqadi,
 *      sarlavhada "Barchasi/Hech biri" almashtirgichi paydo bo'ladi;
 *   3. kamida bittasi belgilangan — pastda "O'chirish (N)" paneli.
 *
 * ★ NEGA KATAKCHALAR DOIM KO'RINMAYDI: ular doimiy bo'lsa har qator
 * chapdan 26px yo'qotardi va ro'yxatning ASOSIY vazifasi (o'qish va
 * bosib o'tish) belgilash bilan aralashib ketardi. iOS Mail va
 * Bildirishnomalar markazi ham aynan shunday ishlaydi.
 */
const selectMode = ref(false)

/**
 * ★ MASSIV, `Set` EMAS: ro'yxat ko'pi bilan 20 qator
 * (`NOTIFICATION_PAGE_SIZE`), ya'ni `includes` ning O(n) narxi
 * sezilmaydi; massiv esa shablonda ham, `mutate` ga uzatishda ham
 * qo'shimcha o'girishsiz ishlaydi.
 */
const selectedIds = ref<number[]>([])

const selectedCount = computed(() => selectedIds.value.length)
const allSelected = computed(
  () => items.value.length > 0 && selectedCount.value === items.value.length,
)

function isSelected(id: number): boolean {
  return selectedIds.value.includes(id)
}

function toggleSelected(id: number): void {
  selectedIds.value = isSelected(id)
    ? selectedIds.value.filter((current) => current !== id)
    : [...selectedIds.value, id]
}

function toggleSelectAll(): void {
  selectedIds.value = allSelected.value ? [] : items.value.map((item) => item.id)
}

function exitSelectMode(): void {
  selectMode.value = false
  selectedIds.value = []
}

/**
 * Ro'yxat yangilanganda BELGILANGANLARNI TOZALAYMIZ.
 *
 * ★ NEGA KERAK: qator boshqa qurilmada o'chirilgan yoki sahifa qayta
 * so'ralgan bo'lishi mumkin. Tozalanmasa, "O'chirish (3)" yozuvi
 * ro'yxatda YO'Q qatorlarni sanab turardi va so'rov ularni jimgina
 * e'tiborsiz qoldirardi — foydalanuvchi esa "uchtasini o'chirdim" deb
 * o'ylardi.
 */
watch(items, (rows) => {
  if (selectedIds.value.length === 0) return
  const alive = new Set(rows.map((row) => row.id))
  selectedIds.value = selectedIds.value.filter((id) => alive.has(id))
})

/* -------------------------------- o'chirish -------------------------------- */

/**
 * 🔴 TASDIQLASH OYNASI OCHIQ TURGAN PAYT.
 *
 * `ConfirmDialog` ham `body` ga teleport qilinadi, ya'ni undagi bosish
 * bizning "tashqariga bosildi" tekshiruvimizga TASHQI bo'lib ko'rinadi
 * va panel tasdiq berilgunga qadar yopilib ketardi (Promise esa
 * yopilgan panelning mutatsiyasini chaqirardi). ESC bilan ham xuddi
 * shunday: `useModalHost` faqat ENG USTIDAGI qatlamni yopadi, bizning
 * `document` tinglovchimiz esa buni bilmaydi.
 *
 * Shuning uchun tasdiq kutilayotgan paytda IKKALA yopish yo'li ham
 * o'chiriladi.
 */
const confirming = ref(false)

/**
 * O'chirish mutatsiyasi.
 *
 * ★ KESH DARHOL YANGILANADI (`setQueryData`), keyin bekor qilinadi:
 * `invalidateQueries` ning O'ZI yetarli emas edi — TanStack yangi
 * javob kelgunicha ESKI ro'yxatni ko'rsatib turadi, ya'ni o'chirilgan
 * qator yana ~200ms ekranda qolardi va bosilishi ham mumkin edi.
 * `setQueryData` uni shu zahoti olib tashlaydi, `invalidateQueries`
 * esa server haqiqatini olib keladi (boshqa qurilmadagi o'zgarishlar).
 *
 * ★ `unreadCount` JAVOBDAN olinadi, qo'lda hisoblanmaydi: o'chirilgan
 * qatorlarning nechtasi o'qilmagan ekanini faqat server biladi
 * (ro'yxat eskirgan bo'lishi mumkin).
 */
const remove = useMutation({
  mutationFn: (ids: number[]) => deleteNotifications(ids),
  onSuccess: (result, ids) => {
    const gone = new Set(ids)

    queryClient.setQueryData<NotificationPageDto>(NOTIFICATIONS_FEED_KEY, (previous) =>
      previous === undefined
        ? previous
        : {
            ...previous,
            items: previous.items.filter((item) => !gone.has(item.id)),
            unreadCount: result.unreadCount,
          },
    )
    queryClient.setQueryData<NotificationUnreadDto>(NOTIFICATIONS_UNREAD_KEY, {
      unreadCount: result.unreadCount,
    })

    void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_ROOT_KEY })
  },
  /*
    Xato PANELNING O'ZIDA ko'rsatiladi, toast bilan emas.

    ⚠️ SABAB O'ZGARDI (2026-08-15), QAROR O'ZGARMADI: ilgari toast faqat
    o'quvchi karkasida chizilardi, ya'ni xodim panelida xabar ko'rinmasdi.
    Endi `ToastHost` ildizda va ikkala karkasda ham ishlaydi.

    Shunga qaramay xato SHU YERDA qoladi: u AYNAN o'chirilmagan qatorlar
    yonida turishi kerak. Toast 3 soniyada yo'qoladi va foydalanuvchi
    "qaysi biri o'chmadi?" degan savol bilan qolardi.
  */
  onError: () => {
    actionError.value = 'O‘chirib bo‘lmadi. Qayta urinib ko‘ring.'
  },
})

/**
 * Tasdiq so'raydi.
 *
 * ⚠️ MATN "QAYTARIB BO'LMAYDI" DEB OGOHLANTIRADI va bu shunchaki
 * xushmuomalalik emas: serverda soft-delete YO'Q (sabab
 * `INotificationFeed.DeleteAsync` izohida), ya'ni bu haqiqatan
 * so'nggi to'siq.
 */
async function askDelete(count: number): Promise<boolean> {
  confirming.value = true
  try {
    return await confirm({
      title: count === 1 ? 'Bildirishnomani o‘chirish' : 'Belgilanganlarni o‘chirish',
      message:
        count === 1
          ? 'Bu bildirishnoma ro‘yxatdan butunlay o‘chiriladi.'
          : `${count} ta bildirishnoma ro‘yxatdan butunlay o‘chiriladi.`,
      confirmLabel: 'O‘chirish',
      tone: 'danger',
      details: ['Amalni QAYTARIB BO‘LMAYDI', 'Baho va vazifa yozuvlari saqlanadi'],
    })
  } finally {
    confirming.value = false
  }
}

async function handleRemoveOne(item: NotificationDto): Promise<void> {
  if (!(await askDelete(1))) return

  actionError.value = null
  remove.mutate([item.id])
}

async function handleRemoveSelected(): Promise<void> {
  const ids = [...selectedIds.value]
  if (ids.length === 0) return

  if (!(await askDelete(ids.length))) return

  actionError.value = null
  /*
    Belgilash rejimi DARHOL yopiladi (javob kutilmasdan): tanlov
    allaqachon `ids` ga ko'chirilgan, ekranda esa o'chayotgan qatorlar
    ustida "belgilangan" holat osilib turardi. Xato bo'lsa qatorlar
    joyida qoladi va yuqorida qizil satr chiqadi.
  */
  exitSelectMode()
  remove.mutate(ids)
}

/* ------------------------------ ochish/yopish ----------------------------- */

/**
 * Telefondagi qatlamning YUQORI chekkasi (piksel).
 *
 * ★ NEGA O'LCHANADI, nega qat'iy qiymat emas: qo'ng'iroqcha ikki xil
 * karkasda (`StudentAppBar` va `AppShell`) turli balandlikdagi
 * sarlavha ichida joylashgan, ustiga `env(safe-area-inset-top)` ham
 * qurilmaga qarab o'zgaradi. O'lchov bilan panel HAR IKKALA karkasda
 * qo'ng'iroqcha ostidan chiqadi.
 */
const panelTop = ref(0)

function measurePanel(): void {
  const rect = trigger.value?.getBoundingClientRect()
  if (rect === undefined) return
  panelTop.value = Math.round(rect.bottom + 8)
}

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
 *
 * ★ PANEL ALOHIDA TEKSHIRILADI: telefonda u `body` ga teleport
 * qilinadi, ya'ni `root` ning ICHIDA emas — faqat `root` tekshirilsa
 * panelning o'z ichiga bosish uni yopib yuborardi.
 */
function handlePointerDown(event: MouseEvent): void {
  if (confirming.value) return

  const target = event.target
  if (!(target instanceof Node)) return

  if (root.value?.contains(target) === true) return
  if (panel.value?.contains(target) === true) return

  close()
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return
  // Telefonda ESC ni `useModalHost` boshqaradi (u faqat eng ustidagi
  // qatlamni yopadi, ya'ni tasdiq oynasi ochiq bo'lsa panel qoladi).
  if (isCompact.value || confirming.value) return

  close()
  // Fokus tugmaga QAYTADI — klaviatura foydalanuvchisi paneldan chiqib
  // sahifaning boshiga tushib qolmasin (`AppShell` drawer'idagi qoida).
  trigger.value?.focus()
}

watch(open, (isOpen) => {
  if (isOpen) {
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeydown)
    window.addEventListener('resize', measurePanel)
    void nextTick(() => {
      measurePanel()
      // Ro'yxat eskirgan bo'lishi mumkin (hub uzilib turgan bo'lsa) —
      // ochilishda darhol yangilaymiz.
      void feedQuery.refetch()
    })
  } else {
    document.removeEventListener('mousedown', handlePointerDown)
    document.removeEventListener('keydown', handleKeydown)
    window.removeEventListener('resize', measurePanel)
    // Yopilganda TOZA holatga qaytadi: keyingi ochilishda yarim
    // belgilangan ro'yxat va eski xato satri chiqmasin.
    exitSelectMode()
    actionError.value = null
  }
})

// Qurilma aylantirilsa qo'ng'iroqcha joyi o'zgaradi (sarlavha balandligi
// boshqa) — panel u bilan birga suriladi.
watch(isCompact, () => {
  if (open.value) measurePanel()
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', handlePointerDown)
  document.removeEventListener('keydown', handleKeydown)
  window.removeEventListener('resize', measurePanel)
})

/**
 * TELEFONDA panel to'laqonli QATLAM: skroll qulfi, fokus tuzog'i va ESC
 * shu yerdan keladi (`BaseModal`/`ConfirmDialog` bilan AYNI mexanika —
 * `useModalHost` izohidagi qoida: o'z `keydown` ishlovchingizni
 * qo'ymang).
 *
 * ★ Keng ekranda QATLAM QO'YILMAYDI: u yerda bu oddiy ochiluvchi ro'yxat
 * bo'lib qoladi va sahifa skrollini qulflash noto'g'ri bo'lardi.
 */
useModalHost({
  open: () => open.value && isCompact.value,
  onClose: close,
  panel,
  kind: 'dialog',
})

/* -------------------------------- amallar --------------------------------- */

function handleMarkAll(): void {
  if (unreadCount.value === 0) return
  markRead.mutate(undefined)
}

/**
 * Qatorni bosish.
 *
 * • BELGILASH REJIMIDA — belgilaydi/bekor qiladi va HECH QAYERGA
 *   O'TMAYDI. Aks holda "uchtasini tanlayman" degan foydalanuvchi
 *   birinchi bosishda boshqa sahifaga uchib ketardi.
 * • ODATDAGI holatda — o'qildi deb belgilanadi va tegishli sahifaga
 *   o'tiladi.
 *
 * ★ NAVIGATSIYA "O'QILDI" JAVOBINI KUTMAYDI: kutilsa, tarmoq sekin
 * bo'lgan paytda foydalanuvchi bosgandan keyin bir soniya hech nima
 * bo'lmagandek turardi. Belgilash idempotent, ya'ni u yiqilsa ham eng
 * yomoni — nishon raqami keyingi yangilanishgacha eski qoladi.
 */
function handleOpenItem(item: NotificationDto): void {
  if (selectMode.value) {
    toggleSelected(item.id)
    return
  }

  if (!item.read) markRead.mutate([item.id])

  close()

  const name = notificationRouteName(item.kind)

  // Marshrut mavjud bo'lmasa (rol boshqa panelda) — jimgina qolamiz:
  // `router.push` noma'lum nom bilan istisno tashlaydi va u konsolni
  // bekorga to'ldirardi.
  if (router.hasRoute(name)) void router.push({ name })
}

/* ------------------------------- ko'rinish -------------------------------- */

/**
 * Panelning joylashuvi.
 *
 * ★ 92vw — talabdagi "90-95%" oralig'ining o'rtasi; `max-w` esa katta
 * telefon va planshetda panelni cho'zilib ketishdan saqlaydi
 * (`mx-auto` bilan baribir markazda qoladi).
 */
const panelPositionClass = computed(() =>
  isCompact.value
    ? 'fixed inset-x-[4vw] mx-auto max-w-[26rem]'
    : props.align === 'left'
      ? 'absolute left-0 top-[calc(100%+8px)] w-[min(21rem,calc(100vw-2rem))]'
      : 'absolute right-0 top-[calc(100%+8px)] w-[min(21rem,calc(100vw-2rem))]',
)
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
      ★ `Teleport` FAQAT TELEFONDA yoqiladi (`:disabled="!isCompact"`).

      Keng ekranda panel qo'ng'iroqchaning yonida `absolute` bo'lib
      qoladi — u yopishqoq sarlavha ichida `z-30` bilan chiziladi, ya'ni
      joylashuvni JS bilan hisoblash kerak emas. Telefonda esa u ekran
      markaziga chiqishi kerak va sarlavhaning `overflow`/`z-index`
      konteksti bunga xalaqit berardi.
    -->
    <Teleport
      to="body"
      :disabled="!isCompact"
    >
      <!--
        FON PARDASI — faqat telefonda.

        ★ JUDA YENGIL (`/25` + kichik blur): talab *"orqa fon ozgina
        ko'rinib tursin"* deydi. To'q parda qo'yilsa panelning shaffofligi
        umuman sezilmasdi — orqada faqat kulrang tekislik qolardi.
      -->
      <div
        v-if="open && isCompact"
        class="fixed inset-0 z-40 bg-slate-950/25 backdrop-blur-[2px]"
        role="presentation"
        @click="close"
      />

      <div
        v-if="open"
        ref="panel"
        class="z-40 flex animate-fade-up flex-col overflow-hidden rounded-[22px] border border-line-strong/60 bg-ink-900/75 shadow-[0_20px_60px_-16px_rgb(15_17_23/0.35)] backdrop-blur-2xl backdrop-saturate-150"
        :class="panelPositionClass"
        :style="isCompact ? { top: `${panelTop}px` } : undefined"
        role="dialog"
        :aria-modal="isCompact ? 'true' : undefined"
        aria-label="Bildirishnomalar"
        tabindex="-1"
      >
        <!--
          SARLAVHA — iOS navigatsiya paneli naqshi: chapda holat/amal,
          o'ngda amal, ikkalasi ham MATNLI tugma (indigo aksent).
        -->
        <div class="flex shrink-0 items-center justify-between gap-2 border-b border-line/70 px-3 py-2.5">
          <!-- ODATDAGI HOLAT: sarlavha + "hammasini o'qildi" ikonkasi -->
          <template v-if="!selectMode">
            <p class="min-w-0 truncate text-[15px] font-semibold tracking-[-0.2px]">
              Bildirishnomalar
            </p>

            <div class="flex shrink-0 items-center gap-1">
              <!--
                "Hammasini o'qildi" — IKONKALI tugma, matnli emas: uning
                yonida "Tanlash" turadi va ikkita matnli tugma 92vw
                kenglikda sarlavhani ikki qatorga bo'lib yuborardi.
              -->
              <button
                v-if="unreadCount > 0"
                type="button"
                class="tap-expand flex size-8 items-center justify-center rounded-full text-brand-500 transition-colors hover:bg-brand-500/10 active:bg-brand-500/15 disabled:opacity-40"
                :disabled="markRead.isPending.value"
                aria-label="Hammasini o'qildi deb belgilash"
                title="Hammasini o'qildi deb belgilash"
                @click="handleMarkAll"
              >
                <AppIcon
                  name="check"
                  :size="17"
                />
              </button>

              <button
                v-if="items.length > 0"
                type="button"
                class="tap-expand rounded-lg px-2 py-1 text-[13px] font-semibold text-brand-500 transition-colors hover:bg-brand-500/10 active:bg-brand-500/15"
                @click="selectMode = true"
              >
                Tanlash
              </button>
            </div>
          </template>

          <!-- BELGILASH REJIMI: "Barchasi/Hech biri" + "Bekor qilish" -->
          <template v-else>
            <button
              type="button"
              class="tap-expand rounded-lg px-2 py-1 text-[13px] font-semibold text-brand-500 transition-colors hover:bg-brand-500/10 active:bg-brand-500/15"
              :aria-label="allSelected ? 'Belgilashni bekor qilish' : 'Barchasini belgilash'"
              @click="toggleSelectAll"
            >
              {{ allSelected ? 'Hech biri' : 'Barchasi' }}
            </button>

            <p
              class="min-w-0 truncate text-[13px] font-semibold tabular-nums text-slate-300"
              aria-live="polite"
            >
              {{ selectedCount > 0 ? `${selectedCount} ta belgilandi` : 'Belgilang' }}
            </p>

            <button
              type="button"
              class="tap-expand rounded-lg px-2 py-1 text-[13px] font-semibold text-brand-500 transition-colors hover:bg-brand-500/10 active:bg-brand-500/15"
              @click="exitSelectMode"
            >
              Bekor qilish
            </button>
          </template>
        </div>

        <!-- Amal xatosi — ro'yxat USTIDA, ya'ni tugmaning yonida turadi. -->
        <p
          v-if="actionError !== null"
          class="shrink-0 border-b border-rose-500/20 bg-rose-500/10 px-3.5 py-2 text-[12px] leading-snug text-rose-400"
          role="alert"
          v-text="actionError"
        />

        <div
          class="min-h-0 flex-1 overflow-y-auto scrollbar-slim"
          :class="isCompact ? 'max-h-[min(62dvh,28rem)]' : 'max-h-[min(24rem,60vh)]'"
        >
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
              class="flex items-stretch border-b border-line/70 transition-colors last:border-b-0"
              :class="item.read ? '' : 'bg-brand-500/[0.07]'"
            >
              <!--
                ★ `v-text` ISHLATILADI, `v-html` EMAS. `body` ichida
                USTOZNING IZOHI bor — ya'ni foydalanuvchi yozgan matn.
                `v-html` bu yerda to'g'ridan-to'g'ri XSS yo'li bo'lardi.
                Server ham sof matn yuboradi (Telegram HTML mutlaqo boshqa
                jadvalda) — ikkala tomon bir xil kelishuvda.

                ★ QATOR — TUGMA, o'chirish tugmasi esa uning YONIDA
                (ichida EMAS): tugma ichidagi tugma yaroqsiz HTML va
                brauzerlar uni turlicha "tuzatadi" — bosish hodisasi
                ba'zilarida ikkala tugmaga ham yetib borardi.
              -->
              <button
                type="button"
                class="flex min-w-0 flex-1 items-start gap-2.5 py-3 pl-3 pr-1.5 text-left transition-colors hover:bg-ink-800/60 active:bg-ink-750/60"
                :role="selectMode ? 'checkbox' : undefined"
                :aria-checked="selectMode ? isSelected(item.id) : undefined"
                @click="handleOpenItem(item)"
              >
                <!--
                  BELGILASH KATAKCHASI — chapdan SIRG'ALIB chiqadi
                  (`width` + `opacity` o'tishi). `v-if` bilan
                  qo'yilganda ro'yxat sakrab o'zgarardi; iOS'da esa bu
                  o'tish aynan shu naqshning "tanilishi".
                -->
                <span
                  class="flex shrink-0 items-center self-center overflow-hidden transition-all duration-200 ease-out"
                  :class="selectMode ? 'w-[26px] opacity-100' : 'w-0 opacity-0'"
                  aria-hidden="true"
                >
                  <span
                    class="flex size-[21px] items-center justify-center rounded-full border-[1.5px] transition-colors"
                    :class="
                      isSelected(item.id)
                        ? 'border-brand-500 bg-brand-500 text-white'
                        : 'border-line-strong text-transparent'
                    "
                  >
                    <AppIcon
                      name="check"
                      :size="12"
                    />
                  </span>
                </span>

                <span
                  class="relative mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-[9px]"
                  :class="item.read ? 'bg-ink-800 text-slate-400' : 'bg-brand-500/15 text-brand-500'"
                >
                  <AppIcon
                    :name="notificationIcon(item.kind)"
                    :size="15"
                  />

                  <!--
                    O'QILMAGAN NUQTASI — ikonka BURCHAGIDA, qatorning
                    o'ng chekkasida emas: u yer endi o'chirish tugmasiga
                    tegishli va nuqta bilan tugma yonma-yon tursa,
                    barmoq nuqtani bosmoqchi bo'lib o'chirishni bosardi.
                  -->
                  <span
                    v-if="!item.read"
                    class="absolute -right-0.5 -top-0.5 size-2 rounded-full bg-brand-500 ring-2 ring-ink-900"
                    aria-hidden="true"
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
              </button>

              <!--
                BITTA QATORNI O'CHIRISH.

                ★ KENGLIGI 44px (`w-11`) — `tap-expand` EMAS: u ko'rinmas
                `::after` bilan maydonni har tomondan 6px kengaytiradi va
                bu yerda o'sha 6px qo'shni tugmaning (qator) ustiga
                tushardi — ya'ni qatorning o'ng chekkasini bosgan odam
                O'CHIRISHNI bosgan bo'lardi. Xavfli amal uchun bu
                mumkin emas.

                ★ Belgilash rejimida YASHIRINADI: u yerda o'chirish
                pastdagi umumiy tugma orqali bo'ladi.
              -->
              <button
                v-if="!selectMode"
                type="button"
                class="flex w-11 shrink-0 items-center justify-center self-stretch text-slate-500 transition-colors hover:bg-rose-500/10 hover:text-rose-500 active:bg-rose-500/15 disabled:opacity-40"
                :disabled="remove.isPending.value"
                :aria-label="`«${item.title}» bildirishnomasini o'chirish`"
                @click="handleRemoveOne(item)"
              >
                <AppIcon
                  name="trash"
                  :size="16"
                />
              </button>
            </li>
          </ul>
        </div>

        <!--
          PASTKI AMAL PANELI — FAQAT kamida bitta qator belgilanganda.
          Talab: *"agar hech nima belgilanmagan bo'lsa hammasini
          o'chirish buttoni ko'rinmasligi kerak"*.
        -->
        <div
          v-if="selectMode && selectedCount > 0"
          class="shrink-0 border-t border-line/70 p-2.5"
          :style="isCompact ? { paddingBottom: 'calc(0.625rem + env(safe-area-inset-bottom, 0px))' } : undefined"
        >
          <button
            type="button"
            class="flex w-full items-center justify-center gap-1.5 rounded-[14px] bg-rose-500/12 py-2.5 text-[13px] font-semibold text-rose-500 transition-colors hover:bg-rose-500/20 active:bg-rose-500/25 disabled:opacity-50"
            :disabled="remove.isPending.value"
            @click="handleRemoveSelected"
          >
            <AppIcon
              name="trash"
              :size="15"
            />
            <span class="tabular-nums">O‘chirish ({{ selectedCount }})</span>
          </button>
        </div>
      </div>
    </Teleport>
  </div>
</template>
