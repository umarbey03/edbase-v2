<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { groupDisplayName } from '@/entities/group'
import { sendGroupBroadcast } from '@/entities/group-broadcast'
import { fetchMessageTemplates } from '@/entities/message-template'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { GroupDto } from '@/shared/types'
import { BaseBadge, BaseButton, BaseCard, BaseField } from '@/shared/ui'

import BroadcastGroupPicker from './BroadcastGroupPicker.vue'

/**
 * "Xabarlar" paneli — YUBORISH qismi (2026-08-16).
 *
 * Ikki mustaqil portga tayanadi (`GroupBroadcastService` izohi): Telegram DM
 * va platforma chati. Kamida BITTASI tanlanishi shart — server ham buni
 * 400 bilan qaytaradi (`sendToTelegram`/`sendToPlatformChat` ikkalasi ham
 * `false` bo'lsa), shu tekshiruv mijozda TAKRORLANADI, aks holda foydalanuvchi
 * so'rov yuborib, keyin nima xato ekanini o'qishga majbur bo'lardi.
 *
 * ★ MAKSIMAL UZUNLIK `4096` — `NotificationText.MaxBodyLength` bilan AYNI
 * (backend Telegram xabar hajmi chegarasi). Qattiq kodlangan: bu qiymat
 * o'zgarmas Telegram API cheklovi, so'rov orqali olinadigan sozlama emas.
 *
 * ★ `fixedGroup` — GURUH ICHIDAGI "Xabar" tabidan chaqirilganda (loyiha
 * egasi, 2026-08-16: *"xabar yuborish har bir guruh ichida ham bo'lishi
 * kerak"*). Sahifa allaqachon BITTA guruh ichida turibdi, shuning uchun
 * qidiruv+belgilash paneli (`BroadcastGroupPicker`) ORTIQCHA — guruh
 * O'ZGARMAS holda ko'rsatiladi, tanlov qulfini yechish uchun tugma YO'Q
 * (markaziy "Xabarlar" panelidan farqli, bu yerda "boshqa guruhga ham
 * yuboraymi" savoli tug'ilmaydi — xodim aynan SHU guruh sahifasida).
 */
const MAX_BODY_LENGTH = 4096

/**
 * ★ `bare` — markaziy "Xabarlar" panelida (2026-08-16, loyiha egasi:
 * "xabar yuborish formasi modal holatida ochilishi kerak") komponent
 * `BaseModal` ICHIDA ochiladi — o'zining `BaseCard`/sarlavhasi bo'lsa,
 * modal ichida ikkinchi ramka/sarlavha paydo bo'lardi (ikki qavat quti).
 * `BroadcastTab.vue`dagi inline chaqiruv `bare` bermaydi — u yerda
 * xatti-harakat o'zgarmaydi (o'z `BaseCard`si bilan sahifa ichida turadi).
 */
const props = defineProps<{ fixedGroup?: GroupDto; bare?: boolean }>()
const emit = defineEmits<{ sent: [] }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

/* ------------------------------------------------------------- shablon */

const templatesQuery = useQuery({
  queryKey: ['message-templates', 'active'],
  queryFn: ({ signal }) => fetchMessageTemplates({ isActive: true }, { signal }),
})

const templates = computed(() => templatesQuery.data.value ?? [])

const templateId = ref<number | null>(null)

/**
 * Shablon tanlansa matn maydoni O'SHA shablon bilan TO'LDIRILADI — keyin
 * xodim uni tahrirlab yuborishi mumkin (server matnni QAYTA o'qimaydi,
 * `SendGroupBroadcastRequest.Body` izohi).
 */
function onTemplateChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  templateId.value = value.length > 0 ? Number(value) : null

  const template = templates.value.find((t) => t.id === templateId.value)
  if (template !== undefined) body.value = template.body
}

/* --------------------------------------------------------------- forma */

const selectedGroups = ref<GroupDto[]>(props.fixedGroup !== undefined ? [props.fixedGroup] : [])
const body = ref('')
const sendToTelegram = ref(true)
const sendToPlatformChat = ref(true)
const formError = ref<string | null>(null)

function resetForm(): void {
  selectedGroups.value = props.fixedGroup !== undefined ? [props.fixedGroup] : []
  body.value = ''
  templateId.value = null
  sendToTelegram.value = true
  sendToPlatformChat.value = true
}

