<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  BOOK_UPLOAD_PATH,
  buildBookForm,
  deleteBook,
  fetchBooks,
  resolveBookUrl,
  updateBook,
} from '@/entities/book'
import { countPdfPages } from '@/features/book-share/model/pdf'
import { uploadWithProgress } from '@/features/lesson-media/lib/upload-with-progress'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { formatFileSize } from '@/shared/lib/text'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import type { BookDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseField,
  BaseModal,
  DataStatus,
  IconButton,
  PageHeader,
} from '@/shared/ui'

/**
 * KUTUBXONA (2026-09-09) — PDF kitoblar: yuklash, nomlash, arxivlash.
 *
 * NIMA UCHUN: telefondan dars o'tayotgan ustoz ekran ulasha olmaydi.
 * Kitob shu yerga yuklanadi, jonli darsda ustoz «Kitob» tugmasi bilan
 * uni tanlab, kerakli sahifani chizma bilan o'quvchilarga ko'rsatadi.
 *
 * ★ SAHIFALAR SONI KLIENTDA aniqlanadi (`countPdfPages`, pdf.js): server
 *   PDF'ni ochmaydi — unga kutubxona kerak bo'lardi. Aniqlanmasa (buzuq
 *   fayl) yuklash baribir davom etadi, son «—» bo'ladi.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

const booksQuery = useQuery({
  queryKey: ['books', 'all'],
  queryFn: ({ signal }) => fetchBooks({ includeInactive: true }, { signal }),
})

const books = computed(() => booksQuery.data.value ?? [])
const booksError = computed(() =>
  booksQuery.error.value !== null ? toUserMessage(booksQuery.error.value) : null,
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['books'] })
}

/* ------------------------------------------------------------- yuklash */

const fileInput = ref<HTMLInputElement | null>(null)
const pickedFile = ref<File | null>(null)
const uploadTitle = ref('')
const uploadPercent = ref<number | null>(null)
const uploadError = ref<string | null>(null)

function onFilePicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  pickedFile.value = file
  uploadError.value = null
  if (file !== null && uploadTitle.value.trim().length === 0) {
    uploadTitle.value = file.name.replace(/\.pdf$/i, '').trim()
  }
}

const uploadMutation = useMutation({
  mutationFn: async (file: File) => {
    let pageCount: number | null = null
    try {
      pageCount = await countPdfPages(file)
    } catch {
      // Sahifa soni — qulaylik; aniqlanmasa yuklash to'xtamaydi.
    }
    uploadPercent.value = 0
    return uploadWithProgress<BookDto>({
      path: BOOK_UPLOAD_PATH,
      form: buildBookForm(file, { title: uploadTitle.value, pageCount }),
      onProgress: (progress) => {
        uploadPercent.value = progress.percent
      },
    })
  },
  onSuccess: (book) => {
    showToast(`«${book.title}» yuklandi.`)
    pickedFile.value = null
    uploadTitle.value = ''
    if (fileInput.value !== null) fileInput.value.value = ''
    refresh()
  },
  onError: (error: Error) => {
    uploadError.value = toUserMessage(error)
  },
  onSettled: () => {
    uploadPercent.value = null
  },
})

function submitUpload(): void {
  const file = pickedFile.value
  if (file === null || uploadMutation.isPending.value) return
  uploadError.value = null
  uploadMutation.mutate(file)
}

/* ------------------------------------------------------------ amallar */

const editing = ref<BookDto | null>(null)
const editTitle = ref('')
const editError = ref<string | null>(null)

function openEdit(book: BookDto): void {
  editing.value = book
  editTitle.value = book.title
  editError.value = null
}

const updateMutation = useMutation({
  mutationFn: (payload: { book: BookDto; title: string; isActive: boolean }) =>
    updateBook(payload.book.id, {
      title: payload.title,
      isActive: payload.isActive,
      pageCount: null,
    }),
  onSuccess: () => {
    editing.value = null
    refresh()
  },
  onError: (error: Error) => {
    editError.value = toUserMessage(error)
    showToast(toUserMessage(error), 'error')
  },
})

