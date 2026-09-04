import type { PayrollRuleKindName } from '@/shared/types'

/**
 * OYLIK HISOBLASH TURLARI QO'LLANMASI — platforma ichidagi izoh.
 *
 * ★ NIMA UCHUN KOD ICHIDA, HUJJATDA EMAS: admin qoida yaratayotganda
 * "bu tur aynan qanday hisoblaydi?" degan savolga o'sha yerning o'zida
 * javob olishi kerak. Tashqi hujjatga havola — amalda hech kim ochmaydi.
 *
 * ★ YAGONA MANBA: bu fayl formadagi izoh bloki VA "Qanday hisoblanadi?"
 * drawer'i uchun bitta manba. Matn ikki joyda takrorlansa vaqt o'tib
 * bir-biridan uzilardi.
 *
 * ★ FORMULALAR BACKEND BILAN AYNI: `PayrollCalculator.ComputeSession` /
 * `ComputePeriod`. Backend o'zgarsa bu fayl ham o'zgarishi SHART —
 * noto'g'ri izoh izohsizdan yomon.
 */

export interface PayrollKindGuideEntry {
  kind: PayrollRuleKindName
  /** `session` — har dars uchun; `period` — oyga bir marta. */
  scope: 'session' | 'period'
  /** Bir gapli mohiyat. */
  summary: string
  /** Formula — oddiy matn, `×` va `÷` bilan. */
  formula: string
  /** Sonli misol — qadam-baqadam. Oxirgi qator natija. */
  example: ReadonlyArray<string>
  /** Formada shu tur uchun ma'noli maydonlar. */
  fields: ReadonlyArray<string>
  /** Chekka holatlar va tez-tez so'raladigan savollar. */
  notes: ReadonlyArray<string>
}

