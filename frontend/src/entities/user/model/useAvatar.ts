import { onScopeDispose, ref, toValue, watch } from 'vue'
import type { MaybeRefOrGetter, Ref } from 'vue'

import { fetchAvatarBlob } from '../api/profile-api'

/**
 * ============================================================================
 *  PROFIL RASMI — `<img src>` UCHUN `blob:` MANZIL (2026-08-15)
 * ============================================================================
 *
 * ── MUAMMO ─────────────────────────────────────────────────────────────────
 *
 * 🔴 Brauzerning rasm yuklovchisi `Authorization` sarlavhasini
 * YUBORMAYDI, `GET /api/v1/profile/avatar/{id}` esa uni TALAB qiladi.
 * Ya'ni manzilni to'g'ridan-to'g'ri `<img src>` ga qo'yish 401 beradi.
 * (Dars videosi bilan AYNI cheklov — `IMediaAccessTicketService` izohi.)
 *
 * ── NIMA UCHUN CHIPTA (TICKET) EMAS ────────────────────────────────────────
 *
 * Dars videosi uchun qisqa muddatli CHIPTA mexanizmi bor va u shu
 * muammoni hal qiladi. Avatar uchun u ISHLATILMADI, sabab:
 *
 *   • video — o'nlab megabayt va u `Range` bilan bo'lak-bo'lak
 *     o'qiladi, ya'ni uni `Blob` sifatida xotiraga olish mumkin emas;
 *     avatar esa bir necha yuz kilobayt va u BUTUNLAY yuklanadi;
 *   • chipta har asset uchun imzolanadi va uning muddati tugaydi —
 *     ya'ni sahifa uzoq ochiq tursa rasm birdan yo'qolardi;
 *   • `Blob` yechimi hech qanday yangi endpoint, imzo va muddat
 *     talab qilmaydi.
 *
 * ── NIMA UCHUN `shared/lib` DA EMAS, `entities/user` DA ────────────────────
 *
 * ★ FSD QOIDASI: bog'lanish faqat PASTGA qaraydi. Bu kompozitsiya
 * `fetchAvatarBlob` ga (ya'ni `entities/user` ga) tayanadi, demak u
 * `shared` da tura olmaydi — u yerdan yuqoriga murojaat qilish qatlam
 * tartibini buzardi va keyingi dasturchi uchun "shared hamma narsani
 * biladi" degan yomon o'rnak bo'lardi.
 *
 * ── KESH: MODUL DARAJASIDA, KOMPONENTDA EMAS ───────────────────────────────
 *
 * ★ Avatar BIR SAHIFADA BIR NECHA MARTA chiziladi (sarlavha, yon menyu,
 * profil oynasi). Kesh komponent ichida bo'lsa, har biri AYNI rasmni
 * qaytadan yuklab, uchta `blob:` manzil yasardi.
 *
 * Kalit — `userId` va VERSIYA juftligi: rasm almashtirilganda versiya
 * o'zgaradi, ya'ni eski yozuv o'z-o'zidan chetlab o'tiladi.
 *
 * ⚠️ `URL.revokeObjectURL` ATAYLAB CHAQIRILMAYDI (bitta istisno —
 * `dropAvatar`). `blob:` manzil bekor qilinsa, o'sha rasmni ko'rsatib
 * turgan BOSHQA komponent ham bo'sh kvadratga aylanadi. Kesh o'lchami
 * amalda bir necha element (foydalanuvchi bir sessiyada o'nlab avatarni
 * ko'rmaydi), ya'ni xotira sarfi sezilmaydi.
 */

/** `userId|version` -> `blob:` manzil (yoki yuklanayotgan va'da). */
const cache = new Map<string, Promise<string>>()

function keyOf(userId: number, version: string): string {
  return `${userId}|${version}`
}

function load(userId: number, version: string): Promise<string> {
  const key = keyOf(userId, version)
  const existing = cache.get(key)

  if (existing !== undefined) return existing

  const promise = fetchAvatarBlob(userId, version)
    .then(async (blob) => {
      const url = URL.createObjectURL(blob)

      /*
        ★ RASM DEKODLANGUNCHA KUTAMIZ (2026-08-15 da qo'shildi).

        🔴 SABAB — CHAQNASH: `<img src>` ga yangi manzil berilganda
        brauzer faylni O'QIB, DEKODLAB bo'lgunicha elementni BO'SH
        chizadi. Almashtirish paytida bu "bir zumga o'chib yonish"
        bo'lib ko'rinardi (loyiha egasining shikoyati).

        Manzil shu yerda oldindan yuklab qo'yiladi, ya'ni komponent uni
        o'rnatganda rasm brauzer keshida TAYYOR turadi va almashish
        BITTA freymda, uzilishsiz bo'ladi.

        ★ `onerror` da ham `resolve`: yaroqsiz bayt kelsa ham osilib
        qolmaymiz — quyidagi `<img>` o'zi bo'sh qoladi va ekranda ism
        harfi ko'rinadi.
      */
      await new Promise<void>((resolve) => {
        const probe = new Image()
        probe.onload = () => {
          resolve()
        }
        probe.onerror = () => {
          resolve()
        }
        probe.src = url
      })

      return url
    })
    .catch((error: unknown) => {
      // Yiqilgan va'da KESHDA QOLDIRILMAYDI: aks holda bir martalik
      // tarmoq uzilishi rasmni butun sessiya davomida "yo'q" qilib
      // qo'yardi.
      cache.delete(key)
      throw error
    })

  cache.set(key, promise)

  return promise
}