function saveEdit(): void {
  const book = editing.value
  if (book === null) return
  const title = editTitle.value.trim()
  if (title.length === 0) {
    editError.value = 'Nom bo‘sh bo‘lmasin.'
    return
  }
  updateMutation.mutate({ book, title, isActive: book.isActive })
}

function toggleActive(book: BookDto): void {
  updateMutation.mutate({ book, title: book.title, isActive: !book.isActive })
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteBook(id),
  onSuccess: () => {
    showToast('Kitob o‘chirildi.')
    refresh()
  },
  onError: (error: Error) => {
    showToast(toUserMessage(error), 'error')
  },
})

async function askDelete(book: BookDto): Promise<void> {
  const ok = await confirm({
    title: 'Kitobni o‘chirish',
    message: `«${book.title}» butunlay o‘chiriladi — fayl ham ombordan ketadi.`,
    confirmLabel: 'O‘chirish',
    tone: 'danger',
    details: ['Jonli darsda uni endi tanlab bo‘lmaydi.', 'Vaqtincha yashirish uchun «Arxivlash» yetarli.'],
  })
  if (!ok) return
  deleteMutation.mutate(book.id)
}

/** Yangi oynada ochish — chipta bilan (sarlavhasiz GET). */
async function openBook(book: BookDto): Promise<void> {
  try {
    const url = await resolveBookUrl(book.id)
    window.open(url, '_blank', 'noopener')
  } catch (error) {
    showToast(toUserMessage(error), 'error')
  }
}
</script>