const GUIDE: Record<PayrollRuleKindName, PayrollKindGuideEntry> = {
  PerSession: {
    kind: 'PerSession',
    scope: 'session',
    summary: 'Yakunlangan har bir dars uchun bir xil belgilangan summa.',
    formula: 'Summa × (dam olish/bayram ustamasi)',
    example: [
      'Stavka: 40 000 so‘m.',
      'Oyda 12 ta dars yakunlangan, shundan 2 tasi bayram kuni (ustama 1.5).',
      '10 × 40 000 = 400 000 so‘m',
      '2 × 40 000 × 1.5 = 120 000 so‘m',
      'Natija: 520 000 so‘m',
    ],
    fields: ['Summa', 'Minimal davomiylik', 'Dam olish/bayram ustamasi', 'Maqsad (rol, xodim, kurs, guruh…)'],
    notes: [
      'Dars davomiyligi va o‘quvchi soni natijaga ta’sir qilmaydi.',
      'Dars «Minimal davomiylik» dan qisqa bo‘lsa, qoida shu darsga tushmaydi.',
      'Dars yakunlangan paytdagi stavka muzlatiladi — qoidani keyin o‘zgartirish o‘tgan darslarni qayta hisoblamaydi.',
    ],
  },

  PerAcademicHour: {
    kind: 'PerAcademicHour',
    scope: 'session',
    summary: 'Dars davomiyligi akademik soatga bo‘linadi va stavkaga ko‘paytiriladi.',
    formula: 'Summa × (dars daqiqasi ÷ akademik soat daqiqasi) × ustama',
    example: [
      'Stavka: 30 000 so‘m / akademik soat. Akademik soat: 45 daqiqa.',
      'Dars 80 daqiqa davom etdi.',
      '80 ÷ 45 = 1.78 soat (ikki xonagacha yaxlitlanadi)',
      '30 000 × 1.78 = 53 400 so‘m',
      'Natija: 53 400 so‘m',
    ],
    fields: ['Summa', 'Akademik soat (daqiqa)', 'Minimal davomiylik', 'Dam olish/bayram ustamasi', 'Maqsad'],
    notes: [
      'Akademik soat 10..240 daqiqa oralig‘ida. Standart — 45.',
      'Davomiyligi turlicha darslar (60, 80, 90 daqiqa) uchun eng adolatli tur.',
      'Soat soni avval yaxlitlanadi, keyin summaga ko‘paytiriladi — hisobotdagi «1.78 akademik soat» yozuvi shundan.',
    ],
  },

  PerAttendedStudent: {
    kind: 'PerAttendedStudent',
    scope: 'session',
    summary: 'Darsdagi har bir sanaladigan o‘quvchi uchun qo‘shimcha summa — bonus.',
    formula: 'Summa × o‘quvchi soni (asos bo‘yicha)',
    example: [
      'Stavka: 5 000 so‘m / o‘quvchi. Asos: «Faqat kelganlar».',
      'Darsda 8 ta a’zo bor, 6 tasi keldi, 1 tasi sababli kelmadi.',
      '6 × 5 000 = 30 000 so‘m',
      'Natija: 30 000 so‘m (asos «Kelgan + sababli» bo‘lsa 7 × 5 000 = 35 000)',
    ],
    fields: ['Summa', 'Asos (kim sanaladi)', 'Min/Max o‘quvchi', 'Maqsad'],
    notes: [
      'Bu tur BONUS hisoblanadi: dam olish/bayram ustamasi unga qo‘llanmaydi.',
      'Odatda «Har dars uchun» yoki «Akademik soat» bilan BIRGA ishlatiladi — turlar qo‘shiladi.',
      '«Min o‘quvchi» / «Max o‘quvchi» — bu oraliqdan tashqarida qoida shu darsga umuman tushmaydi.',
    ],
  },

  TieredByAttendance: {
    kind: 'TieredByAttendance',
    scope: 'session',
    summary: 'Suzuvchi stavka: o‘quvchi soniga qarab bosqichli summa.',
    formula: 'Bosqich summasi (o‘quvchi soniga eng yaqin pastki bosqich) × ustama',
    example: [
      'Bosqichlar: 0 ta → 20 000, 3 ta → 35 000, 6 ta → 50 000 so‘m.',
      'Darsga 4 ta o‘quvchi keldi → «3 ta» bosqichi ishlaydi.',
      'Natija: 35 000 so‘m',
      'Darsga 10 ta kelsa → eng yuqori «6 ta» bosqichi: 50 000 so‘m.',
      'Hech kim kelmasa → «0 ta» bosqichi: 20 000 so‘m.',
    ],
    fields: ['Bosqichlar jadvali', 'Asos (kim sanaladi)', 'Min/Max o‘quvchi', 'Dam olish/bayram ustamasi', 'Maqsad'],
    notes: [
      '«Summa» maydoni bu turda ishlatilmaydi — bosqichlar jadvali ustun.',
      'O‘quvchi soniga TENG yoki undan KICHIK eng katta bosqich olinadi.',
      '«0 ta» bosqichi bo‘lmasa va hech kim kelmasa — 0 so‘m. Bo‘sh darsga ham to‘lash kerak bo‘lsa, 0 bosqichini qo‘shing.',
    ],
  },

  FixedMonthly: {
    kind: 'FixedMonthly',
    scope: 'period',
    summary: 'Oklad — dars soniga bog‘liq emas, oyga bir marta qo‘shiladi.',
    formula: 'Summa (oyga bir marta)',
    example: [
      'Oklad: 3 000 000 so‘m.',
      'Oyda nechta dars bo‘lganidan qat’i nazar:',
      'Natija: 3 000 000 so‘m',
    ],
    fields: ['Summa', 'Rol yoki aniq xodim', 'Amal muddati'],
    notes: [
      'Guruh, kurs yoki kategoriyaga bog‘lab bo‘lmaydi — oklad xodimning butun oyiga tegishli.',
      'Oyda ishlagan kunlar hisobga olinmaydi: yarim oy uchun yarim oklad kerak bo‘lsa, «Tuzatish» qo‘shing.',
      'Ayrim guruhlar «oklad ichida» bo‘lsa — «Guruh bo‘yicha istisnolar» bo‘limida belgilang, o‘sha darslarga stavka tushmaydi.',
      'Oyni yopish paytida AMALDA bo‘lgan qoida ishlaydi (davr oxiridagi sana bo‘yicha).',
    ],
  },

  PercentOfRevenue: {
    kind: 'PercentOfRevenue',
    scope: 'period',
    summary: 'Guruhlar bo‘yicha HISOBLANGAN o‘quv haqidan foiz. Reja bajarilsa foiz oshadi.',
    formula: 'Tushum × foiz ÷ 100 (rejaga yetilgan bo‘lsa — oshirilgan foiz)',
    example: [
      'Foiz: 10%. Reja: 5 000 000 so‘m, reja bajarilgach: 15%.',
      'Ustozning guruhlariga oyda 4 200 000 so‘m o‘quv haqi hisoblangan.',
      '4 200 000 < 5 000 000 → reja bajarilmadi → 10%',
      '4 200 000 × 10% = 420 000 so‘m',
      'Tushum 6 000 000 bo‘lganda: 6 000 000 × 15% = 900 000 so‘m.',
    ],
    fields: ['Foiz (%)', 'Reja summasi', 'Reja bajarilgach foiz', 'Maqsad (kurs, kategoriya, guruh…)'],
    notes: [
      'Tushum — o‘quvchiga HISOBLANGAN dars haqi (yakunlangan darslar bo‘yicha), o‘quvchi to‘lagan pul EMAS. Ustoz haqi o‘quvchining to‘lov intizomiga bog‘lanmaydi.',
      'Har guruh o‘z qoidasini topadi: arab tili 10%, ingliz tili 8% — ikkalasi bitta ustozda bo‘lishi mumkin. Bir xil qoidaga tushgan guruhlar hisobotda bitta qatorga yig‘iladi.',
      'Reja xodimning BUTUN oylik tushumiga qaraladi, alohida qoida ulushiga emas.',
      'Bu davr turi: oy yopilganda jonli hisoblanadi, dars-dars muzlatilmaydi.',
    ],
  },

  MonthlyPerActiveStudent: {
    kind: 'MonthlyPerActiveStudent',
    scope: 'period',
    summary: 'Har faol o‘quvchi uchun oylik summa, o‘quvchi bo‘yicha koeffitsient bilan.',
    formula: 'Summa × (faol o‘quvchilar ulushlari yig‘indisi)',
    example: [
      'Stavka: 100 000 so‘m / faol o‘quvchi.',
      'Kuratorda 3 ta faol o‘quvchi. Bittasi 20-sentabrda qo‘shilgan → koeffitsienti 50%.',
      'Ulushlar: 1 + 1 + 0.5 = 2.5',
      '100 000 × 2.5 = 250 000 so‘m',
      'Natija: 250 000 so‘m',
    ],
    fields: ['Summa', 'Rol yoki aniq xodim', 'Amal muddati'],
    notes: [
      'Faol o‘quvchi — davr oxirida xodimning guruhlarida a’zo bo‘lib turgan o‘quvchi.',
      'Koeffitsient yozilmagan o‘quvchi 100% sanaladi. Koeffitsient faqat ISTISNO uchun va bitta oyga tegishli — keyingi oyda o‘quvchi to‘liq sanaladi.',
      'Koeffitsientlar «Oylik tafsiloti» oynasida xodim bo‘yicha qo‘yiladi.',
      'Guruh yoki kursga bog‘lab bo‘lmaydi — xodimning butun oyiga tegishli.',
    ],
  },
}

