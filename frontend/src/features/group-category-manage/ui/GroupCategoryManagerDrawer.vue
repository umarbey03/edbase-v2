<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  createGroupCategory,
  deleteGroupCategory,
  fetchGroupCategories,
  updateGroupCategory,
} from '@/entities/group-category'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { GroupCategoryDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseDrawer,
  BaseField,
  ConfirmDeleteDialog,
  DataStatus,
} from '@/shared/ui'

/**
 * ============================================================================
 *  O'QUV YO'NALISHLARI (KATEGORIYALAR) BOSHQARUVI — R21b
 * ============================================================================
 *
 * Talab (loyiha egasi): *"guruh category bo'yicha (ATF va grammatika, masalan
 * CEFR yoki IELTS), bu category parametr sifatida guruh uchun qo'shilishi
 * kerak"*. "Masalan" — ro'yxat OCHIQ, ya'ni uni o'quv bo'limi o'zi
 * to'ldiradi va bu ekran aynan shuning uchun bor.
 *
 * ★ NEGA ALOHIDA MARSHRUT EMAS, GURUHLAR SAHIFASIDAGI PANEL: lug'atda
 * o'nlab qator bo'ladi va unga faqat guruh yaratayotgan/tahrirlayotgan
 * xodim tegadi. Alohida sahifa yon menyuga oltinchi band qo'shishni talab
 * qilardi — menyu esa eski ilovadan AYNAN ko'chirilgan va uzaytirilmaydi
 * (`entities/user/model/navigation.ts` qoidasi). Panel esa aynan ehtiyoj
 * tug'ilgan joyda, bir bosishda ochiladi.
 *
 * 🔴 O'CHIRISH SERVERDA TO'SILADI (409) agar kategoriyaga guruh
 * biriktirilgan bo'lsa: bazadagi FK `SET NULL`, ya'ni o'chirish jimgina
 * muvaffaqiyatli tugab, o'nlab guruh yorlig'ini yo'qotardi. Shu sababli
 * ro'yxatda HAR qatorda `groupCount` ko'rinib turadi va tasdiq oynasi
 * server sababini AYNAN o'z so'zlari bilan ko'rsatadi
 * (`ConfirmDeleteDialog` — u xato kelganda OCHIQ qoladi).
 *
 * ★ TAHRIRLASH JOYIDA (inline): lug'atda ikkitagina maydon bor (nom va
 * faollik) va ular uchun ichma-ich panel ochish taqiqlangan
 * (`BaseDrawer` izohi: drawer ichida drawer). Shuning uchun qator
 * tahrirlash rejimiga o'tadi.
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

/* -------------------------------------------------------------- ro'yxat */

const categoriesQuery = useQuery({
  /*
    ★ KALIT `['group-categories', 'all']` — guruh formasidagi tanlagich
    `['group-categories', 'active']` ni ishlatadi. Ikkisi ham
    `['group-categories']` PREFIKSI ostida, ya'ni bitta invalidatsiya
    ikkalasini ham yangilaydi: yangi yo'nalish qo'shilgan zahoti u guruh
    formasida ham chiqadi.
  */
  queryKey: ['group-categories', 'all'],
  queryFn: ({ signal }) => fetchGroupCategories({}, { signal }),
  enabled: computed(() => props.open),
})

const categories = computed<GroupCategoryDto[]>(() => categoriesQuery.data.value ?? [])

const listError = computed(() =>
  categoriesQuery.error.value !== null ? toUserMessage(categoriesQuery.error.value) : null,
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['group-categories'] })

  /*
    🔴 GURUH RO'YXATLARI HAM ESKIRADI: kategoriya nomi `GroupDto.categoryName`
    ichida NUSXA bo'lib keladi (server JOIN bilan beradi), ya'ni nomni
    o'zgartirgandan keyin jadvalda ESKI nom turib qolardi. `['groups']` —
    ro'yxatlar, `['group']` — bitta guruh sahifasi; bu ikki kalit bir-birini
    QAMRAMAYDI (`ManageGroupsPage.refresh` da AYNI juftlik va AYNI sabab).
  */
  void queryClient.invalidateQueries({ queryKey: ['groups'] })
  void queryClient.invalidateQueries({ queryKey: ['group'] })
}

/* ------------------------------------------------------------ yaratish */

