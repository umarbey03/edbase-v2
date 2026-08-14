import type { GroupCategoryDto, GroupDto } from '@/shared/types'

/**
 * GURUH KATEGORIYASI (R21b) — ko'rsatish yordamchilari.
 *
 * ★ NEGA ALOHIDA ENTITY, `entities/group` ICHIDA EMAS: kategoriya guruhdan
 * MUSTAQIL hayot siklga ega (o'z CRUD'i, o'z boshqaruv ekrani) va uni
 * chatlar ro'yxati ham ishlatadi — u yerda esa `GroupDto` umuman yo'q.
 */

/** Yorliqsiz guruh uchun ko'rsatiladigan matn — HAMMA joyda AYNI. */
export const NO_CATEGORY_LABEL = 'Yo‘nalish tanlanmagan'

/**
 * Guruh kartochkasi/jadvali uchun kategoriya yorlig'i.
 *
 * ★ `categoryName` BO'SH SATR bo'lishi ham mumkin (nazariy jihatdan — server
 * bo'sh nomni rad etadi, lekin tur `string | null`), shuning uchun uzunlik
 * tekshiriladi, faqat `null` emas.
 */
export function groupCategoryLabel(group: Pick<GroupDto, 'categoryName'>): string {
  const name = group.categoryName ?? ''
  return name.length > 0 ? name : '—'
}

/**
 * Tanlagich uchun band matni: arxivlangan kategoriya ochiq belgilanadi.
 *
 * NEGA KERAK: guruhda ARXIVLANGAN kategoriya turgan bo'lishi mumkin va u
 * tanlagichga qaytariladi (aks holda saqlashda yorliq jimgina uzilardi —
 * `PUT` to'liq almashtirish). Belgisiz u faol kategoriyadan farq qilmasdi
 * va xodim uni bexosdan boshqa guruhlarga ham tanlab yuborardi.
 */
export function groupCategoryOptionLabel(category: GroupCategoryDto): string {
  const name = category.name ?? '—'
  return category.isActive ? name : `${name} (arxiv)`
}
