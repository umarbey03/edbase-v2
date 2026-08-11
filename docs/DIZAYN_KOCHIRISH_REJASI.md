# Eski ilova dizaynini v2 ga ko'chirish — REJA

> **Sana:** 2026-07-30 · **Qaror egasi:** loyiha egasi · **Holat:** 1-to'lqin bajarilmoqda
>
> **Talab (loyiha egasi so'zi):** *"dizayn deyarli bir xil bo'lib ko'chmasa
> eski appni foydalanuvchilari juda qiynaladi tushunib olgani"*.
>
> Ya'ni qabul mezoni — **"eski ilova ruhida"** emas, **"deyarli bir xil"**.
> Eski tizimdan bugun haqiqiy o'quvchilar, ustozlar va o'quv bo'limi
> foydalanmoqda; ular yangi joylashuvni qaytadan o'rganishi kerak bo'lmasin.

---

## 🔴 2026-08-11: RANG QARORI TESKARISIGA O'ZGARDI — avval shuni o'qing

Loyiha egasi **iOS uslubidagi yorug', minimalistik** dizaynni so'radi (oq
kartochka, ochiq kul-ko'k sahifa foni, indigo `#4f4de8` aksent, pastel
nishonlar, yumshoq soyalar). Bajarildi: `frontend/src/style.css` yagona yorug'
palitraga o'tkazildi, kontrast auditi 100 juftlikda yashil.

**Shu hujjatning qaysi qismi KUCHINI YO'QOTDI:**

| Bo'lim | Holat |
|---|---|
| 3.1 tema tokenlari jadvali (navy + oltin, rol bo'yicha) | ❌ **eskirgan** — uchta tema bittaga yig'ildi |
| 7-bo'lim oxiridagi "1–2 to'lqin natijasi" rang jadvali | ❌ **eskirgan** |
| "oltin fonda `text-white` ishlatmang" ogohlantirishi | ❌ endi aksent indigo, `on-brand` hamma joyda oq |
| "kirish sahifasi yashil qoladi" qarori | ❌ u ham yorug' temaga o'tdi |

**KUCHDA QOLGANI (o'zgarmadi):** matnlar aynan · menyu va tab tartibi aynan ·
karkas (yon menyu, 390px Mini App, 5 tab) · `v-html` taqiqi · `100dvh` ·
shrift loyihada saqlanishi · kuratorda yashiriladigan bloklar.

Ya'ni foydalanuvchi "nima qayerda turishini" qayta o'rganmaydi — faqat
ko'rinish yangi. **Yagona ataylab qilingan tartib chekinishi:** o'quv bo'limi
guruh ichiga kirganda "O'quvchilar" tabi birinchi bo'ladi (ustoz/kuratorda
tegilmaydi) — bu ham loyiha egasining talabi.

Yangi rang tizimi va qarorlar: `docs/YANGI_TALABLAR_REJASI.md` 1-bo'limi.
Jonli dars xonasi ATAYLAB to'q qoladi (`[data-surface='stage']`).

---

## 1. XATONING ILDIZ SABABI (topildi)

Eski loyihada `app/static/app.css` — **bazaviy** uslub fayli, uning rangi
**yashil `#2f9e41`**. Lekin har bir HAQIQIY panel o'z HTML'i ichida shu rangni
**ustidan yozadi**:

| Yuza | Fon | Accent | Manba |
|---|---|---|---|
| O'quvchi | `#051e2d` | `#f5b731` (oltin) | `student.html` `:root` |
| Ustoz / kurator | `#092235` | `#ffcc33` (oltin) | `teacher.html` inline |
| O'quv bo'limi / admin | `#0f2d48` | `#f2c84b` (oltin) | `academic.html` inline |
| Jonli dars | `#051e2d` oilasi | `#ffcc33` (oltin) | `live.html` inline |
| **Kirish sahifasi** | `#070d09` | `#2f9e41` (yashil) | `app.css` — **ustidan yozilmagan** |

v2 rangni aynan **bazaviy fayldan** olgan (`style.css` izohi: `--accent #2f9e41
-> brand-500`) va uni HAMMA rolga tarqatgan. Ya'ni panellarda **hech bir
foydalanuvchi ko'rmaydigan rang** ishlatilgan. Farq shuning uchun faqat
o'quvchida emas — uchala panelda ham.

> Yagona joy — **kirish sahifasi** — eskisida ham yashil. Demak v2 dagi yashil
> login ekrani TO'G'RI va o'zgartirilmaydi.

---

## 2. MANBA VA USUL

Dizayn **koddan** ko'chiriladi — eski shablonlar o'z ichida to'liq
(HTML + CSS + JS bitta faylda), ya'ni aniq token, o'lcham, matn va holatlar
o'sha yerda:

```
app/templates/student.html    3 232 satr   — o'quvchi (Telegram Mini App)
app/templates/teacher.html    2 665 satr   — ustoz va kurator
app/templates/academic.html   6 434 satr   — o'quv bo'limi / admin
app/templates/live.html       1 897 satr   — jonli dars
app/templates/auth/*.html       201 satr   — kirish va Telegram
app/static/app.css              (bazaviy)
```

**Ekran suratlari nima uchun baribir kerak:** kod bo'sh holatni ham,
to'ldirilgan holatni ham bir xil aniq ko'rsatadi, lekin lokal bazada haqiqiy
ma'lumot yo'q — 30 o'quvchili guruh jadvali, to'la reyting, uzun chat qanday
ko'rinishini men ko'ra olmayman. Shuning uchun **6-bo'limdagi qisqa ro'yxat**
bo'yicha surat so'raladi; qolgani koddan olinadi.

---

## 3. TOKENLAR VA KARKAS

### 3.1. Tema tokenlari (rol bo'yicha)

Dizayn tizimi IKKIGA BO'LINMAYDI: `shared/ui` komponentlari bitta bo'lib
qoladi, faqat CSS o'zgaruvchilari almashadi (shell ildizida `data-theme`).

| Token | O'quvchi | Ustoz/kurator | O'quv bo'limi |
|---|---|---|---|
| `--bg` | `#051e2d` | `#092235` | `#0f2d48` |
| `--surface` | `rgba(8,44,64,.86)` | `#0e304b` | `#163a5a` |
| `--elevated` / `--hover` | `rgba(13,58,82,.92)` / `rgba(20,74,104,.96)` | `#154165` / `#1e527d` | `#1d4567` / `#245074` |
| `--border` | `rgba(245,183,49,.20)` | `#1a476b` | `#2b5276` |
| `--accent` | `#f5b731` | `#ffcc33` | `#f2c84b` |
| `--accent2` | `#fcd34d` | `#fbbf24` | `#f2c84b` |
| `--text` / `--muted` / `--dim` | `#fff` / `rgba(255,255,255,.62)` / `rgba(255,255,255,.35)` | `#fff` / `#a3c2db` / `#5c7f9c` | `#eaf2f9` / `#9fbad4` / `#5a7c9a` |
| `--green` / `--red` | `#22c55e` / `#ef4444` | `#10b981` / `#f43f5e` | `#22c55e` / `#ef4444` |
| radius | 16px | 14px oilasi | 14px oilasi |

Shrift: **Plus Jakarta Sans** (400–800). Google Fonts'ga tashqi so'rov
yubormaydigan yo'l tanlanadi (fayl loyihada saqlanadi) — CSP va oflayn uchun.

### 3.2. Karkas

| Yuza | Eski karkas | v2 da nima bo'ladi |
|---|---|---|
| O'quvchi | `max-width:520px`, pastda 5 tabli fixed panel, sticky appbar, pastdan chiquvchi modal | **Qayta quriladi** (hozir yon menyu) |
| Ustoz/kurator | `aside.sidebar` + guruh ichida 8 ta ichki tab | Karkas **to'g'ri**, ichki tablar yetishmaydi |
| O'quv bo'limi | `aside.sidebar`, kontent 1200px | Karkas **to'g'ri**, bo'limlar yetishmaydi |
| Jonli dars | to'liq ekran: `stage` + `chat` + `controls` | v2 da bor — parite tekshiriladi |
| Kirish | oddiy markazlashgan forma, yashil | **O'zgarmaydi** |

---

## 4. EKRAN INVENTARI

### 4.1. O'quvchi (Telegram Mini App)

| Eski bo'lim | v2 holati | Kerakli ish |
|---|---|---|
| **Bosh sahifa** — keyingi dars kartochkasi (jonli bo'lsa qizil, pulsatsiya), davomat doirasi | ❌ boshqacha | Frontend + davomat xulosasi endpointi |
| **Kalendar** — oy setkasi (YAK–SHAN), kun darslari | ❌ yo'q | Frontend + oy oralig'i endpointi |
| **O'quv** — kurs daraxti, gating, "Kurs hali biriktirilmagan" | qisman | Frontend (backend tayyor) |
| **Reyting** — leaderboard, "Guruh yo'q" | ❌ yo'q | **Backend + frontend** |
| **Chat** — chatlar ro'yxati, "Guruh yo'q" | ❌ yo'q | **Backend + frontend** |
| **Profil sheet** — avatar, telefon, iqtibos | ❌ yo'q | Frontend |
| Vazifalar / testlar | ✅ bor, joylashuvi boshqacha | Yangi karkasga joylash |

### 4.2. Ustoz va kurator

Menyu (`aside.sidebar`):

| Eski bo'lim | v2 holati | Kerakli ish |
|---|---|---|
| **Bosh sahifa** — bugungi va kelgusi darslar | ❌ yo'q | Frontend |
| **Guruhlarim** | ✅ bor | Rang + parite |
| **Chatlar** | ❌ yo'q | Backend (DM) + frontend |
| **Kuratorlik** | ❌ yo'q | Frontend (guruh bog'lanishi backendda bor) |
| **Savollar** (o'qilmagan belgisi bilan) | ❌ yo'q | Backend (DM) + frontend |

Guruh ichidagi **8 ta tab**: Darslar · O'quvchilar · Davomat · Baholar ·
Vazifalar · Testlar · Reyting · Chat.
v2 `TeacherGroupPage` da faqat **a'zolar va jadval** bor.

★ Kurator uchun eskisida **Testlar va Reyting tablari yashiriladi**
(`isCurator` sharti) — bu qoida ham ko'chiriladi.

### 4.3. O'quv bo'limi / admin

| Eski bo'lim | v2 holati | Kerakli ish |
|---|---|---|
| Guruhlar · Foydalanuvchilar · Testlar · Kurs quruvchi | ✅ bor | Rang + parite |
| **To'lovlar** | ❌ UI yo'q | Frontend (**backend bugun tayyor**) |
| **Moliya** (dashboard) | ❌ UI yo'q | Frontend + hisobot endpointlari |
| **Dars yozuvlari** | ❌ | Backend (FAZA 5.3) + frontend |
| **Qarorlar / Xabarlar** | ❌ | Backend (FAZA 5.2) + frontend |

### 4.4. Jonli dars

v2 da mavjud (`LiveRoomPage`, LiveKit + chat + qo'l ko'tarish). Parite
tekshiriladi: boshqaruv tugmalari joylashuvi, chat paneli, oltin `#ffcc33`.

---

## 5. YO'L-YO'LAKAY TUZATILADIGAN XATOLAR

Ko'chirish "nusxa" emas — eski koddagi nosozliklar takrorlanmaydi:

1. **`esc()` uch xil nusxada** (student/teacher/live) — v2 da Vue shabloni
   avtomatik escape qiladi, `v-html` taqiqlangan.
2. **324 KB bitta HTML fayl** — v2 da komponent va marshrut bo'yicha bo'linadi
   (kod bo'lish allaqachon ishlaydi).
3. **Qo'lda telefon kiritish oynasi** (eski X-1b zaifligi) — ko'chirilmaydi.
4. **`.layout{height:100vh}`** — telefonda brauzer paneli ustiga chiqib ketardi;
   `100dvh` + `safe-area-inset` ishlatiladi.
5. **Inline `onclick` va global funksiyalar** — Vue hodisalari bilan almashadi.
6. **Google Fonts tashqi so'rovi** — shrift loyihaga ko'chiriladi.
7. **Kurator uchun yashiriladigan bloklar** (`display:none` bilan) — v2 da
   rol shartida umuman render qilinmaydi (DOM'da qolmaydi).

---

## 6. QAYSI SURATLAR KERAK

Kod bo'sh holatlarni to'liq beradi. **To'ldirilgan** holatlar uchun 6 ta surat
foydali (lokal bazada bunday ma'lumot yo'q):

1. Ustoz → guruh ichi, **Davomat** tabi (belgilangan holatlar bilan)
2. Ustoz → guruh ichi, **Baholar** tabi
3. O'quvchi → **Reyting**, to'la ro'yxat bilan
4. O'quvchi → **Chat**, ochiq suhbat (xabarlar bilan)
5. O'quv bo'limi → **Moliya** dashboard
6. O'quv bo'limi → **To'lovlar** ro'yxati

Qolgan ekranlar koddan aynan ko'chiriladi.

---

## 7. BOSQICHLAR

| To'lqin | Ish | Holat |
|---|---|---|
| **1** | O'quvchi Mini App karkasi (520px, 5 tab, oltin tema) + Bosh sahifa/Kalendar/O'quv | ✅ tugadi |
| **1b** | Reyting va Chat backendi (`points_svc`, `dm_svc` ko'chirish) + kalendar/davomat endpointlari | ✅ tugadi |
| **1c** | Reyting va Chat EKRANLARI + davomat doirasini jonli ma'lumotga ulash | ✅ tugadi |
| **2** | Tema mexanizmini ustoz va o'quv bo'limiga qo'llash (navy + oltin) | ✅ tugadi |
| **2b** | Paritetni to'sib turgan backend bo'shliqlari | ✅ tugadi |
| **3** | Ustoz: Bosh sahifa, Kuratorlik, Chatlar/Savollar + guruh ichidagi 8 tab | 🔄 bajarilmoqda |
| **4** | O'quv bo'limi: To'lovlar va Moliya UI (backend tayyor) | 🔄 bajarilmoqda |
| **5** | Jonli dars pariteti (oltin `#ffcc33`) | ✅ tema qo'llandi |
| **6** | Dars yozuvlari va Qarorlar/Xabarlar (backend FAZA 5 dan keyin) | keyinroq |

### Mustaqil parite tekshiruvi (2026-07-30 kechqurun)

Alohida tekshiruvchi (kod yozmaydigan rol) 44 ta ekran surati va shablon
kodini solishtirib chiqdi.

**Tasdiqlangan:** tokenlar uchala rolda **bit-to-bit aynan**; o'quvchining
5 tabi nomi va tartibi aynan; 520px karkas (1100px ekranda o'lchandi); guruh
ichidagi 8 tab tartibi aynan; kuratorda Testlar/Reyting yashirin; bo'sh holat
matnlari aynan; **kontrast auditi 19 sahifada 0 ta muammo** topdi.

**Topilgan va DARHOL tuzatilgan:**

| Chekinish | Tuzatish |
|---|---|
| Jonli dars temasiz (yashil) qolgan | `teacher` temasi qo'llandi — eskisida ham AYNAN shu tokenlar edi |
| "Tekshirish" → "Vazifalar" qayta nomlangan | Eski nom qaytarildi |
| Kuratorda "Vazifalar" paydo bo'lgan | Olib tashlandi (eski `{% if role != 'assistant' %}`) |
| "Kurator guruhlari" | "Guruhlarim" ga qaytarildi |
| "Kurs kontenti" | "Kurs quruvchi" ga qaytarildi |
| O'quv bo'limi menyusi tartibi | Eski tartib tiklandi, bosh sahifa Guruhlar |
| Logo ostida hamma rolda bir xil matn | Rol yoziladi ("Ustoz paneli", "Admin", ...) |

**Qolgan chekinishlar** — funksional yo'qotishlar bo'lgani uchun backend ishi
talab qiladi; ro'yxat `docs/DAVOM_ETTIRISH.md` 3.1-bo'limida.

### Parite bahosi (tekshiruvchi)

| Rol | Baho |
|---|---|
| O'quvchi | ~90% |
| Kurator | ~80% |
| Ustoz | ~75% |
| O'quv bo'limi | ~60% |
| Jonli dars | ~40% → tema tuzatilgach yuqoriroq |

### 1–2 to'lqin natijasi (brauzerda tasdiqlangan)

| Rol | `data-theme` | Fon | Accent | Karkas |
|---|---|---|---|---|
| O'quvchi | `student` | `#051e2d` | `#f5b731` | 390px, pastda 5 tab |
| Ustoz/kurator | `teacher` | `#092235` | `#ffcc33` | yon menyu |
| O'quv bo'limi/admin | `manage` | `#0f2d48` | `#f2c84b` | yon menyu |

Tema `<html>` ga qo'yiladi — `BaseModal` va toast `Teleport to="body"` bilan
chiziladi va karkas `<div>` ida bo'lsa temadan chiqib ketardi (brauzerda
topilgan xato). Komponentlar nusxalanmadi: bitta `shared/ui`, uchta token
to'plami.

**Bitta komponent o'zgarishi:** `BaseButton` primary'da `text-white` edi —
oltin fonda kontrast ~1.9:1, o'qib bo'lmaydi. Rang `--color-on-brand`
tokeniga chiqarildi (xodimda oq, o'quvchida to'q ko'k).

### 2b to'lqin — paritetni to'sib turgan backend bo'shliqlari

UI ko'chirishda uch joyda "server bermaydi" degan cheklov chiqqan edi. Ular
yopildi (614 test yashil, mustaqil tasdiqlandi):

| Bo'shliq | Yechim | Frontendga ta'siri |
|---|---|---|
| Kalendarda o'tgan oylar bo'sh | `GET /live-sessions/calendar?from=&to=` (92 kun chegara, `localDate` maydoni) | Kalendar tabi to'liq ishlaydi |
| Kurs yo'lakchasida "tugatilgan" belgisi yo'q | `CourseLessonDto.completed` | Yashil belgi va ulagichlar |
| Davomatni qo'lda tuzatib bo'lmaydi | `GET/PUT /live-sessions/{id}/attendance[/{studentId}]` + `AttendanceAudits` | Ustozning "Davomat" tabi tahrirlanadigan bo'ldi |

★ **`localDate` maydoni muhim:** kunlar bo'yicha guruhlash AYNAN shu maydon
bo'yicha qilinadi. `scheduledStart` dan brauzerda sana chiqarilsa, chet
eldagi o'quvchida 20:00 dagi dars kechagi kunga tushib qolardi.

★ **`completed` qulflangan darsda doim `false`.** Gating qoidasi ochiqlikka
qaramaydi ("talab qoldimi?" degan sof savol) — xom qiymat berilganda
vazifasi yo'q kursning butun daraxti, qulflanganlari ham, yashil chiqardi.

★ **Davomat auditi `PaymentAudit` dan NUSXA EMAS.** To'lov auditi polimorf
(qiymatlar satr sifatida), davomatda esa o'zgaradigan narsa bitta va turi
aniq — satrga o'girilsa `"Present"` ni `"present"` dan ajratib bo'lmasdi.
Falsafa saqlandi: audit asosiy amal bilan bitta tranzaksiyada.

★ **Domain invarianti himoyalandi:** qo'lda tuzatish `FirstJoinAt` /
`LastJoinAt` / `DurationSeconds` ga TEGMAYDI. Aks holda o'quvchi xonada
turganda dars oxiridagi `Finalize` vaqtni ikki barobar qo'shardi (eski
tizimning B-5 xatosi). Ikkita test aynan shu stsenariyni qulflaydi.

---

## 8. QABUL MEZONI

Har to'lqin uchun:

1. **Yonma-yon solishtirish** — 390px kenglikdagi ekran surati eski ilova
   surati bilan yonma-yon qo'yiladi.
2. **Matnlar aynan** — bo'lim nomlari, bo'sh holat jumlalari, tugma yozuvlari
   o'zgartirilmaydi ("yaxshilash" ham o'zgartirish hisoblanadi).
3. **Tartib aynan** — menyu va tab ketma-ketligi eskisidek.
4. **Chekinish faqat ro'yxat bilan** — eski markupda haqiqiy xato bo'lsa yoki
   v2 ma'lumot shakli boshqacha bo'lsa, o'zboshimchalik bilan o'zgartirilmaydi:
   sababi bilan hisobotga yoziladi, qarorni loyiha egasi qabul qiladi.
5. `vue-tsc` va `eslint --max-warnings 0` toza; brauzerda tekshirilgan.
