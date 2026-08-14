<script setup lang="ts">
import { computed } from 'vue'

import { roleLabel, roleTone } from '@/entities/user'
import type { GroupChatAttachmentDto } from '@/shared/types'
import { BaseAvatar, BaseBadge } from '@/shared/ui'

import ChatAttachment from './ChatAttachment.vue'

/**
 * Guruh chatining bitta qatori — eski ilovadagi `.mrow` / `.mbub` tuzilishi:
 * avatar chapda, pufakcha ichida ism + rol nishoni + vaqt, o'z xabari o'ngda
 * (`.mrow.mine`, `flex-direction: row-reverse`).
 *
 * ★ FAQAT PRIMITIV PROP'LAR — `features/chat/ui/ChatMessageRow.vue` dagi
 * qoidaning o'zi: Vue prop'larni sayoz taqqoslaydi, shuning uchun primitivlar
 * o'zgarmaganda ro'yxat qayta chizilsa ham ESKI qatorlar umuman yangilanmaydi.
 * Bu yerga `message` obyekti berilganida har render'da yangi havola bo'lib,
 * yuzlab qator qaytadan patch qilinardi.
 */
const props = withDefaults(
  defineProps<{
    senderName: string
    body: string
    time: string
    isOwn: boolean
    /** Ketma-ket xabarlarda avatar va ism takrorlanmaydi. */
    showHeader: boolean
    role: string
    /**
     * R16b · biriktirmalar.
     *
     * ⚠️ YUQORIDAGI "FAQAT PRIMITIV PROP'LAR" QOIDASIDAN ONGLI CHEKINISH.
     * Sabab: biriktirmalar RO'YXAT va uni primitivga aylantirishning
     * ishonchli yo'li yo'q (id'larni satrga yopishtirish `kind` va nomni
     * yo'qotardi). Narx CHEKLANGAN: massiv havolasi FAQAT xabar obyekti
     * almashganda o'zgaradi (`useGroupChatRows` `computed` — har render'da
     * yangi obyekt yasamaydi), ya'ni qayta chizish soni oshmaydi. Sukut
     * qiymati BO'SH massiv — ro'yxatning katta qismi biriktirmasiz va
     * ular uchun hech nima chizilmaydi.
     */
    attachments?: readonly GroupChatAttachmentDto[]
  }>(),
  { role: '', attachments: () => [] },
)

/** Rasm kattalashtirilishi so'raldi — oynani ota komponent chizadi. */
const emit = defineEmits<{ zoom: [url: string] }>()

const roleText = computed(() => (props.role.length > 0 ? roleLabel(props.role) : ''))
const tone = computed(() => roleTone(props.role))

/**
 * Rol nishoni FAQAT xodimlarda ko'rsatiladi — eski ilovadagidek
 * (`m.role === 'teacher' ? Ustoz : m.role === 'assistant' ? Kurator : ''`).
 * Guruhdagi o'quvchilarning har biriga "O'quvchi" yorlig'i osilsa, u
 * ma'lumot bermay, faqat shovqin bo'lardi.
 */
const showRole = computed(() => props.role === 'Teacher' || props.role === 'Assistant')
</script>

<template>
  <div
    class="flex gap-2"
    :class="[props.isOwn ? 'flex-row-reverse' : '', props.showHeader ? 'mt-3' : 'mt-0.5']"
  >
    <!-- O'z xabarida avatar chizilmaydi (eski `.mrow.mine .mava { display: none }`). -->
    <div
      v-if="!props.isOwn"
      class="w-8 shrink-0"
    >
      <BaseAvatar
        v-if="props.showHeader"
        :name="props.senderName"
        size="sm"
      />
    </div>

    <div
      class="flex min-w-0 max-w-[82%] flex-col"
      :class="props.isOwn ? 'items-end' : 'items-start'"
    >
      <div
        class="max-w-full rounded-2xl px-3 py-1.5 shadow-sm"
        :class="
          props.isOwn
            ? 'rounded-br-sm bg-brand-500 text-on-brand'
            : 'rounded-bl-sm border border-line bg-ink-900 text-slate-100'
        "
      >
        <!--
          Ism + rol nishoni — FAQAT boshqaning xabarida. Eski ilova ham
          o'z xabarida ismni yashirardi (`.mrow.mine .mname { display: none }`):
          guruh chatida "kim yozdi" savoli faqat boshqalar uchun ma'noli.
        -->
        <div
          v-if="props.showHeader && !props.isOwn"
          class="mb-0.5 flex min-w-0 items-center gap-1.5"
        >
          <span
            class="truncate text-[12.5px] font-bold text-brand-300"
            v-text="props.senderName"
          />
          <BaseBadge
            v-if="showRole"
            :tone="tone"
            size="xs"
          >
            {{ roleText }}
          </BaseBadge>
        </div>

        <!--
          ★ `v-text` — mazmun HTML sifatida HECH QACHON talqin qilinmaydi.
          `v-html` loyihada qat'iyan taqiqlangan (eslint `vue/no-v-html`):
          bu yerdagi matnni O'QUVCHI yozadi va u boshqa o'quvchilarga hamda
          ustozga ko'rinadi — ya'ni saqlangan XSS uchun mukammal joy bo'lardi.
        -->
        <p
          v-if="props.body.length > 0"
          class="whitespace-pre-wrap break-words text-sm leading-relaxed"
          v-text="props.body"
        />

        <!--
          ★ R16b · BIRIKTIRMALAR. Matn BO'SH bo'lishi mumkin (izohsiz surat,
          Telegram'dagi kabi) — shuning uchun yuqoridagi `<p>` da `v-if` bor.
          Bo'sh `<p>` qoldirilsa, rasm ustida sababsiz bo'sh qator turardi.
        -->
        <div
          v-if="props.attachments.length > 0"
          class="mt-0.5"
          :class="props.body.length > 0 ? 'mt-1.5' : ''"
        >
          <ChatAttachment
            v-for="item in props.attachments"
            :key="item.id"
            :attachment="item"
            @zoom="(url) => emit('zoom', url)"
          />
        </div>

        <!--
          Vaqt. O'z xabarimda oltin fon ustida turadi, shuning uchun rang
          `text-on-brand/70` — `text-slate-500` u yerda ko'rinmasdi. Eski
          ilovada ham shunday: `.mrow.mine .mtime { color: rgba(16,36,58,.7) }`.
          ★ `text-white` ATAYLAB ISHLATILMAYDI: o'quvchi temasida brend oltin
          (#f5b731) va oq matn kontrasti ~1.9:1 — o'qilmaydi.
        -->
        <span
          class="mt-0.5 block text-right text-[10.5px] tabular-nums"
          :class="props.isOwn ? 'text-on-brand/70' : 'text-dim'"
          v-text="props.time"
        />
      </div>
    </div>
  </div>
</template>