const newName = ref('')
const createError = ref<string | null>(null)

const createMutation = useMutation({
  mutationFn: (name: string) => createGroupCategory({ name, isActive: true }),
  onSuccess: () => {
    newName.value = ''
    createError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    createError.value = toUserMessage(error)
  },
})

function onCreate(): void {
  const name = newName.value.trim()
  createError.value = null

  if (name.length === 0) {
    createError.value = 'Yo‘nalish nomini kiriting.'
    return
  }

  createMutation.mutate(name)
}

/* ---------------------------------------------------------- tahrirlash */

const editingId = ref<number | null>(null)
const editName = ref('')
const editActive = ref(true)
const editError = ref<string | null>(null)

function startEdit(category: GroupCategoryDto): void {
  editingId.value = category.id
  editName.value = category.name ?? ''
  editActive.value = category.isActive
  editError.value = null
}

function cancelEdit(): void {
  editingId.value = null
  editError.value = null
}

const updateMutation = useMutation({
  mutationFn: (input: { id: number; name: string; isActive: boolean }) =>
    updateGroupCategory(input.id, { name: input.name, isActive: input.isActive }),
  onSuccess: () => {
    editingId.value = null
    editError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    editError.value = toUserMessage(error)
  },
})

/**
 * Saqlash — TASDIQ bilan.
 *
 * ★ NEGA TASDIQ SO'RALADI: bu `PUT`, ya'ni ma'lumotni ALMASHTIRUVCHI amal
 * (`useConfirm` izohidagi jadval: "ma'lumotni almashtiruvchi saqlash → HAR
 * DOIM"). Arxivlashda esa oqibat kattaroq va u `details` da ochiq aytiladi:
 * yorliq guruhlarda QOLADI, faqat yangi tanlovlarda taklif qilinmaydi.
 */
async function onSaveEdit(category: GroupCategoryDto): Promise<void> {
  const name = editName.value.trim()
  editError.value = null

  if (name.length === 0) {
    editError.value = 'Yo‘nalish nomini kiriting.'
    return
  }

  const details: string[] = []
  if (name !== (category.name ?? '')) details.push(`Nomi: “${category.name ?? '—'}” → “${name}”`)
  if (editActive.value !== category.isActive) {
    details.push(
      editActive.value
        ? 'Yo‘nalish qayta faollashtiriladi'
        : `Arxivlanadi: yangi guruhlarga taklif qilinmaydi, ${category.groupCount} ta mavjud guruhda qoladi`,
    )
  }

  if (details.length === 0) {
    cancelEdit()
    return
  }

  const ok = await confirm({
    title: 'Yo‘nalishni saqlash',
    message: 'O‘zgarishlar saqlansinmi?',
    confirmLabel: 'Saqlash',
    tone: 'primary',
    details,
  })

  if (!ok) return

  updateMutation.mutate({ id: category.id, name, isActive: editActive.value })
}

/* ------------------------------------------------------------ o'chirish */

const deleting = ref<GroupCategoryDto | null>(null)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteGroupCategory(id),
  onSuccess: () => {
    deleting.value = null
    deleteError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    // ★ OYNA OCHIQ QOLADI: server 409 sababini AYNAN o'z so'zlari bilan
    //   aytadi ("... ta guruh biriktirilgan — ... ARXIVLANG").
    deleteError.value = toUserMessage(error)
  },
})

function askDelete(category: GroupCategoryDto): void {
  deleting.value = category
  deleteError.value = null
}

const deleteMessage = computed(() => {
  const current = deleting.value
  if (current === null) return ''

  return current.groupCount > 0
    ? `“${current.name ?? '—'}” yo‘nalishiga ${current.groupCount} ta guruh biriktirilgan. `
      + 'Server bunday yo‘nalishni o‘chirtirmaydi — uning o‘rniga arxivlang.'
    : `“${current.name ?? '—'}” yo‘nalishi o‘chiriladi. Bu amalni qaytarib bo‘lmaydi.`
})

/* Panel yopilganda holat tozalanadi — keyingi ochilishda eski xato yoki
   yarim yozilgan nom qolib ketmasin. */
watch(
  () => props.open,
  (open) => {
    if (open) return
    newName.value = ''
    createError.value = null
    editingId.value = null
    editError.value = null
    deleting.value = null
    deleteError.value = null
  },
)
</script>