export function payrollKindGuide(kind: PayrollRuleKindName): PayrollKindGuideEntry {
  return GUIDE[kind]
}

/** Drawer uchun tartib: avval dars qamrovi, keyin davr — forma ro'yxati bilan bir xil. */
export const PAYROLL_KIND_GUIDE: ReadonlyArray<PayrollKindGuideEntry> = [
  GUIDE.PerSession,
  GUIDE.PerAcademicHour,
  GUIDE.PerAttendedStudent,
  GUIDE.TieredByAttendance,
  GUIDE.FixedMonthly,
  GUIDE.PercentOfRevenue,
  GUIDE.MonthlyPerActiveStudent,
]

export interface PayrollGuideSection {
  title: string
  items: ReadonlyArray<string>
}

/**
 * Turlardan tashqari UMUMIY qoidalar — dvigatelning "qanday birlashadi"
 * qismi. Bularni bilmasdan turlarni bilish yetarli emas.
 */
export const PAYROLL_GUIDE_SECTIONS: ReadonlyArray<PayrollGuideSection> = [
  {
    title: 'Qoidalar qanday birlashadi',
    items: [
      'BIR XIL turdagi qoidalardan faqat ENG ANIQI ishlaydi. Aniqlik: guruh (8) > xodim (4) > kurs (2) > kategoriya (1) = guruh turi (1). Ballar qo‘shiladi: «xodim + kurs» (6) «faqat guruh» (8) dan past.',
      'Aniqlik teng bo‘lsa — amal boshlanish sanasi kechrog‘i, u ham teng bo‘lsa — oxirgi yaratilgani ustun.',
      'TURLI turdagi qoidalar QO‘SHILADI: «oklad + akademik soat + darsdagi o‘quvchi bonusi» birga ishlaydi.',
      'Bitta darsga ikkita soatbay stavka HECH QACHON tushmaydi — kerak bo‘lsa aniqroq maqsadli qoida yarating.',
    ],
  },
  {
    title: 'Dars qamrovi va davr qamrovi',
    items: [
      'Dars qamrovi («Har dars», «Akademik soat», «Har o‘quvchi», «Bosqichli»): har dars yakunlanganda hisoblanadi va MUZLATILADI. Qoidani keyin tahrirlash o‘tgan darslarga ta’sir qilmaydi.',
      'Davr qamrovi («Oklad», «Tushumdan foiz», «Oylik: faol o‘quvchi»): oyga bir marta, davr oxiridagi holat bo‘yicha jonli hisoblanadi.',
      'Hisobotda «Asosiy» va «Bonus» ustunlari — dars qamrovi yig‘indisi, «Oylik qismi» ustuni — davr qamrovi.',
    ],
  },
  {
    title: 'Asos — kim sanaladi',
    items: [
      '«Faqat kelganlar» — davomatda «kelmadi» belgilanmagan o‘quvchilar (standart).',
      '«Kelgan + sababli kelmagan» — sababli kelmaganlar ham sanaladi: joy band, ustoz tayyorlangan.',
      '«Barcha a’zolar» — guruhning shu darsdagi barcha faol a’zolari, davomatdan qat’i nazar.',
      'Asos faqat o‘quvchi soniga bog‘liq turlarda ma’noli: «Har o‘quvchi» va «Bosqichli».',
    ],
  },
  {
    title: 'Dam olish va bayram ustamasi',
    items: [
      'Ustama faqat ASOSIY stavkaga: «Har dars», «Akademik soat», «Bosqichli». 1.5 = +50%.',
      '«Har o‘quvchi» bonusiga ustama qo‘llanmaydi — dam olish kuni ustozning mehnati qimmatroq, o‘quvchi soni emas.',
      'Shanba va yakshanba avtomatik dam olish kuni; bayramlar «Akademik sozlamalar» sahifasidagi ro‘yxatdan olinadi.',
    ],
  },
  {
    title: 'Guruh bo‘yicha istisnolar',
    items: [
      '«Avtomatik» — qoidalar odatdagidek moslanadi (standart).',
      '«Oklad ichida» — shu guruh darslariga dars qamrovidagi HECH QANDAY haq hisoblanmaydi; davr qamrovi (oklad, foiz) ta’sirlanmaydi.',
      '«Aniq qoida» — tanlangan bitta qoida majburan qo‘llanadi, maqsad tekshirilmaydi, lekin muddat va shartlar (min davomiylik, min/max o‘quvchi) kuchda qoladi.',
    ],
  },
  {
    title: 'Qoida topilmasa',
    items: [
      'Darsga birorta dars qamrovidagi qoida tushmasa — hisobotda «qoida topilmadi» ogohlantirishi chiqadi va dars 0 so‘m bilan qoladi.',
      'Qoida qo‘shilgach o‘tgan darslar avtomatik qayta hisoblanmaydi — kerak bo‘lsa «Tuzatish» orqali qo‘lda qo‘shing.',
    ],
  },
]
