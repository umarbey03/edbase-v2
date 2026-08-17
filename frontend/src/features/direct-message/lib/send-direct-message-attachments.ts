import { uploadWithProgress } from '@/features/lesson-media'
import type { UploadProgress } from '@/features/lesson-media'
import type { DirectMessageDto } from '@/shared/types'

/**
 * ========================================================================
 * FAYL BILAN XABAR YUBORISH (2026-08-17) — kurator ↔ o'quvchi shaxsiy chati
 * ========================================================================
 *
 * `features/group-chat/lib/send-chat-attachments.ts` bilan AYNI naqsh va
 * AYNI sabab: DM realtime kanali yo'q (hub o'rniga oddiy `useQuery`
 * so'rovi), shuning uchun bu yerda hatto "hub vs REST" tanlovi ham yo'q —
 * xabar HAR DOIM shu funksiya (fayl bo'lsa) yoki `sendDirectMessage`
 * (fayl bo'lmasa) orqali ketadi.
 */

/** Bitta xabarga ko'pi bilan shuncha fayl — SERVER chegarasining nusxasi. */
export const DM_ATTACHMENT_MAX_FILES = 5

export interface SendDmAttachmentsOptions {
  peerId: number
  files: readonly File[]
  /** Ixtiyoriy izoh. Bo'sh bo'lishi MUMKIN (izohsiz surat). */
  body?: string
  moduleLessonId?: number | null
  onProgress?: (progress: UploadProgress) => void
  signal?: AbortSignal
}

export function sendDirectMessageAttachments(
  options: SendDmAttachmentsOptions,
): Promise<DirectMessageDto> {
  const form = new FormData()

  for (const file of options.files) form.append('files', file)

  const body = options.body?.trim() ?? ''
  if (body.length > 0) form.append('body', body)
  if (options.moduleLessonId !== undefined && options.moduleLessonId !== null) {
    form.append('moduleLessonId', String(options.moduleLessonId))
  }

  return uploadWithProgress<DirectMessageDto>({
    path: `/api/v1/messages/conversations/${options.peerId}/messages/attachments`,
    form,
    onProgress: options.onProgress,
    signal: options.signal,
  })
}
