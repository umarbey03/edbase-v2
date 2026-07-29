/**
 * Xaritadan qiymat oladi; kalit topilmasa `fallback` qaytaradi.
 *
 * Backend kutilmagan enum nomi yuborsa (masalan yangi rol qo'shilsa) UI qulamasligi
 * kerak. `Record<Union, V>` ishlatamiz — shunda barcha variantlarni yozganimizni
 * kompilyator tekshiradi, lekin o'qishda xavfsiz fallback bo'ladi.
 */
export function lookup<V>(map: Readonly<Record<string, V>>, key: string, fallback: NoInfer<V>): V {
  return map[key] ?? fallback
}
