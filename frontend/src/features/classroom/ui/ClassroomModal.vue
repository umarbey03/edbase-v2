<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchClassroom } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseAvatar, BaseModal, DataStatus } from '@/shared/ui'

/**
 * ============================================================================
 *  "MENING GURUHIM" OYNASI (2026-08-17)
 * ============================================================================
 *
 * Bosh sahifadagi karta bosilganda ochiladi. Telegram guruh "chat info"
 * ekraniga o'xshab: guruh nomi, ustoz/kurator ismi va guruhdoshlar
 * ro'yxati (faqat ism-familiya — kontakt YO'Q, sabab server DTO'si
 * izohida). Pastda — muammo/fikr-taklif uchun bog'lanish kontakti
 * (sozlanmagan bo'lsa qator umuman chiqmaydi).
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()

const classroomQuery = useQuery({
  queryKey: ['students', 'me', 'classroom'],
  queryFn: ({ signal }) => fetchClassroom({ signal }),
  enabled: computed(() => props.open),
})

const groups = computed(() => classroomQuery.data.value?.groups ?? [])
const supportContact = computed(() => classroomQuery.data.value?.supportContact ?? null)

const errorMessage = computed(() =>
  classroomQuery.error.value !== null ? toUserMessage(classroomQuery.error.value) : null,
)
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Mening guruhim"
    sheet
    @close="emit('close')"
  >
    <DataStatus
      :pending="classroomQuery.isPending.value"
      :error="errorMessage"
      :empty="groups.length === 0"
      :retrying="classroomQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="users"
      empty-title="Guruh topilmadi"
      empty-text="Hozircha birorta guruhga biriktirilmagansiz — o‘quv bo‘limiga murojaat qiling."
      @retry="classroomQuery.refetch()"
    >
      <div class="space-y-5">
        <section
          v-for="group in groups"
          :key="group.groupId"
        >
          <h3 class="mb-2.5 flex items-center gap-2 text-[15px] font-extrabold text-slate-100">
            <AppIcon
              name="book"
              :size="16"
              class="shrink-0 text-brand-400"
            />
            <span
              class="min-w-0 truncate"
              v-text="group.groupName"
            />
          </h3>

          <!-- Ustoz / Kurator — Telegram "chat info" uslubidagi ikkita qator. -->
          <div class="space-y-2">
            <div
              v-if="group.teacherName !== null"
              class="flex items-center gap-3 rounded-[14px] border border-line bg-ink-900 px-3.5 py-2.5"
            >
              <BaseAvatar
                :name="group.teacherName"
                size="md"
              />
              <span class="min-w-0 flex-1">
                <span
                  class="block truncate text-sm font-bold text-slate-100"
                  v-text="group.teacherName"
                />
                <span class="text-xs text-dim">Ustoz</span>
              </span>
            </div>

            <div
              v-if="group.curatorName !== null"
              class="flex items-center gap-3 rounded-[14px] border border-line bg-ink-900 px-3.5 py-2.5"
            >
              <BaseAvatar
                :name="group.curatorName"
                size="md"
              />
              <span class="min-w-0 flex-1">
                <span
                  class="block truncate text-sm font-bold text-slate-100"
                  v-text="group.curatorName"
                />
                <span class="text-xs text-dim">Kurator</span>
              </span>
            </div>
          </div>

          <!-- Guruhdoshlar — Telegram guruh a'zolari ro'yxatiga o'xshash. -->
          <h4
            class="mb-2 ml-1 mt-4 text-xs font-bold uppercase tracking-[1.2px] text-slate-400"
            v-text="`O‘quvchilar (${group.classmates.length + 1})`"
          />
          <ul class="space-y-1.5">
            <!-- O'zi — DOIM birinchi, "Siz" yorlig'i bilan. -->
            <li class="flex items-center gap-3 rounded-[14px] px-1 py-1.5">
              <BaseAvatar
                name="Siz"
                size="sm"
              />
              <span class="text-sm font-semibold text-brand-300">Siz</span>
            </li>
            <li
              v-for="classmate in group.classmates"
              :key="classmate.id"
              class="flex items-center gap-3 rounded-[14px] px-1 py-1.5"
            >
              <BaseAvatar
                :name="classmate.fullName"
                size="sm"
              />
              <span
                class="min-w-0 flex-1 truncate text-sm text-slate-200"
                v-text="classmate.fullName"
              />
            </li>
          </ul>
        </section>

        <!--
          Bog'lanish kontakti — HAMMA guruhdan KEYIN, bittagina (guruh
          sonidan qat'i nazar): bu shaxsiy sozlama, guruhga tegishli emas.
        -->
        <section
          v-if="supportContact !== null"
          class="rounded-2xl border border-brand-500/25 bg-brand-500/10 p-3.5"
        >
          <p class="flex items-center gap-2 text-[13px] font-bold text-brand-300">
            <AppIcon
              name="message-circle"
              :size="15"
            />
            Muammo yoki fikr-taklif bormi?
          </p>
          <p
            class="mt-1.5 text-sm font-semibold text-slate-100"
            v-text="supportContact"
          />
        </section>
      </div>
    </DataStatus>
  </BaseModal>
</template>