const sendMutation = useMutation({
  mutationFn: () =>
    sendGroupBroadcast({
      groupIds: selectedGroups.value.map((group) => group.id),
      body: body.value.trim(),
      templateId: templateId.value,
      sendToTelegram: sendToTelegram.value,
      sendToPlatformChat: sendToPlatformChat.value,
    }),
  onSuccess: () => {
    resetForm()
    formError.value = null
    void queryClient.invalidateQueries({ queryKey: ['group-broadcasts'] })
    emit('sent')
  },
  onError: (error: unknown) => {
    formError.value = toUserMessage(error)
  },
})

/**
 * Yuborishdan OLDIN tasdiq — QAYTARIB BO'LMAYDIGAN amal (Telegram DM
 * navbatga qo'yiladi va matn allaqachon yozilib bo'ladi), qatorlar soni
 * o'nlab o'quvchiga yetishi mumkin.
 */
async function askSend(): Promise<void> {
  formError.value = null

  if (selectedGroups.value.length === 0) {
    formError.value = 'Kamida bitta guruh tanlang.'
    return
  }

  if (body.value.trim().length === 0) {
    formError.value = 'Xabar matnini kiriting.'
    return
  }

  if (!sendToTelegram.value && !sendToPlatformChat.value) {
    formError.value = 'Yuborish kanalini tanlang — Telegram yoki platforma chatidan kamida bittasi.'
    return
  }

  const channels: string[] = []
  if (sendToTelegram.value) channels.push('Telegram (har a’zoga shaxsiy xabar)')
  if (sendToPlatformChat.value) channels.push('Platforma chati (guruh oqimi)')

  const ok = await confirm({
    title: 'Xabar yuborish',
    message: `${selectedGroups.value.length} ta guruhga xabar yuboriladi.`,
    confirmLabel: 'Yuborish',
    tone: 'warning',
    details: [
      `Guruhlar: ${selectedGroups.value.map(groupDisplayName).join(', ')}`,
      `Kanal: ${channels.join(', ')}`,
      'Yuborilgandan keyin xabarni bekor qilib bo‘lmaydi.',
    ],
  })
  if (!ok) return

  sendMutation.mutate()
}

const bodyCount = computed(() => body.value.length)
</script>

<template>
  <component
    :is="props.bare === true ? 'div' : BaseCard"
    v-bind="props.bare === true ? {} : { title: 'Yangi xabar' }"
  >
    <div class="space-y-3.5 p-3.5 sm:p-5">
      <BaseField
        v-if="props.fixedGroup !== undefined"
        label="Guruh"
      >
        <div class="flex items-center gap-2 rounded-lg border border-line bg-ink-950 px-3 py-2.5">
          <span
            class="min-w-0 flex-1 truncate text-sm text-slate-200"
            v-text="groupDisplayName(props.fixedGroup)"
          />
          <BaseBadge tone="neutral">
            {{ props.fixedGroup.memberCount }} o‘quvchi
          </BaseBadge>
        </div>
      </BaseField>
      <BaseField
        v-else
        label="Guruhlar"
      >
        <BroadcastGroupPicker v-model="selectedGroups" />
      </BaseField>

      <BaseField
        label="Shablon (ixtiyoriy)"
        hint="Tanlansangiz matn maydoni shablon bilan to‘ldiriladi — keyin tahrirlashingiz mumkin"
      >
        <select
          class="zn-input"
          :value="templateId ?? ''"
          @change="onTemplateChange"
        >
          <option value="">
            Shablonsiz — o‘zim yozaman
          </option>
          <option
            v-for="template in templates"
            :key="template.id"
            :value="template.id"
          >
            {{ template.name }}
          </option>
        </select>
      </BaseField>

      <BaseField
        label="Xabar matni"
        :hint="`${bodyCount} / ${MAX_BODY_LENGTH}`"
      >
        <textarea
          v-model="body"
          class="zn-input"
          rows="6"
          :maxlength="MAX_BODY_LENGTH"
          placeholder="Xabar matnini kiriting..."
        />
      </BaseField>

      <div>
        <span class="mb-1.5 block text-xs font-medium text-slate-400">Yuborish kanali</span>
        <div class="flex flex-wrap gap-4">
          <label class="flex min-h-11 items-center gap-2 text-sm text-slate-300">
            <input
              v-model="sendToTelegram"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Telegram (shaxsiy xabar)
          </label>
          <label class="flex min-h-11 items-center gap-2 text-sm text-slate-300">
            <input
              v-model="sendToPlatformChat"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Platforma chati
          </label>
        </div>
      </div>

      <p
        v-if="formError !== null"
        class="rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-xs text-rose-200"
        role="alert"
        v-text="formError"
      />

      <div class="flex justify-end">
        <BaseButton
          :loading="sendMutation.isPending.value"
          @click="askSend"
        >
          Yuborish
        </BaseButton>
      </div>
    </div>
  </component>
</template>
