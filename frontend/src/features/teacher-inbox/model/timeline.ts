/**
 * Yozishma oqimiga qo'shiladigan karta (kurator ish stoli, 2026-11-20):
 * vazifa javobi, test natijasi, dars qarori. `InboxThread` ularni xabarlar
 * bilan VAQT BO'YICHA aralashtiradi va `#extra` slotida chaqiruvchiga
 * chizdiradi — kartaning mazmunini u bilmaydi.
 */
export interface InboxTimelineExtra {
  /** Oqim ichida noyob kalit (masalan `s-15`, `t-3`). */
  key: string
  /** ISO vaqt — tartib shu bo'yicha. */
  at: string
}
