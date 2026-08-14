import { isManagerRole } from '@/entities/user'
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

/**
 * ★ O'QUV BO'LIMI/ADMIN UCHUN BIRINCHI TAB — o'quvchilar ro'yxati.
 *
 * Talab (loyiha egasi): *"guruh ichiga kirilganda o'quvchilar ro'yxati
 * birinchi o'rinda"*. Bu eski ilova bilan dizayn paritetining "tartib
 * aynan" mezoniga ATAYLAB qilingan chekinish.
 *
 * 🔴 QAMROV FAQAT `Academic`/`Admin`: ular guruhga ro'yxat bilan ishlash
 * uchun kiradi. USTOZ VA KURATORDA TARTIB TEGILMAYDI — ular kunda darsga
 * kiradi va birinchi tab o'zgarsa har kunlik ish oqimi buzilardi.
 *
 * ★ IKKI NUSXA RO'YXAT YASALMADI: bitta `GROUP_TABS` + rolga qarab
 * tartiblash. Ikki nusxada yangi tab bittasiga qo'shilib ikkinchisida
 * unutilardi.
 */
const MANAGER_FIRST_TAB: GroupTabKey = 'students'

/**
 * Rolga mos KO'RINADIGAN tablar, TO'G'RI TARTIBDA.
 *
 * `role` — `useAuthStore().role` (`null` bo'lsa eng cheklangan variant:
 * standart tartib, hech narsa yashirilmaydi).
 */
export function visibleGroupTabs(role: string | null): GroupTabDef[] {
  const visible =
    role === 'Assistant'
      ? GROUP_TABS.filter((tab) => !CURATOR_HIDDEN_TABS.includes(tab.key))
      : [...GROUP_TABS]

  if (role === null || !isManagerRole(role)) return visible

  return [
    ...visible.filter((tab) => tab.key === MANAGER_FIRST_TAB),
    ...visible.filter((tab) => tab.key !== MANAGER_FIRST_TAB),
  ]
}

/**
 * Sahifa ochilganda qaysi tab faol bo'lishi.
 *
 * ★ ATAYLAB "ko'rinadigan tablarning BIRINCHISI": standart tab qattiq
 * yozilsa (masalan `'att'`) u rolda YASHIRILGAN bo'lib qolishi mumkin edi
 * (kuratorda `tests`) va sahifa bo'sh ochilardi. Shu bilan birga bu qoida
 * "o'quvchilar birinchi" talabini o'z-o'zidan bajaradi.
 */
export function defaultGroupTab(role: string | null): GroupTabKey {
  return visibleGroupTabs(role)[0]?.key ?? 'att'
}