/**
 * Rasm keshdan CHIQARILADI va `blob:` manzili bekor qilinadi.
 *
 * Chaqiruvchi: rasm O'CHIRILGANDA yoki YANGISI yuklanganda. Bu yagona
 * joy — yuqoridagi izohda tushuntirilganidek, boshqa hollarda bekor
 * qilish boshqa komponentlarning rasmini buzardi.
 */
export function dropAvatar(userId: number, version: string | null): void {
  if (version === null) return

  const key = keyOf(userId, version)
  const entry = cache.get(key)

  if (entry === undefined) return

  // Keshdan DARHOL chiqariladi: keyingi so'rov yangi nusxa oladi.
  cache.delete(key)

  void entry.then((url) => {
    /*
      🔴 BEKOR QILISH KECHIKTIRILADI — VA BU CHAQNASHNING ASOSIY SABABI
      EDI (2026-08-15 da topildi).

      `URL.revokeObjectURL` chaqirilgan zahoti o'sha manzilni KO'RSATIB
      TURGAN har bir `<img>` bo'sh bo'lib qoladi. Rasm almashtirilganda
      esa eski manzil hali ekranda edi: sarlavhadagi, yon menyudagi va
      oynadagi avatar. Ular yangi rasmga o'tguncha (bir necha yuz
      millisekund) UCHALASI ham bo'sh turardi — ko'z buni "o'chib yonish"
      deb qabul qiladi.

      Kechikish ular yangi manzilga o'tib bo'lishiga yetadi; undan keyin
      eski `blob:` hech qayerda ishlatilmaydi va xotira bo'shatiladi.
    */
    window.setTimeout(() => {
      URL.revokeObjectURL(url)
    }, REVOKE_DELAY_MS)
  }).catch(() => {
    // Yiqilgan va'da — bekor qiladigan manzil yo'q.
  })
}

/**
 * Eski `blob:` manzil qancha vaqtdan keyin bekor qilinadi.
 *
 * ★ 10 SONIYA — "yetarlicha ko'p": yangi rasm sekin tarmoqda ham shu
 * vaqt ichida yuklanadi. Xotira narxi nolga yaqin (bir nechta rasm), 
 * erta bekor qilishning narxi esa — ko'rinadigan chaqnash.
 */
const REVOKE_DELAY_MS = 10_000

/**
 * Foydalanuvchi rasmining `blob:` manzili.
 *
 * `null` — rasm yo'q yoki hali yuklanmadi; chaqiruvchi bu holatda ism
 * harfini chizadi (ya'ni "bo'sh kvadrat" hech qachon ko'rinmaydi).
 *
 * @param userId Kimning rasmi.
 * @param version `avatarUpdatedAt`. `null` bo'lsa rasm YO'Q — so'rov ham
 * yuborilmaydi (404 ni bekorga kutib o'tirmaymiz).
 */
export function useAvatar(
  userId: MaybeRefOrGetter<number | null | undefined>,
  version: MaybeRefOrGetter<string | null | undefined>,
): Readonly<Ref<string | null>> {
  const url = ref<string | null>(null)

  watch(
    [() => toValue(userId), () => toValue(version)],
    ([id, ver]) => {
      if (typeof id !== 'number' || ver === null || ver === undefined || ver === '') {
        url.value = null
        return
      }

      // ★ POYGA HIMOYASI: yuklash tugagunicha `userId`/versiya yana
      //   o'zgargan bo'lishi mumkin (ro'yxatda tez skroll). Javob
      //   kelganda so'ralgan kalit HALI HAM joriymi — tekshiramiz,
      //   aks holda eski rasm yangisining ustiga chizilardi.
      const requested = keyOf(id, ver)

      load(id, ver)
        .then((value) => {
          if (keyOf(toValue(userId) as number, toValue(version) as string) === requested) {
            url.value = value
          }
        })
        .catch(() => {
          // Rasm yuklanmadi — ism harfi chiziladi. Konsolga yozmaymiz:
          // avatar ikkinchi darajali element va uning xatosi
          // foydalanuvchiga hech nima anglatmaydi.
          if (keyOf(toValue(userId) as number, toValue(version) as string) === requested) {
            url.value = null
          }
        })
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    // `revokeObjectURL` CHAQIRILMAYDI — sabab fayl tepasidagi izohda.
    url.value = null
  })

  return url
}
