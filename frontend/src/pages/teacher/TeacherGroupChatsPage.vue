<script setup lang="ts">
import { computed, ref } from 'vue'

import { threadKey } from '@/entities/group-chat'
import {
  ChatFillColumn,
  GroupChatRoom,
  GroupChatThreadList,
  useFillHeight,
} from '@/features/group-chat'
import type { GroupChatThreadDto } from '@/shared/types'
import { AppIcon, EmptyState, PageHeader } from '@/shared/ui'

/**
 * "CHATLAR" — eski `teacher.html` dagi `#chats-hub` bo'limi.
 *
 * ★ MATNLAR ESKI MARKUPDAN AYNAN (`teacher.html`, 683–688-qatorlar):
 *     <h1>💬 Chatlar</h1>
 *     <div class="sub">Barcha guruhlaringiz chatlari. Kirib, erkin
 *                      yozishingiz mumkin.</div>
 *     <input placeholder="🔍 Guruh nomi bo'yicha...">
 *
 * ★ ESKI ILOVADAN FARQ (ataylab): u yerda ro'yxatdagi guruh bosilganda
 * `openGroupChat()` GURUH SAHIFASINI ochib, "Chat" tabiga o'tardi — ya'ni
 * ustoz suhbatga kirish uchun har safar butun guruh sahifasini (davomat,
 * baholar, darslar so'rovlari bilan birga) yuklardi. Bu yerda suhbat SHU
 * sahifaning O'ZIDA ochiladi: hub'ning butun ma'nosi "barcha chatlar bitta
 * joyda" bo'lgani uchun, ro'yxat va suhbat orasida sakrash tez bo'lishi
 * kerak. Guruh sahifasidagi "Chat" tabi ham ishlaydi va o'sha suhbatni
 * ko'rsatadi — ikki yo'l bir joyga olib boradi.
 *
 * Suhbat ALOHIDA MARSHRUT emas, sahifa ichidagi holat: eski ilovada ham
 * shunday edi va "orqaga" tugmasi brauzer tarixini chat bilan to'ldirmaydi.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * ★ 2026-08-13 (R28) — DESKTOPDA (≥1024px) IKKI USTUN, O'QUVCHI CHATIDAGIDEK
 * ══════════════════════════════════════════════════════════════════════════
 *
 * Talab: *"teacher chat qismi student chat qismi qoidalari bilan bir xil
 * bo'lsin"*. Bu yerda ro'yxat suhbat ochilganda DOM'dan butunlay chiqib
 * ketardi (`v-if="active === null"`), ya'ni 1600px lik ekranning chap yarmi
 * bo'shab qolar va har suhbat almashuvi "Orqaga" bosishni talab qilardi.
 * Endi joylashuv `StudentChatPage` bilan bir xil (`docs/MOSLASHUVCHANLIK.md`
 * 6.3: `lg:grid-cols-[340px_minmax(0,1fr)]`): ro'yxat DOIM chapda, suhbat
 * o'ngda almashadi.
 *
 * ★ TELEFON YO'LI O'ZGARMADI va buni CSS kafolatlaydi: ro'yxat ustuni
 * suhbat ochilganda `hidden lg:flex`, bo'sh holat esa `hidden lg:flex` —
 * ya'ni <1024px da baribir HAR DOIM bittasi ko'rinadi. Holat modeli
 * (`active`) o'zgarmadi.
 *
 * ★ YAGONA HAQIQIY FARQ (ko'rinmaydigan): suhbat ochilganda ro'yxat endi
 * DOM'dan chiqmaydi, faqat `display:none` bo'ladi — ya'ni uning 30
 * sekundlik so'rovi telefonda ham davom etadi. Bu o'quvchi sahifasida
 * ataylab qabul qilingan qaror va sabab o'sha: `v-if` bilan yechish uchun
 * JS chegara tekshiruvi kerak bo'lardi, u esa "desktop chegarasining
 * yagona hakami CSS `lg:`" qoidasini buzardi. Yon foydasi ham bir xil —
 * "Orqaga" bosilganda ro'yxat yangilangan va skroll joyi saqlangan holda
 * qaytadi.
 *
 * 🔴 SETKA CHEGARASI VA "ORQAGA" TUGMASINING CHEGARASI BIR XIL BO'LISHI
 *    SHART — ikkalasi ham `lg`. Ular ajralib qolsa oraliqdagi kengliklarda
 *    ro'yxat ham, chiqish tugmasi ham yo'qolib, foydalanuvchi suhbatda
 *    QAMALIB qolardi (`docs/MOSLASHUVCHANLIK.md` 3-bo'lim, 7-qator —
 *    aynan shu xato `InboxThread` da bo'lgan).
 */
const active = ref<GroupChatThreadDto | null>(null)

const title = computed(() => active.value?.groupName ?? '')

