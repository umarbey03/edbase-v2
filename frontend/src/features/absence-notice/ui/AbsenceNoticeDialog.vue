<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { sendAbsenceNotices } from '@/entities/absentee'
import { fetchMessageTemplates } from '@/entities/message-template'
import { toUserMessage } from '@/shared/api'
import { showToast } from '@/shared/lib/useToast'
import type { AbsenceNoticeTarget } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KELMAGANLARGA XABAR YOZISH (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ O'RIN EGALLOVCHILAR — BU OYNANING BUTUN MA'NOSI: "Assalomu alaykum"
 * bilan boshlangan bir xil matn 40 kishiga ketsa, u e'longa o'xshab
 * qoladi va o'qilmaydi. Ism, guruh va dars sanasi qo'yilgan xabar esa
 * aynan o'sha o'quvchiga qaratilgan bo'ladi. Almashtirish SERVERDA
 * bajariladi — har oluvchi uchun alohida matn.
 *
 * ★ NAMUNA KO'RSATILADI: operator `{ism}` nima ekanini o'qib emas,
 * KO'RIB tushunsin. Namuna birinchi tanlangan o'quvchi bo'yicha —
 * qolganlari ham xuddi shunday chiqadi.
 *
 * ★ TELEGRAMI YO'QLAR HAQIDA OGOHLANTIRILADI: ularga xabar bormaydi va
 * kurator buni YUBORISHDAN OLDIN bilishi kerak, keyin emas.
 */
const props = defineProps<{
  open: boolean
  targets: AbsenceNoticeTarget[]
  /** Namuna uchun: birinchi tanlangan o'quvchining ma'lumoti. */
  sampleName?: string
  sampleGroup?: string
  sampleDate?: string
  sampleTime?: string
  /** Telegrami ulanmagan tanlanganlar soni. */
  withoutTelegram?: number
}>()

const emit = defineEmits<{ close: []; sent: [] }>()

const queryClient = useQueryClient()

const DEFAULT_BODY =
  'Assalomu alaykum, {ism}!\n\n'
  + 'Siz {sana} kuni soat {vaqt} dagi {guruh} guruhi darsiga qatnashmadingiz.\n'
  + 'Iltimos, sababini kuratoringizga bildiring va keyingi darsni qoldirmang.'

const body = ref(DEFAULT_BODY)
const templateId = ref<number | ''>('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  (open) => {
    if (!open) return

    body.value = DEFAULT_BODY
    templateId.value = ''
    errorMessage.value = null
  },
)

const templatesQuery = useQuery({
  queryKey: ['message-templates', 'active'],
  queryFn: ({ signal }) => fetchMessageTemplates({ isActive: true }, { signal }),
  enabled: computed(() => props.open),
})

const templates = computed(() => templatesQuery.data.value ?? [])

// Shablon tanlansa matn ALMASHTIRILADI: shablon — boshlang'ich nuqta,
// operator uni keyin tahrirlaydi (mavjud guruh xabarnomasidagi AYNI xulq).
watch(templateId, (value) => {
  if (value === '') return

  const picked = templates.value.find((t) => t.id === value)

  if (picked !== undefined) body.value = picked.body
})

/** Serverdagi almashtirish bilan AYNI qoida — faqat ko'rsatish uchun. */
const preview = computed(() =>
  body.value
    .replaceAll('{ism}', props.sampleName ?? 'Ism Familiya')
    .replaceAll('{guruh}', props.sampleGroup ?? 'Guruh')
    .replaceAll('{sana}', props.sampleDate ?? '01.01.2026')
    .replaceAll('{vaqt}', props.sampleTime ?? '09:00')
    .replaceAll('{ustoz}', 'Ustoz ismi'),
)

const sendMutation = useMutation({
  mutationFn: () =>
    sendAbsenceNotices({
      targets: props.targets,
      body: body.value.trim(),
      ...(templateId.value === '' ? {} : { templateId: templateId.value }),
    }),
  onSuccess: (result) => {
    void queryClient.invalidateQueries({ queryKey: ['absence-notices'] })

    // ★ NATIJA ROSTINI AYTADI: nechtasi navbatga tushdi va nechtasiga
    //   umuman bormadi. "Yuborildi" deb yumaloq javob berish kuratorni
    //   chalg'itardi.
    const parts = [`${result.sent} ta xabar yozildi`]

    if (result.queued > 0) parts.push(`${result.queued} tasi Telegramga`)
    if (result.withoutTelegram > 0) parts.push(`${result.withoutTelegram} tasida Telegram yo‘q`)

    showToast(parts.join(' · '), result.withoutTelegram > 0 ? 'warning' : 'success')

    emit('sent')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function handleSubmit(): void {
  errorMessage.value = null

  if (body.value.trim().length === 0) {
    errorMessage.value = 'Xabar matnini kiriting.'
    return
  }

  if (props.targets.length === 0) {
    errorMessage.value = 'Kamida bitta o‘quvchini tanlang.'
    return
  }

  sendMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="`Xabar yuborish — ${props.targets.length} ta o‘quvchi`"
    @close="emit('close')"
  >
    <p
      v-if="(props.withoutTelegram ?? 0) > 0"
      class="mb-3 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
    >
      Tanlanganlardan <span class="font-semibold">{{ props.withoutTelegram }} tasida</span>
      Telegram ulanmagan — ularga xabar bormaydi, qo‘ng‘iroq qilish kerak.
      Yozuv baribir saqlanadi.
    </p>

    <div class="space-y-3">
      <BaseField
        label="Tayyor shablon"
        hint="Tanlansa matn almashtiriladi, keyin tahrirlash mumkin."
      >
        <select
          v-model="templateId"
          class="zn-input"
        >
          <option value="">
            — Shablonsiz —
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

      <BaseField label="Xabar matni">
        <textarea
          v-model="body"
          class="zn-input min-h-32"
          maxlength="2000"
          rows="6"
        />
      </BaseField>

      <!--
        O'rin egallovchilar ro'yxati — matn maydoni OSTIDA, chunki
        operator yozayotganda ularga qarab turadi.
      -->
      <p class="text-[11px] text-dim">
        O‘rin egallovchilar (har o‘quvchi uchun alohida to‘ldiriladi):
        <code class="text-slate-300">{ism}</code>
        <code class="ml-1 text-slate-300">{guruh}</code>
        <code class="ml-1 text-slate-300">{sana}</code>
        <code class="ml-1 text-slate-300">{vaqt}</code>
        <code class="ml-1 text-slate-300">{ustoz}</code>
      </p>

      <div class="rounded-xl border border-line bg-ink-800 p-3.5">
        <p class="mb-1.5 text-[11px] font-semibold text-slate-400">
          Namuna — {{ props.sampleName ?? 'birinchi o‘quvchi' }} uchun
        </p>
        <p
          class="whitespace-pre-wrap text-sm text-slate-200"
          v-text="preview"
        />
      </div>
    </div>

    <p
      v-if="errorMessage !== null"
      class="mt-3 text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :loading="sendMutation.isPending.value"
        @click="handleSubmit"
      >
        Yuborish
      </BaseButton>
    </template>
  </BaseModal>
</template>
