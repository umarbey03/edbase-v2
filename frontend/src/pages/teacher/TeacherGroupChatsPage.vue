<script setup lang="ts">
import { computed, ref } from 'vue'

import { GroupChatRoom, GroupChatThreadList } from '@/features/group-chat'
import type { GroupChatThreadDto } from '@/shared/types'
import { AppIcon, PageHeader } from '@/shared/ui'

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
 */
const active = ref<GroupChatThreadDto | null>(null)

const title = computed(() => active.value?.groupName ?? '')

function open(thread: GroupChatThreadDto): void {
  active.value = thread
}
</script>

<template>
  <div>
    <!-- ============================== Ro'yxat =============================== -->
    <template v-if="active === null">
      <PageHeader
        title="💬 Chatlar"
        subtitle="Barcha guruhlaringiz chatlari. Kirib, erkin yozishingiz mumkin."
      />

      <GroupChatThreadList
        searchable
        empty-title="Guruh topilmadi"
        empty-text="Sizga guruh biriktirilgach, uning chati shu yerda ochiladi."
        @open="open"
      />
    </template>

    <!-- ============================ Ochiq suhbat ============================ -->
    <template v-else>
      <div class="mb-3.5 flex items-center gap-3">
        <!--
          🔴 Fon `bg-white/[0.06]` + `hover:bg-white/[0.12]` edi: oq
          kartochkada oq ustiga 6% oq = 1.02:1, ya'ni tugma UMUMAN
          ko'rinmasdi (matn va ikonka "havoda" turardi) va hover hech qanday
          javob bermasdi.

          Naqsh `StudentChatPage` dan olindi — u yerda aynan shu tugma
          allaqachon shunday tuzatilgan: oq sirt + `line-strong` kontur +
          `ink-800` hover (`BaseButton` ning `secondary` varianti bilan bir
          xil qoida).
        -->
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800"
          @click="active = null"
        >
          <AppIcon
            name="arrow-left"
            :size="15"
          />
          Orqaga
        </button>
        <h1
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
        :group-id="active.groupId"
        :group-name="active.groupName"
        :channel="active.channel"
      />
    </template>
  </div>
</template>
