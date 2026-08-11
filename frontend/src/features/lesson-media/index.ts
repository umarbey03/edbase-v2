/**
 * DARS MEDIASI VA YUKLASH OQIMI (WAVE 2).
 *
 * Bu feature IKKI joyda ishlatiladi:
 *  • dars drawer'i — video qismlari / imtihon rasmlari (`LessonAssetsSection`);
 *  • uy vazifasi shartining biriktirmalari (`features/assignment-form`) —
 *    u yerda yuklash MEXANIZMI (`useUploadQueue`, `uploadWithProgress`,
 *    `UploadQueueList`, `useProtectedBlobUrl`) qayta ishlatiladi.
 *
 * Ya'ni "progress + bekor qilish + chegarani oldindan tekshirish" mantig'i
 * BITTA joyda turadi: ikkinchi nusxada tuzatish esdan chiqishi mumkin emas.
 */
export { probeKindForAttachment, probeMedia } from './lib/media-probe'
export type { MediaMetadata } from './lib/media-probe'
export { isUploadCancelled, uploadWithProgress, UploadCancelledError } from './lib/upload-with-progress'
export type { UploadProgress, UploadRequest } from './lib/upload-with-progress'
export { useProtectedBlobUrl } from './lib/useProtectedBlobUrl'
export type { ProtectedBlob } from './lib/useProtectedBlobUrl'
export {
  FALLBACK_IMAGE_MAX_MB,
  FALLBACK_VIDEO_MAX_MB,
  useUploadLimits,
} from './model/limits'
export type { UploadLimits } from './model/limits'
export { useUploadQueue } from './model/upload-queue'
export type {
  QueueUploader,
  UploadItem,
  UploadItemStatus,
  UploadQueue,
  UseUploadQueueOptions,
} from './model/upload-queue'
export { default as AssetPreviewDialog } from './ui/AssetPreviewDialog.vue'
export { default as LessonAssetsSection } from './ui/LessonAssetsSection.vue'
export { default as UploadQueueList } from './ui/UploadQueueList.vue'