<template>
  <div>
    <PageHeader
      title="Kutubxona"
      subtitle="Jonli darsda ustoz ko‘rsatadigan PDF kitoblar"
    />

    <div class="grid gap-4 lg:grid-cols-[360px_1fr]">
      <!-- ============================================== yuklash -->
      <BaseCard title="Kitob yuklash">
        <p class="mb-3 text-xs leading-relaxed text-slate-400">
          Faqat PDF, 50 MB gacha. Ustoz jonli darsda «Kitob» tugmasi orqali
          tanlab, sahifani chizma bilan o‘quvchilarga ko‘rsatadi — telefondan
          ham.
        </p>

        <BaseField label="PDF fayl">
          <input
            ref="fileInput"
            type="file"
            accept="application/pdf,.pdf"
            class="block w-full text-sm text-slate-300 file:mr-3 file:rounded-lg file:border-0 file:bg-ink-750 file:px-3 file:py-2 file:text-sm file:font-medium file:text-slate-100 hover:file:bg-ink-700"
            :disabled="uploadMutation.isPending.value"
            @change="onFilePicked"
          >
        </BaseField>

        <div class="mt-3">
          <BaseField
            label="Nomi"
            hint="Ustoz ro‘yxatda shu nomni ko‘radi."
          >
            <input
              v-model="uploadTitle"
              class="zn-input"
              maxlength="200"
              placeholder="Masalan: Harflar — 28 ta dars (lotin)"
              :disabled="uploadMutation.isPending.value"
            >
          </BaseField>
        </div>

        <p
          v-if="pickedFile !== null"
          class="mt-2 text-xs text-slate-400"
        >
          {{ pickedFile.name }} · {{ formatFileSize(pickedFile.size) }}
        </p>

        <div
          v-if="uploadPercent !== null"
          class="mt-3"
        >
          <div class="h-1.5 overflow-hidden rounded-full bg-ink-800">
            <div
              class="h-full rounded-full bg-brand-500 transition-[width]"
              :style="{ width: `${uploadPercent}%` }"
            />
          </div>
          <p class="mt-1 text-[11px] text-slate-500">
            Yuklanmoqda… {{ uploadPercent }}%
          </p>
        </div>

        <p
          v-if="uploadError !== null"
          class="mt-3 text-xs text-rose-400"
          role="alert"
          v-text="uploadError"
        />

        <BaseButton
          class="mt-4 w-full"
          :disabled="pickedFile === null || uploadMutation.isPending.value"
          :loading="uploadMutation.isPending.value"
          @click="submitUpload"
        >
          <template #icon>
            <AppIcon
              name="upload"
              :size="15"
            />
          </template>
          Yuklash
        </BaseButton>
      </BaseCard>

      <!-- ============================================== ro'yxat -->
      <BaseCard title="Kitoblar">
        <template #actions>
          <span
            v-if="books.length > 0"
            class="text-xs text-slate-400"
          >
            {{ books.filter((b) => b.isActive).length }} faol
          </span>
        </template>

        <DataStatus
          :pending="booksQuery.isPending.value"
          :error="booksError"
          :empty="books.length === 0"
          :retrying="booksQuery.isFetching.value"
          :skeleton-rows="4"
          empty-icon="book"
          empty-title="Hali kitob yo‘q"
          empty-text="Chapdagi formadan birinchi PDF kitobni yuklang."
          @retry="booksQuery.refetch()"
        >
          <ul class="divide-y divide-line rounded-xl border border-line">
            <li
              v-for="book in books"
              :key="book.id"
              class="flex items-center gap-3 p-3"
              :class="book.isActive ? '' : 'opacity-60'"
            >
              <span class="flex size-10 shrink-0 items-center justify-center rounded-lg bg-brand-500/15 text-brand-300">
                <AppIcon
                  name="book"
                  :size="18"
                />
              </span>
              <div class="min-w-0 flex-1">
                <p class="flex flex-wrap items-center gap-1.5">
                  <button
                    type="button"
                    class="truncate text-left text-sm font-medium text-slate-100 hover:text-brand-300"
                    :title="`${book.title}: ochish`"
                    @click="openBook(book)"
                  >
                    {{ book.title }}
                  </button>
                  <BaseBadge
                    v-if="!book.isActive"
                    tone="neutral"
                  >
                    Arxiv
                  </BaseBadge>
                </p>
                <p class="mt-0.5 text-xs text-slate-400">
                  {{ book.pageCount !== null ? `${book.pageCount} sahifa · ` : '' }}{{ formatFileSize(book.sizeBytes) }}
                  · {{ formatDateWithYear(book.createdAt) }}
                  <span v-if="book.createdByName !== null"> · {{ book.createdByName }}</span>
                </p>
              </div>
              <div class="flex shrink-0 items-center gap-1">
                <IconButton
                  icon="eye"
                  label="Ochish"
                  size="sm"
                  @click="openBook(book)"
                />
                <IconButton
                  icon="edit"
                  label="Nomini o‘zgartirish"
                  size="sm"
                  @click="openEdit(book)"
                />
                <IconButton
                  :icon="book.isActive ? 'eye-off' : 'eye'"
                  :label="book.isActive ? 'Arxivlash (ustozga ko‘rinmaydi)' : 'Arxivdan chiqarish'"
                  size="sm"
                  :loading="updateMutation.isPending.value && updateMutation.variables.value?.book.id === book.id"
                  @click="toggleActive(book)"
                />
                <IconButton
                  icon="trash"
                  label="O‘chirish"
                  size="sm"
                  tone="danger"
                  :loading="deleteMutation.isPending.value && deleteMutation.variables.value === book.id"
                  @click="askDelete(book)"
                />
              </div>
            </li>
          </ul>
        </DataStatus>
      </BaseCard>
    </div>

    <BaseModal
      :open="editing !== null"
      title="Kitob nomi"
      @close="editing = null"
    >
      <BaseField label="Nomi">
        <input
          v-model="editTitle"
          class="zn-input"
          maxlength="200"
          @keydown.enter.prevent="saveEdit"
        >
      </BaseField>
      <p
        v-if="editError !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="editError"
      />
      <template #footer>
        <BaseButton
          variant="secondary"
          @click="editing = null"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          :loading="updateMutation.isPending.value"
          @click="saveEdit"
        >
          Saqlash
        </BaseButton>
      </template>
    </BaseModal>
  </div>
</template>
