/**
 * DARS KESIMIDAGI MATRITSA — eski `#att-grid` (`loadAttendance()`).
 *
 * ★ MA'LUMOT SHAKLI BOSHQACHA: eski server BUTUN guruh matritsasini bitta
 * javobda berardi, v2 endpointi esa DARS kesimida ishlaydi
 * (`/live-sessions/{id}/attendance`). Shuning uchun matritsa bir nechta
 * varaqdan shu yerda YIG'ILADI, ustunlar soni esa ataylab cheklanadi:
 * 8 oylik guruhda 69 dars bor va hammasi uchun 69 ta so'rov yuborish
 * mumkin emas.
 *
 * ★ FUNKSIYALAR UMUMIY (R24 dan keyin): "Davomat" va "Baholar" tablari
 * AYNI shakldagi matritsani quradi (qator — o'quvchi, ustun — dars,
 * varaqlar `sessionId` kesimida). Ikkinchi nusxa yozilsa, ikki jadval
 * bir kun kelib boshqa-boshqa o'quvchilar ro'yxatini ko'rsatib qolardi
 * — shuning uchun bu yerdagi uchala funksiya ham STRUKTURAL turlar
 * ustida ishlaydi va ikkala varaq turini ham qabul qiladi.
 */

/** Varaqning matritsa uchun kerak bo'lgan YAGONA qismi. */
interface MatrixSheet<TRow> {
  sessionId: number
  rows: TRow[] | null
}

/** Qatorning matritsa uchun kerak bo'lgan YAGONA qismi. */
interface MatrixRowLike {
  studentId: number
  studentName: string | null
}

/** Bir ekranda ochiladigan ustunlar (darslar) soni. */
export const ATTENDANCE_WINDOW = 10

/**
 * Qaysi darslar ustun bo'lishini tanlaydi.
 *
 * Mantiq eski ilovadagi "bugungi ustunga avtomatik siljish" xatti-harakati
 * bilan bir xil maqsadga xizmat qiladi: ustoz avvalo YAQIN darslarni
 * ko'rmoqchi. Shuning uchun oyna BUGUNDAN ORQAGA olinadi va oxirgi ustun
 * — joriy yoki eng yaqin dars.
 *
 * Guruh hali boshlanmagan bo'lsa (hamma dars kelajakda) — BIRINCHI darslar
 * ko'rsatiladi: ularni oldindan belgilash ham mumkin.
 */
export function selectAttendanceColumns<T extends { id: number; scheduledStart: string }>(
  sessions: readonly T[],
  limit: number,
  now: Date,
): T[] {
  const ordered = [...sessions].sort(
    (a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime(),
  )

  // "Bugun + ertaga" chegarasi: bugungi dars hali boshlanmagan bo'lsa ham
  // ustunda tursin (ustoz darsdan oldin belgilay oladi).
  const horizon = now.getTime() + 24 * 60 * 60 * 1000
  const started = ordered.filter((item) => new Date(item.scheduledStart).getTime() <= horizon)

  if (started.length > 0) return started.slice(-limit)
  return ordered.slice(0, limit)
}

export interface MatrixStudent {
  studentId: number
  name: string
}

/**
 * Ustunlardagi barcha o'quvchilar birlashmasi.
 *
 * Server har varaqni ism bo'yicha saralab beradi, lekin arxivlangan
 * o'quvchi faqat AYRIM darslarda uchraydi — shuning uchun birlashma
 * oxirida qayta saralanadi, aks holda u ro'yxat o'rtasiga tushib qolardi.
 */
export function collectStudents<TRow extends MatrixRowLike>(
  sheets: readonly MatrixSheet<TRow>[],
): MatrixStudent[] {
  const map = new Map<number, string>()
  for (const sheet of sheets) {
    for (const row of sheet.rows ?? []) {
      if (!map.has(row.studentId)) map.set(row.studentId, row.studentName ?? `#${row.studentId}`)
    }
  }
  return [...map.entries()]
    .map(([studentId, name]) => ({ studentId, name }))
    .sort((a, b) => a.name.localeCompare(b.name))
}

/** `sessionId:studentId` -> qator. */
export function indexRows<TRow extends MatrixRowLike>(
  sheets: readonly MatrixSheet<TRow>[],
): Map<string, TRow> {
  const map = new Map<string, TRow>()
  for (const sheet of sheets) {
    for (const row of sheet.rows ?? []) {
      map.set(`${sheet.sessionId}:${row.studentId}`, row)
    }
  }
  return map
}
