import type { IconName } from '@/shared/ui'

/**
 * Guruh ichidagi tablar — eski `teacher.html` dagi `.tabs` bloki.
 *
 * ★ TARTIB, NOM va IKONKA eski markupdan AYNAN olingan va o'zgartirilmaydi:
 * ustozlar shu ketma-ketlikni yodlab olishgan. Ikonkalar ham o'sha
 * sprite'dan (`i-att`, `i-grade`, `i-lesson`, `i-task`, `i-test`, `i-board`,
 * `i-student`, `i-chat`) — `AppIcon` ga qo'shilgan.
 *
 * ★ TO'QQIZINCHI TAB — "Yozuvlar". U eski `academic.html` dagi guruh
 * tafsiloti panelidan olingan (615-qator: `tab('t-recordings')`, yorlig'i
 * "🎬 Yozuvlar", 663–674-qatorlarda mazmuni). U yerda AYNAN OXIRGI tab
 * bo'lgan, shu o'rni saqlandi va mavjud sakkiztaning tartibi TEGILMAGAN.
 * v2 da guruh sahifasi ustoz va o'quv bo'limi uchun BITTA, shuning uchun
 * tab ikkala rolda ham ko'rinadi — server ro'yxatni o'zi cheklaydi.
 *
 * Tab ALOHIDA MARSHRUT emas, sahifa ichidagi holat — eski `switchTab()`
 * kabi. Sabab: har tab almashishida brauzer tarixiga yozuv qo'shilsa,
 * "orqaga" tugmasi guruhdan chiqarmasdan tablar orasida sakrardi.
 */
export type GroupTabKey =
  | 'att'
  | 'grades'
  | 'lessons'
  | 'tasks'
  | 'tests'
  | 'board'
  | 'students'
  | 'chat'
  | 'recordings'

export interface GroupTabDef {
  key: GroupTabKey
  label: string
  icon: IconName
}

export const GROUP_TABS: readonly GroupTabDef[] = [
  { key: 'att', label: 'Davomat', icon: 'check-square' },
  { key: 'grades', label: 'Baholar', icon: 'award' },
  { key: 'lessons', label: 'Darslar', icon: 'calendar' },
  { key: 'tasks', label: 'Vazifalar', icon: 'clipboard' },
  { key: 'tests', label: 'Testlar', icon: 'file-text' },
  { key: 'board', label: 'Reyting', icon: 'trophy' },
  { key: 'students', label: 'O‘quvchilar', icon: 'user' },
  { key: 'chat', label: 'Chat', icon: 'chat' },
  // Yorliqdagi 🎬 emoji ko'chirilmadi: qolgan sakkiztasida emoji yo'q va
  // bittasida bo'lishi qatorni notekis ko'rsatardi. Ikonka o'sha ma'noni beradi.
  { key: 'recordings', label: 'Yozuvlar', icon: 'camera' },
]

/**
 * ★ KURATOR UCHUN YASHIRINADIGAN TABLAR — eski qoida (`teacher.html`,
 * `openGroup()`: `isCurator` bo'lsa `tests` va `board` tugmalari
 * `display:none`).
 *
 * FARQ: eski ilova tugmani DOM'da qoldirib faqat yashirardi, v2 esa uni
 * umuman CHIZMAYDI — yashirilgan element brauzer inspektorida ham,
 * klaviatura bilan Tab bosganda ham topilmasin.
 */
const CURATOR_HIDDEN_TABS: readonly GroupTabKey[] = ['tests', 'board']

export function visibleGroupTabs(isCurator: boolean): GroupTabDef[] {
  if (!isCurator) return [...GROUP_TABS]
  return GROUP_TABS.filter((tab) => !CURATOR_HIDDEN_TABS.includes(tab.key))
}
