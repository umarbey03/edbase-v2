<script setup lang="ts">
import { computed } from 'vue'

import { telegramHandle, telegramLink } from '@/entities/user'
import { formatDateTime } from '@/shared/lib/datetime'
import type { ProfileTelegramDto, UserDetailsDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard } from '@/shared/ui'

/**
 * 1-BO'LIM: SHAXSIY MA'LUMOTLAR + Telegram holati.
 *
 * Tartib loyiha egasi bergan ro'yxatdan: ism · telefon · email · Telegram
 * (nom va id) · ulanish holati.
 *
 * 🔴 TELEGRAM NOMI FAQAT HAVOLA (13-bo'lim, 35-tuzoq): bo'shatilgan nom
 * boshqa odamga o'tadi, ya'ni u SHAXSNI ANIQLAMAYDI. Shu sababli nom yonida
 * DOIM `telegramId` ko'rsatiladi — xodim ishonchli identifikatorni ko'rib
 * turishi kerak.
 */
const props = defineProps<{
  user: UserDetailsDto
  telegram: ProfileTelegramDto
  /**
   * "Uzish" tugmasi ko'rinadimi — faqat `Academic`/`Admin`.
   * ⚠️ KO'RINISH darvozasi; serverda ham `[Authorize(Roles="Academic,Admin")]`.
   */
  canUnlink: boolean
}>()

const emit = defineEmits<{ unlink: [] }>()

/** Bo'sh satr ham "yo'q" hisoblanadi: server `""` yubormaydi, lekin himoya arzon. */
function textOrDash(value: string | null): string {
  return value !== null && value.length > 0 ? value : '—'
}

const rows = computed(() => [
  { label: 'F.I.Sh.', value: textOrDash(props.user.fullName) },
  { label: 'Telefon', value: textOrDash(props.user.phone) },
  { label: 'Email', value: textOrDash(props.user.email) },
  {
    label: 'Telegram ID',
    value: props.telegram.telegramId === null ? '—' : String(props.telegram.telegramId),
  },
  { label: 'Hisob yaratilgan', value: formatDateTime(props.user.createdAt) },
])

/** Uzish izi — faqat xodimga keladi (`Student` rolida uchtasi ham `null`). */
const hasUnlinkTrace = computed(() => props.telegram.unlinkedAt !== null)
</script>

<template>
  <BaseCard title="Shaxsiy ma’lumotlar">
    <dl class="grid gap-3 sm:grid-cols-2">
      <div
        v-for="row in rows"
        :key="row.label"
      >
        <dt
          class="text-[11px] uppercase tracking-wide text-slate-400"
          v-text="row.label"
        />
        <dd
          class="mt-0.5 break-words text-sm text-slate-100"
          v-text="row.value"
        />
      </div>
    </dl>

    <!-- ------------------------------------------------------- Telegram -->
    <div class="mt-4 rounded-xl border border-line bg-ink-800 p-3.5">
      <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
        <span class="text-[11px] uppercase tracking-wide text-slate-400">
          Telegram
        </span>
        <BaseBadge
          :tone="props.telegram.linked ? 'success' : 'neutral'"
          dot
        >
          {{ props.telegram.linked ? 'Ulangan' : 'Ulanmagan' }}
        </BaseBadge>
        <span class="flex-1" />
        <BaseButton
          v-if="props.canUnlink && props.telegram.linked"
          size="sm"
          variant="danger"
          @click="emit('unlink')"
        >
          <template #icon>
            <AppIcon
              name="link-off"
              :size="14"
            />
          </template>
          Uzish
        </BaseButton>
      </div>

      <p
        v-if="props.telegram.linked"
        class="mt-2 text-sm text-slate-100"
      >
        <!--
          Havola `t.me` ga. `rel="noreferrer"` — tashqi sayt bizning
          manzilimizni bilmasin; `target="_blank"` bilan `noopener` MAJBURIY
          (aks holda yangi tab `window.opener` orqali sahifaga tegishi mumkin).
        -->
        <a
          v-if="props.telegram.username !== null"
          class="font-medium text-brand-400 underline decoration-brand-400/40 underline-offset-2 hover:text-brand-300"
          :href="telegramLink(props.telegram.username)"
          target="_blank"
          rel="noopener noreferrer"
          v-text="telegramHandle(props.telegram.username)"
        />
        <span
          v-else
          class="text-slate-400"
        >Nomi yo‘q (foydalanuvchi Telegram'da username qo‘ymagan)</span>
        <span
          v-if="props.telegram.linkedAt !== null"
          class="text-xs text-slate-400"
        >
          · {{ formatDateTime(props.telegram.linkedAt) }} da bog‘langan
        </span>
      </p>

      <!--
        ★ NOM ISHONCHSIZ EKANI AYNAN AYTILADI: xodim uni "shu odam" degan
        dalil sifatida ishlatmasin (bo'shatilgan nom boshqa odamga o'tadi).
      -->
      <p
        v-if="props.telegram.linked && props.telegram.username !== null"
        class="mt-1 text-[11px] leading-relaxed text-slate-400"
      >
        Telegram nomi vaqt o‘tib o‘zgaradi va bo‘shagan nom boshqa odamga o‘tishi
        mumkin — shaxsni Telegram ID bo‘yicha tekshiring.
      </p>

      <p
        v-if="!props.telegram.linked"
        class="mt-2 text-xs leading-relaxed text-slate-400"
      >
        Bog‘lanish FAQAT bot orqali tuziladi: o‘quvchi botga raqamini ulashadi.
        Bu yerda qo‘lda ulash imkoni ataylab yo‘q.
      </p>
    </div>

    <!-- ------------------------------------------------- uzish izi (audit) -->
    <div
      v-if="hasUnlinkTrace"
      class="mt-3 rounded-xl border border-amber-500/25 bg-amber-500/10 p-3.5"
    >
      <p class="text-xs font-semibold text-amber-200">
        Oxirgi uzish
      </p>
      <p class="mt-1 text-xs leading-relaxed text-amber-200">
        {{ formatDateTime(props.telegram.unlinkedAt ?? '') }}
        <template v-if="props.telegram.unlinkedByName !== null">
          · {{ props.telegram.unlinkedByName }}
        </template>
      </p>
      <p
        v-if="props.telegram.unlinkReason !== null"
        class="mt-1 text-xs leading-relaxed text-amber-200"
      >
        Sabab: <span v-text="props.telegram.unlinkReason" />
      </p>
    </div>
  </BaseCard>
</template>