/** Ro'yxatda qaysi qator ochiqligini KO'RSATISH — ikki ustunli ko'rinish sharti. */
const selectedKey = computed(() =>
  active.value === null ? null : threadKey(active.value.groupId, active.value.channel),
)

function open(thread: GroupChatThreadDto): void {
  /*
    ★ "ALLAQACHON OCHIQ" TEKSHIRUVI — DESKTOP TUG'DIRGAN SHART (o'quvchi
    sahifasidagi bilan bir xil sabab): ro'yxat endi doim ko'rinib turadi va
    ochiq suhbatning qatorini ikkinchi marta bosish oson. Qayta yozuv
    `GroupChatRoom` ning `:key` ini o'zgartirmaydi, lekin ortiqcha holat
    yozuvini umuman qilmaslik aniqroq.
  */
  const current = active.value
  if (
    current !== null &&
    current.groupId === thread.groupId &&
    current.channel === thread.channel
  ) {
    return
  }
  active.value = thread
}

/**
 * SETKANING BALANDLIGI — o'lchanadi, sanalmaydi (`useFillHeight` izohi).
 *
 * ★ NEGA CSS O'ZGARUVCHISI, TO'G'RIDAN-TO'G'RI `height` EMAS: inline
 * `style` ni media so'rovga o'rab bo'lmaydi, chegara esa FAQAT desktopda
 * kerak. Telefonda sahifa avvalgidek hujjat bilan skrollanishi shart —
 * `lg:h-[var(--zn-chat-fill)]` aynan shuni beradi: o'zgaruvchi hamma
 * kenglikda yoziladi, lekin uni FAQAT `lg:` dagi qoida o'qiydi.
 *
 * Suhbat ustuni o'z balandligini `ChatFillColumn` dan oladi (telefon uchun),
 * desktopda esa `lg:h-full!` bilan setka katagiga o'tadi — o'quvchi
 * sahifasidagi bilan AYNAN bir xil naqsh.
 */
const grid = ref<HTMLElement | null>(null)
const fillHeight = useFillHeight(grid)
</script>

