import { computed, onScopeDispose, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { toUserMessage } from '@/shared/api'

import { isUploadCancelled } from '../lib/upload-with-progress'
import type { UploadProgress } from '../lib/upload-with-progress'

/**
 * ========================================================================
 * YUKLASH NAVBATI — KETMA-KET, HAR FAYL UCHUN ALOHIDA QATOR
 * ========================================================================
 *
 * 🔴 NEGA KETMA-KET (parallel EMAS): xodim bir vaqtda 3 ta 1 GB video
 * tanlashi mumkin. Ularni parallel yuborish foydalanuvchining o'z
 * internetini bo'g'adi — uchalasi ham sekinlashadi, birortasi tugamaydi va
 * progress qatorlari "qotib qolgandek" ko'rinadi. Ketma-ket yuborilganda
 * birinchi fayl to'liq tezlikda tugaydi va xodim ish natijasini darhol
 * ko'radi.
 *
 * 🔴 CHEGARA TEKSHIRUVI YUBORISHDAN OLDIN: chegaradan katta fayl UMUMAN
 * yuborilmaydi (`validate`). Aks holda foydalanuvchi 1.5 GB ni yigirma
 * daqiqa yuklab, oxirida 413 olardi — bu shu blokning eng qimmat xatosi
 * (nginx tuzog'i: 13-bo'lim, 40-tuzoq).
 *
 * ★ NAVBAT KOMPONENT ICHIDA yashaydi (global store EMAS): drawer yopilganda
 * yuklash to'xtashi KERAK — davom etayotgan yuklash ko'rinmas holda qolsa,
 * foydalanuvchi uni bekor ham qila olmasdi, xatosini ham ko'rmasdi.
 */

export type UploadItemStatus = 'pending' | 'uploading' | 'done' | 'error' | 'cancelled'

export interface UploadItem {
  /** Faqat UI uchun lokal kalit (serverdagi Id emas). */
  id: string
  file: File
  name: string
  size: number
  status: UploadItemStatus
  /** Yuborilgan bayt (progressni foizsiz ham ko'rsatish uchun). */
  loaded: number
  percent: number
  /** `status: 'error'` da foydalanuvchi matni. */
  error: string | null
}

/**
 * Bitta faylni yuklovchi funksiya (chaqiruvchi beradi: dars mediasi yoki
 * vazifa biriktirmasi).
 *
 * `signal` — "Bekor qilish" tugmasi; `uploadWithProgress` uni to'g'ridan
 * to'g'ri qabul qiladi.
 */
export type QueueUploader = (
  file: File,
  onProgress: (progress: UploadProgress) => void,
  signal: AbortSignal,
) => Promise<void>

export interface UseUploadQueueOptions {
  upload: QueueUploader
  /**
   * Yuborishdan OLDINGI tekshiruv: xato matni yoki `null`.
   *
   * Yiqilgan fayl navbatga `error` holatida tushadi va SERVERGA UMUMAN
   * yuborilmaydi — lekin ro'yxatdan ham yo'qolmaydi: foydalanuvchi nima
   * uchun rad etilganini ko'rishi kerak.
   */
  validate?: (file: File) => string | null
}

export interface UploadQueue {
  items: Ref<UploadItem[]>
  isBusy: ComputedRef<boolean>
  /** Yuklanmoqda yoki navbatda turgan fayllar soni. */
  activeCount: ComputedRef<number>
  enqueue: (files: readonly File[]) => void
  cancel: (id: string) => void
  retry: (id: string) => void
  /** Tugagan/bekor qilingan/xato qatorlarni ro'yxatdan olib tashlaydi. */
  clearFinished: () => void
  /** Drawer yopilganda: davom etayotgan yuklash uziladi, ro'yxat bo'shaydi. */
  reset: () => void
}

let sequence = 0

function nextId(): string {
  sequence += 1
  return `u${sequence}`
}

export function useUploadQueue(options: UseUploadQueueOptions): UploadQueue {
  const items = ref<UploadItem[]>([])
  const controllers = new Map<string, AbortController>()
  let running = false

  const isBusy = computed(() => items.value.some((item) => item.status === 'uploading'))
  const activeCount = computed(
    () => items.value.filter((item) => item.status === 'pending' || item.status === 'uploading').length,
  )

  function find(id: string): UploadItem | undefined {
    return items.value.find((item) => item.id === id)
  }

  async function runOne(item: UploadItem): Promise<void> {
    const controller = new AbortController()
    controllers.set(item.id, controller)

    item.status = 'uploading'
    item.loaded = 0
    item.percent = 0
    item.error = null

    try {
      await options.upload(
        item.file,
        (progress) => {
          // Qator ro'yxatdan olib tashlangan bo'lishi mumkin (`clearFinished`).
          const current = find(item.id)
          if (current === undefined) return
          current.loaded = progress.loaded
          current.percent = progress.percent
        },
        controller.signal,
      )

      const done = find(item.id)
      if (done !== undefined) {
        done.status = 'done'
        done.percent = 100
        done.loaded = done.size
      }
    } catch (error) {
      const failed = find(item.id)
      if (failed === undefined) return

      if (isUploadCancelled(error)) {
        failed.status = 'cancelled'
        failed.error = null
        return
      }

      failed.status = 'error'
      failed.error = toUserMessage(error)
    } finally {
      controllers.delete(item.id)
    }
  }

  /**
   * Navbatni surish. `running` qulfi MAJBURIY: `enqueue` bir necha marta
   * chaqirilsa (ikkinchi fayl to'plami tanlansa) ikki `pump` parallel
   * yurib, ketma-ketlik qoidasi buzilardi.
   */
  async function pump(): Promise<void> {
    if (running) return
    running = true
    try {
      for (;;) {
        const next = items.value.find((item) => item.status === 'pending')
        if (next === undefined) return
        await runOne(next)
      }
    } finally {
      running = false
    }
  }

  function enqueue(files: readonly File[]): void {
    for (const file of files) {
      const problem = options.validate?.(file) ?? null
      items.value.push({
        id: nextId(),
        file,
        name: file.name,
        size: file.size,
        // Tekshiruvdan o'tmagan fayl DARHOL `error` — u hech qachon
        // yuborilmaydi (`pump` faqat `pending` ni oladi).
        status: problem === null ? 'pending' : 'error',
        loaded: 0,
        percent: 0,
        error: problem,
      })
    }
    void pump()
  }

  function cancel(id: string): void {
    const item = find(id)
    if (item === undefined) return

    if (item.status === 'pending') {
      // Hali boshlanmagan: `abort` chaqirishga hech narsa yo'q.
      item.status = 'cancelled'
      return
    }
    controllers.get(id)?.abort()
  }

  function retry(id: string): void {
    const item = find(id)
    if (item === undefined) return
    if (item.status === 'uploading' || item.status === 'pending') return

    /*
      Qayta urinishda tekshiruv YANA yuriladi: chegara sozlamasi shu orada
      o'zgargan bo'lishi mumkin (masalan administrator video chegarasini
      oshirdi va xodim "Qayta urinish" ni bosdi).
    */
    const problem = options.validate?.(item.file) ?? null
    if (problem !== null) {
      item.status = 'error'
      item.error = problem
      return
    }

    item.status = 'pending'
    item.error = null
    item.loaded = 0
    item.percent = 0
    void pump()
  }

  function clearFinished(): void {
    items.value = items.value.filter(
      (item) => item.status === 'pending' || item.status === 'uploading',
    )
  }

  function reset(): void {
    for (const controller of controllers.values()) controller.abort()
    controllers.clear()
    items.value = []
  }

  /*
    Komponent yo'q qilinganda davom etayotgan yuklash UZILADI. Aks holda
    `xhr` fonda yurib, javob kelganda allaqachon yo'q bo'lgan ro'yxatga
    yozishga urinardi.
  */
  onScopeDispose(reset)

  return { items, isBusy, activeCount, enqueue, cancel, retry, clearFinished, reset }
}