<template>
  <BaseDrawer
    :open="props.open"
    title="O‘quv yo‘nalishlari"
    subtitle="Guruhlarni saralash uchun kategoriyalar: ATF, Grammatika, CEFR, IELTS"
    @close="emit('close')"
  >
    <div class="space-y-4">
      <!-- ─────────────────────── YANGI YO'NALISH ─────────────────────── -->
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <BaseField
          label="Yangi yo‘nalish"
          hint="Masalan: ATF, Grammatika, CEFR, IELTS"
          :error="createError"
        >
          <div class="flex gap-2">
            <input
              v-model="newName"
              class="zn-input"
              maxlength="100"
              placeholder="Yo‘nalish nomi"
              @keyup.enter="onCreate"
            >
            <BaseButton
              class="shrink-0"
              :loading="createMutation.isPending.value"
              @click="onCreate"
            >
              <template #icon>
                <AppIcon
                  name="plus"
                  :size="15"
                />
              </template>
              Qo‘shish
            </BaseButton>
          </div>
        </BaseField>
      </div>

      <!-- ───────────────────────── RO'YXAT ───────────────────────── -->
      <DataStatus
        :pending="categoriesQuery.isPending.value"
        :error="listError"
        :empty="categories.length === 0"
        :retrying="categoriesQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="grid"
        empty-title="Yo‘nalish qo‘shilmagan"
        empty-text="Birinchi yo‘nalishni yuqoridagi maydondan qo‘shing."
        @retry="categoriesQuery.refetch()"
      >
        <ul class="divide-y divide-line rounded-xl border border-line">
          <li
            v-for="category in categories"
            :key="category.id"
            class="p-3.5"
          >
            <!-- Tahrirlash rejimi — JOYIDA (ichma-ich panel taqiqlangan). -->
            <div
              v-if="editingId === category.id"
              class="space-y-2.5"
            >
              <BaseField
                label="Nomi"
                :error="editError"
              >
                <input
                  v-model="editName"
                  class="zn-input"
                  maxlength="100"
                >
              </BaseField>
              <!--
                ⚠️ Yorliq matni ATAYLAB uzun: arxivlash "o'chirish" emas va
                bu farq xodimga aytilishi kerak — aks holda u yorliqni
                arxivlab, guruhlardan ham yo'qoldi deb o'ylardi.
              -->
              <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
                <input
                  v-model="editActive"
                  type="checkbox"
                  class="size-4 accent-brand-500"
                >
                Faol (yangi guruhlarga taklif qilinadi)
              </label>
              <div class="flex justify-end gap-2">
                <BaseButton
                  size="sm"
                  variant="secondary"
                  @click="cancelEdit"
                >
                  Bekor qilish
                </BaseButton>
                <BaseButton
                  size="sm"
                  :loading="updateMutation.isPending.value"
                  @click="onSaveEdit(category)"
                >
                  Saqlash
                </BaseButton>
              </div>
            </div>

            <!-- Ko'rish rejimi -->
            <div
              v-else
              class="flex flex-wrap items-center gap-2"
            >
              <span
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="category.name ?? '—'"
              />
              <BaseBadge :tone="category.isActive ? 'success' : 'neutral'">
                {{ category.isActive ? 'Faol' : 'Arxiv' }}
              </BaseBadge>
              <!--
                🔴 GURUHLAR SONI — o'chirishdan OLDIN ko'rinishi shart:
                serverdagi FK `SET NULL` bo'lgani uchun bu son o'chirish
                nechta guruhning yorlig'iga tegishini aytadi.
              -->
              <span class="shrink-0 text-xs tabular-nums text-dim">
                {{ category.groupCount }} guruh
              </span>
              <BaseButton
                size="sm"
                variant="secondary"
                @click="startEdit(category)"
              >
                <template #icon>
                  <AppIcon
                    name="edit"
                    :size="13"
                  />
                </template>
                Tahrirlash
              </BaseButton>
              <BaseButton
                size="sm"
                variant="danger"
                @click="askDelete(category)"
              >
                O‘chirish
              </BaseButton>
            </div>
          </li>
        </ul>
      </DataStatus>
    </div>

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Yo‘nalishni o‘chirish"
      :message="deleteMessage"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="deleting !== null && deleteMutation.mutate(deleting.id)"
    />
  </BaseDrawer>
</template>
