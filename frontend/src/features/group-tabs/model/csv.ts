import { saveBlob } from '@/shared/lib/download'

/**
 * Excel uchun UTF-8 BOM.
 *
 * ★ SHART: usiz Excel faylni ANSI deb o'qiydi va o'zbekcha harflar
 * ("O‘quvchi") krakozyabra bo'lib ochiladi. Eski ilova ham aynan shu uch
 * baytni qo'shardi (`new Uint8Array([0xEF, 0xBB, 0xBF])`).
 */
const BOM = '\uFEFF'

/**
 * Jadvalni CSV qilib yuklab berish — eski "CSV yuklab olish" tugmalari
 * (`exportAttendance()`, `exportGrades()`).
 *
 * Har katak qo'shtirnoq ichida: o'quvchi ismida vergul yoki qator uzilishi
 * bo'lsa ham ustunlar surilib ketmaydi.
 */
export function downloadCsv(fileName: string, rows: readonly (readonly string[])[]): void {
  const body = rows
    .map((row) => row.map((cell) => `"${cell.replaceAll('"', '""')}"`).join(','))
    .join('\n')

  saveBlob(new Blob([`${BOM}${body}`], { type: 'text/csv;charset=utf-8;' }), fileName)
}