<template>
  <div>
    <!--
      Sarlavha TELEFONDA suhbat ochilganda yashiriladi — bugungi xulq
      (u ilgari ro'yxat shoxining ichida turardi). Desktopda esa doim
      ko'rinadi: u yerda ro'yxat ham, suhbat ham bir vaqtda ekranda.

      ★ O'RAM `div` SHART: `hidden lg:block` ni to'g'ridan-to'g'ri
      `PageHeader` ga bersak, uning ildizidagi `flex` bilan `block`
      to'qnashardi (ikkalasi ham `display`).
    -->
    <div :class="{ 'hidden lg:block': active !== null }">
      <PageHeader
        title="💬 Chatlar"
        subtitle="Barcha guruhlaringiz chatlari. Kirib, erkin yozishingiz mumkin."
      />
    </div>

    <div
      ref="grid"
      class="lg:grid lg:h-[var(--zn-chat-fill)] lg:grid-cols-[340px_minmax(0,1fr)]"
      :style="{ '--zn-chat-fill': fillHeight }"
    >
      <!-- ========================= CHAP USTUN: RO'YXAT ======================= -->
      <!--
        `hidden` FAQAT telefonda ishlaydi: `lg:flex` media so'rovi ichida
        turgani uchun ≥1024px da undan kuchli. Ya'ni bitta shart ikki xulq
        beradi — telefonda "ro'yxat o'rnini suhbat egalladi", desktopda
        "ro'yxat joyida qoldi".
      -->
      <section
        class="min-w-0 lg:flex lg:h-full lg:min-h-0 lg:flex-col lg:border-r lg:border-line lg:pr-5"
        :class="{ hidden: active !== null }"
        aria-label="Chatlar ro‘yxati"
      >
        <!--
          ★ IKKINCHI SKROLL SOHASI (faqat desktopda): uzun ro'yxatni ko'rish
          uchun ochiq suhbatni yo'qotish kerak emas. Telefonda bu oddiy
          `div` — sahifaning o'zi skrollanadi.
        -->
        <div class="lg:min-h-0 lg:flex-1 lg:overflow-y-auto lg:pr-1 lg:scrollbar-slim">
          <!--
            ★ `filterable` (R38): guruh TURI va YO'NALISHI bo'yicha filtr.
            AYNAN shu ekranda yoqilgan — u yerda qidiruv ham bor va sabab
            bir xil: ustoz/kurator o'nlab guruhga ega. O'quvchi sahifasida
            ataylab o'chiq (sabab `GroupChatThreadList` prop izohida).

            🔴 Filtr SERVERDA ishlaydi: ro'yxat 200 qatorda kesiladi va
            mijozdagi filtr undan keyingi guruhlarni ko'rmasdi.
          -->
          <GroupChatThreadList
            searchable
            filterable
            empty-title="Guruh topilmadi"
            empty-text="Sizga guruh biriktirilgach, uning chati shu yerda ochiladi."
            :selected-key="selectedKey"
            @open="open"
          />
        </div>
      </section>

      <!-- ============================ Ochiq suhbat ============================ -->
      <!--
        ★ USTUN BALANDLIGI BIR MARTA CHEGARALANADI (2026-08-13, talab:
        *"chat writing part should be stuck in its place"*). Ilgari bu yerda
        hech qanday chegara yo'q edi: `GroupChatRoom` sukut bo'yicha qat'iy
        balandlikdagi ro'yxat chizardi va yozish paneli uning OSTIDA, ya'ni
        ko'pincha ekran tashqarisida qolardi.

        "Orqaga" qatori ATAYLAB ustun ICHIDA: u ham suhbatning bir qismi va
        `shrink-0` sifatida o'z balandligini o'zi belgilaydi — hech kim uni
        piksel bilan sanamaydi.
      -->
      <ChatFillColumn
        v-if="active !== null"
        class="lg:h-full! lg:min-h-0 lg:pl-5"
      >
        <div class="mb-3.5 flex shrink-0 items-center gap-3">
          <!--
            🔴 Fon `bg-white/[0.06]` + `hover:bg-white/[0.12]` edi: oq
            kartochkada oq ustiga 6% oq = 1.02:1, ya'ni tugma UMUMAN
            ko'rinmasdi (matn va ikonka "havoda" turardi) va hover hech qanday
            javob bermasdi.

            Naqsh `StudentChatPage` dan olindi — u yerda aynan shu tugma
            allaqachon shunday tuzatilgan: oq sirt + `line-strong` kontur +
            `ink-800` hover (`BaseButton` ning `secondary` varianti bilan bir
            xil qoida).

            ★ `lg:hidden` (2026-08-13, R28): desktopda ro'yxat chapda turibdi,
            ya'ni tugma hech qayerga qaytarmaydi — bosilgach ekranning yarmi
            bo'shab qolardi. Telefonda esa u YAGONA chiqish yo'li (marshrut
            o'zgarmagani uchun brauzerning "orqaga" tugmasi bu yerda
            ishlamaydi). Chegara setkaniki bilan bir xil bo'lishi SHART —
            sababi skriptdagi izohda.
          -->
          <button
            type="button"
            class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800 lg:hidden"
            @click="active = null"
          >
            <AppIcon
              name="arrow-left"
              :size="15"
            />
            Orqaga
          </button>
          <!--
            `h2`, `h1` EMAS: desktopda sahifa sarlavhasi (`PageHeader` ning
            `h1` i) ekranda BIR VAQTDA turadi va ikkinchi `h1` hujjat
            tuzilishini buzardi. Ko'rinishi bir piksel ham o'zgarmadi.
          -->
          <h2
            class="min-w-0 flex-1 truncate text-lg font-bold tracking-tight"
            v-text="title"
          />
        </div>

        <!--
          ★ `:key` — guruh VA kanal bo'yicha. Boshqa suhbatga o'tilganda
          komponent QAYTA yaratiladi: aks holda eski suhbatning skroll joyi,
          yozib qo'yilgan matni va hub holati yangisiga o'tib ketardi.
        -->
        <GroupChatRoom
          :key="`${active.groupId}:${active.channel}`"
          class="min-h-0 flex-1"
          :group-id="active.groupId"
          :group-name="active.groupName"
          :channel="active.channel"
        />
      </ChatFillColumn>

      <!-- ================= O'NG USTUN: HECH NARSA TANLANMAGAN ================= -->
      <!--
        ★ FAQAT DESKTOPDA MAVJUD HOLAT (`hidden lg:flex`). Telefonda "hech
        narsa tanlanmagan" degani RO'YXATNING O'ZI ko'rinib turgani demak —
        u yerda bo'sh o'ng ustun yo'q, ya'ni bu blok hech qachon chizilmaydi
        va parite shartnomasiga tegmaydi (yangi matn ham FAQAT desktopda
        ko'rinadi). O'quvchi chatidagi bilan bir xil ko'rinish va bir xil
        ohang: nima yo'qligini emas, NIMA QILISH kerakligini aytadi.
      -->
      <div
        v-else
        class="hidden lg:flex lg:h-full lg:min-h-0 lg:items-center lg:justify-center lg:pl-5"
      >
        <EmptyState
          class="w-full max-w-[420px]"
          icon="chat"
          title="Suhbat tanlanmagan"
          text="Chapdagi ro‘yxatdan guruh chatini tanlang — yozishma shu yerda ochiladi."
        />
      </div>
    </div>
  </div>
</template>
