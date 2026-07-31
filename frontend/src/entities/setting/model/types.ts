import type { SettingDto, SettingOriginName, SettingsPageDto } from '@/shared/types'

/**
 * Sozlamalar keshining YAGONA kaliti.
 *
 * Bitta joyda turadi, chunki uni ikki tomon o'qiydi: ro'yxat so'rovi va
 * saqlashdan keyingi nuqtali yangilash (`replaceSettingInPage`). Satrni
 * qo'lda ikki joyda yozsak, birini o'zgartirib ikkinchisini unutish
 * "saqlandi, lekin ekran eski qiymatni ko'rsatyapti" xatosini berardi.
 */
export const SETTINGS_QUERY_KEY = ['settings'] as const

/** `BaseBadge` ning `tone` prop'i bilan bir xil to'plam. */
export type SettingTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger'

/* ================================================================ manba === */

/*
  Manba nomlari ATAYLAB "qayerdan kelgani" tilida, texnik enum nomida emas:
  admin uchun muhim savol — "bu qiymatni men paneldan qo'yganmanmi yoki u
  serverdagi faylda turibdimi?". Javob "Environment" so'zida emas, "muhitda,
  paneldan o'zgarmaydi" jumlasida.
*/
const ORIGIN_LABELS: Record<SettingOriginName, string> = {
  Default: 'Standart',
  Environment: 'Muhitdan',
  Database: 'Paneldan',
}

const ORIGIN_TONES: Record<SettingOriginName, SettingTone> = {
  // Standart — hech kim tegmagan, kulrang.
  Default: 'neutral',
  // Muhitdan — qiymat serverda qotib turibdi, bu OGOHLANTIRISH emas, ma'lumot.
  Environment: 'accent',
  // Paneldan — kimdir qo'lda o'zgartirgan; ko'zga tashlanishi kerak.
  Database: 'warning',
}

export function settingOriginLabel(origin: SettingOriginName): string {
  return ORIGIN_LABELS[origin]
}

export function settingOriginTone(origin: SettingOriginName): SettingTone {
  return ORIGIN_TONES[origin]
}

/**
 * Bazadagi ustki qiymatni olib tashlash MUMKINMI.
 *
 * Server `origin !== Database` da 400 qaytaradi ("bu qiymat muhitdan
 * keladi..."), ya'ni tugma bosilsa faqat xato chiqardi. Shu sababli u
 * o'chirilgan holda emas, UMUMAN ko'rsatilmaydi — bosib bo'lmaydigan tugma
 * "tizim buzuq" degan taassurot qoldiradi.
 */
export function canResetSetting(setting: SettingDto): boolean {
  return setting.origin === 'Database'
}

/* =============================================================== Toggle === */

/*
  ★ TOGGLE QIYMATI — SATR, VA SERVER KATTA HARF BILAN HAM YUBORADI.

  Jonli javobda `finance.enforce_block` uchun `value: "True"`, ammo
  `defaultValue: "true"` keldi — ya'ni ikki manba ikki xil harf registrida
  yozadi. `value === 'true'` deb tekshirsak, yoqilgan sozlama ekranda
  o'chiq ko'rinardi. Shuning uchun taqqoslash registrsiz.
*/
export function isToggleOn(value: string | null): boolean {
  return value !== null && value.trim().toLowerCase() === 'true'
}

/**
 * Toggle uchun serverga yuboriladigan KANONIK satr.
 *
 * Kichik harf tanlandi, chunki serverning O'ZI `defaultValue` da aynan
 * `"true"` yozadi — ya'ni bu qiymat muallif ko'zlagan shakl. (.NET
 * `bool.TryParse` registrga befarq, lekin kanonik shaklga tayanish
 * kelajakda qattiqroq tekshiruv qo'yilsa ham buzilmaydi.)
 */
export function toggleValueText(on: boolean): string {
  return on ? 'true' : 'false'
}

/* ========================================================== ko'rsatish === */

/**
 * Sozlama TAHRIRLANMAGANDA yoki sir bo'lganda ekranda nima chiziladi.
 *
 * ★ Sir uchun FAQAT `maskedValue` — `value` serverdan `null` keladi va
 * boshqa manba yo'q. Sir o'rnatilmagan bo'lsa (`isSet: false`) mask ham
 * yo'q, shuning uchun ochiq matn beriladi: bo'sh joy "yuklanmadi" deb
 * o'qilardi.
 */
export function settingDisplayText(setting: SettingDto): string {
  if (setting.isSecret) {
    if (!setting.isSet) return 'Kiritilmagan'
    return setting.maskedValue ?? '••••••••'
  }
  if (setting.value === null || setting.value.length === 0) return 'Kiritilmagan'
  return setting.value
}

/* ================================================================ kesh === */

/**
 * Keshdagi sahifada BITTA sozlamani almashtiradi.
 *
 * NEGA butun ro'yxatni qayta so'ramaymiz: sahifada 19 ta sozlama bor va
 * ularning bir nechtasida tahrir davom etayotgan bo'lishi mumkin. To'liq
 * `invalidate` javobi kelganda o'sha yozilayotgan maydonlar ham server
 * qiymatiga qaytib, admin terayotgan matnni yo'qotardi. Bu funksiya faqat
 * saqlangan kalitga tegadi.
 *
 * Yangi obyektlar qaytaradi (mutatsiya yo'q) — Vue reaktivligi va TanStack
 * Query'ning tenglik tekshiruvi shunga tayanadi.
 */
export function replaceSettingInPage(page: SettingsPageDto, updated: SettingDto): SettingsPageDto {
  return {
    groups: page.groups.map((group) =>
      group.group !== updated.group
        ? group
        : {
            ...group,
            items: group.items.map((item) => (item.key === updated.key ? updated : item)),
          },
    ),
  }
}
