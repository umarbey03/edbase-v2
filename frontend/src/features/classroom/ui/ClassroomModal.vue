<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

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
 *
 * ★ 2026-10-31 — TELEGRAM "GROUP INFO" KO'RINISHI (loyiha egasi): katta guruh
 *   belgisi, nom va a'zolar soni tepada; tezkor tugmalar (Chat, Jadval);
 *   "Ustozlar" bo'limida rol nomlari landing bilan bir xil (Asosiy ustoz /
 *   Support teacher); a'zolar ro'yxatida qidiruv. Kontakt ma'lumoti hamon
 *   YO'Q — server DTO'si buni bermaydi va bermasligi kerak.
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

const router = useRouter()
function go(name: 'student-chat' | 'student-calendar'): void {
  emit('close')
  void router.push({ name })
}

/** A'zolar ichida qidiruv — katta guruhda (18–20 kishi) ismni tez topish. */
const search = ref('')
function filtered(list: { id: number, fullName: string }[]): { id: number, fullName: string }[] {
  const q = search.value.trim().toLowerCase()
  return q.length === 0 ? list : list.filter((m) => m.fullName.toLowerCase().includes(q))
}

/** Guruh belgisi uchun bosh harflar ("ATF-1 (demo)" → "A1"). */
function groupInitials(name: string): string {
  const parts = name.replace(/[()]/g, ' ').split(/[\s-]+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase()
}

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
      <div class="space-y-6">
        <section
          v-for="group in groups"
          :key="group.groupId"
        >
          <!-- ── Sarlavha: Telegram "group info" kabi ── -->
          <div class="flex flex-col items-center text-center">
            <span
              class="flex size-20 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-emerald-400 text-2xl font-extrabold text-white shadow-md"
              aria-hidden="true"
              v-text="groupInitials(group.groupName)"
            />
            <h3
              class="mt-3 max-w-full truncate text-lg font-extrabold text-slate-100"
              v-text="group.groupName"
            />
            <p class="text-sm text-slate-400">
              {{ group.classmates.length + 1 }} o‘quvchi
            </p>
          </div>

          <!-- ── Tezkor tugmalar ── -->
          <div class="mt-4 grid grid-cols-2 gap-2">
            <button
              type="button"
              class="flex flex-col items-center gap-1 rounded-[14px] bg-ink-900 py-3 text-xs font-semibold text-brand-300 ring-1 ring-inset ring-line transition-colors hover:bg-ink-800"
              @click="go('student-chat')"
            >
              <AppIcon
                name="message-circle"
                :size="20"
              />
              Guruh chati
            </button>
            <button
              type="button"
              class="flex flex-col items-center gap-1 rounded-[14px] bg-ink-900 py-3 text-xs font-semibold text-brand-300 ring-1 ring-inset ring-line transition-colors hover:bg-ink-800"
              @click="go('student-calendar')"
            >
              <AppIcon
                name="calendar"
                :size="20"
              />
              Dars jadvali
            </button>
          </div>

          <!-- ── Ustozlar ── -->
          <div
            v-if="group.teacherName !== null || group.curatorName !== null"
            class="mt-4 overflow-hidden rounded-[14px] bg-ink-900 ring-1 ring-inset ring-line"
          >
            <p class="px-3.5 pt-3 text-xs font-bold uppercase tracking-[1.2px] text-brand-400">
              Ustozlar
            </p>
            <div
              v-if="group.teacherName !== null"
              class="flex items-center gap-3 px-3.5 py-2.5"
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
                <span class="text-xs text-dim">Asosiy ustoz</span>
              </span>
            </div>
            <div
              v-if="group.curatorName !== null"
              class="flex items-center gap-3 border-t border-line px-3.5 py-2.5"
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
                <span class="text-xs text-dim">Support teacher</span>
              </span>
            </div>
          </div>

          <!-- ── A'zolar ── -->
          <div class="mt-4 overflow-hidden rounded-[14px] bg-ink-900 ring-1 ring-inset ring-line">
            <p class="px-3.5 pt-3 text-xs font-bold uppercase tracking-[1.2px] text-brand-400">
              {{ group.classmates.length + 1 }} a’zo
            </p>
            <label
              v-if="group.classmates.length > 6"
              class="mx-3.5 mt-2 flex items-center gap-2 rounded-lg bg-ink-950 px-3 py-2 ring-1 ring-inset ring-line"
            >
              <AppIcon
                name="search"
                :size="15"
                class="text-slate-500"
              />
              <span class="sr-only">A’zolarni qidirish</span>
              <input
                v-model="search"
                type="search"
                class="w-full bg-transparent text-sm text-slate-100 placeholder:text-slate-500 focus:outline-none"
                placeholder="Qidirish"
              >
            </label>
            <ul class="mt-1 pb-1.5">
              <li
                v-if="search.trim().length === 0"
                class="flex items-center gap-3 px-3.5 py-2"
              >
                <BaseAvatar
                  name="Siz"
                  size="sm"
                />
                <span class="text-sm font-semibold text-brand-300">Siz</span>
              </li>
              <li
                v-for="classmate in filtered(group.classmates)"
                :key="classmate.id"
                class="flex items-center gap-3 px-3.5 py-2"
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
              <li
                v-if="filtered(group.classmates).length === 0 && search.trim().length > 0"
                class="px-3.5 py-2 text-sm text-slate-500"
              >
                Hech kim topilmadi
              </li>
            </ul>
          </div>
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
