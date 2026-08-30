"""
══════════════════════════════════════════════════════════════════════════
 og-cover.py — IJTIMOIY TARMOQ KARTOCHKASINI YASAYDI (public/og-cover.png)
══════════════════════════════════════════════════════════════════════════

Ishga tushirish (Docker orqali — mashinaga hech narsa o'rnatilmaydi):

    docker run --rm -v "$PWD/frontend:/w" -w /w python:3.12-slim sh -c \
      "pip install -q pillow fonttools brotli && python3 scripts/og-cover.py"

┌────────────────────────────────────────────────────────────────────────┐
│ 🔴 NEGA UMUMAN KERAK — VA NEGA QO'LDA YASALMAYDI                       │
└────────────────────────────────────────────────────────────────────────┘
2026-08-30 gacha `og:image` sifatida `logo-64.png` (64x64) turardi. U
Open Graph (min 200x200), Twitter `summary_large_image` (min 300x157) va
Google `Organization.logo` (min 112x112) talablarining HECH BIRIDAN
o'tmasdi — ya'ni havola ulashilganda kartochka RASMSIZ chiqardi.

Rasmni Photoshop'da yasab, `public/` ga tashlab qo'yish ham mumkin edi.
LEKIN unda brend rangi yoki kurs raqami o'zgarganda rasm ESKI holicha
qolardi va buni hech kim sezmasdi — bu fayl kod ko'rikidan o'tmaydi.
Skript esa palitrani va shriftni loyihaning O'ZIDAN oladi.

⚠️ BUILD'GA ULANMAGAN — ATAYLAB. Kartochka brend o'zgargandagina
   qayta yasaladi (yiliga bir-ikki marta). Uni `npm run build` ga ulash
   har bir deploy'ga Python + shrift kutubxonalarini olib kirardi —
   bitta statik rasm uchun juda qimmat.

Raqamlar manbai: `src/shared/config/course-facts.ts`. O'sha fayl
o'zgarsa, pastdagi FACTS ni ham yangilang.
"""

from __future__ import annotations

import io
from pathlib import Path

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
PUBLIC = ROOT / "public"

# Open Graph uchun tavsiya etilgan o'lcham. 1.91:1 — Telegram, Facebook va
# Twitter uchastkalari aynan shu nisbatni kesmasdan ko'rsatadi.
W, H = 1200, 630

# --- Palitra: `src/style.css` dagi zumrad shkaladan (o'zgartirmang) ------
BRAND_800 = (0x03, 0x3A, 0x26)   # --color-brand-800, gradient boshi
BRAND_500 = (0x00, 0x78, 0x4F)   # --color-brand-500, AKSENT
PAPER = (0xFB, 0xFA, 0xF8)       # --color-ink-950, "qog'oz" — matn rangi

# Raqamlar `course-facts.ts` dan ko'chirilgan.
FACTS = ["8 oy", "haftasiga 5 kun", "18-20 kishilik guruh"]

HEADLINE = "Online arab tili kursi"
SUBLINE = "8 oyda arab tilidagi harakatli kitobni mustaqil o‘qiysiz"
BRAND = "ZIN-NUR ONLINE"
SITE = "zinnuronline.uz"


def load_font(woff2: Path, size: int, weight: int | None = None) -> ImageFont.FreeTypeFont:
    """woff2 ni TTF ga ochib, PIL uchun shrift qaytaradi.

    PIL woff2 ni O'QIY OLMAYDI — shuning uchun fontTools bilan xotirada
    TTF ga aylantiramiz. Diskka vaqtinchalik fayl yozilmaydi.
    """
    buf = io.BytesIO()
    TTFont(str(woff2)).save(buf)
    buf.seek(0)

    font = ImageFont.truetype(buf, size=size)

    # Onest — o'zgaruvchan (variable) shrift: qalinlik o'qi 100..900.
    # Sarlavha uchun qalin, izoh uchun oddiy qalinlik kerak.
    if weight is not None:
        try:
            font.set_variation_by_axes([float(weight)])
        except OSError:
            # FreeType variation'siz yig'ilgan bo'lsa — 400 bilan qolaveradi.
            # Kartochka baribir yasaladi, faqat sarlavha ingichkaroq chiqadi.
            pass

    return font


def gradient(size: tuple[int, int], top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    """Diagonalga yaqin vertikal gradient.

    Sof tekis fon "yasalgan" ko'rinadi; gradient chuqurlik beradi va
    logotipning o'z gradienti bilan bir oilaga tushadi.
    """
    w, h = size
    base = Image.new("RGB", (1, h))
    px = base.load()

    for y in range(h):
        t = y / (h - 1)
        px[0, y] = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))

    return base.resize((w, h), Image.Resampling.BICUBIC)


def rounded(img: Image.Image, radius: int) -> Image.Image:
    """Rasm burchaklarini yumaloqlaydi (logotip plitasi uchun)."""
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([(0, 0), (img.size[0] - 1, img.size[1] - 1)], radius, fill=255)
    out = img.convert("RGBA")
    out.putalpha(mask)
    return out


def main() -> None:
    fonts_dir = PUBLIC / "fonts"
    onest = fonts_dir / "onest-latin.woff2"

    f_head = load_font(onest, 76, 700)
    f_sub = load_font(onest, 33, 400)
    f_brand = load_font(onest, 27, 600)
    f_fact = load_font(onest, 25, 500)
    f_site = load_font(onest, 25, 500)

    card = gradient((W, H), BRAND_800, BRAND_500).convert("RGBA")

    # Yumshoq yorug'lik dog'i — o'ng yuqoridan. Gradientning "yassi"
    # ko'rinishini sindiradi.
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([(W - 620, -320), (W + 180, 380)], fill=(0, 163, 108, 70))
    card = Image.alpha_composite(card, glow)

    # --- Logotip plitasi --------------------------------------------------
    logo = Image.open(PUBLIC / "apple-touch-icon.png").convert("RGBA")
    logo = logo.resize((104, 104), Image.Resampling.LANCZOS)
    card.alpha_composite(rounded(logo, 26), (80, 74))

    '''
    🔴 SHAFFOF ELEMENTLAR ALOHIDA QATLAMGA CHIZILADI.

    `ImageDraw` RGBA rasmga chizganda rangni ARALASHTIRMAYDI — u pikselni
    (alfa qiymati bilan birga) BUTUNLAY ALMASHTIRADI. Ya'ni alfasi 30 ga
    teng "yengil" tabletka fon ustida shaffof emas, balki TO'LIQ oq
    bo'lib chiqadi va ichidagi matnni yopib qo'yadi.

    (Aynan shu xato birinchi urinishda qilindi: faktlar qatori uchta oq
     to'rtburchak bo'lib chiqdi, matn esa umuman ko'rinmadi.)

    Yechim — hamma yarim shaffof narsani bo'sh qatlamga chizib, keyin
    `alpha_composite` bilan qo'yish: o'shanda alfa haqiqatan aralashadi.
    '''
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    draw.text((208, 112), BRAND, font=f_brand, fill=PAPER + (235,))

    # --- Sarlavha va izoh -------------------------------------------------
    draw.text((80, 244), HEADLINE, font=f_head, fill=PAPER + (255,))
    draw.text((80, 344), SUBLINE, font=f_sub, fill=PAPER + (205,))

    # --- Pastdagi faktlar qatori -----------------------------------------
    x = 80
    y = 462
    for fact in FACTS:
        tw = draw.textlength(fact, font=f_fact)
        draw.rounded_rectangle([(x, y), (x + tw + 44, y + 54)], 27, fill=PAPER + (36,))
        draw.text((x + 22, y + 14), fact, font=f_fact, fill=PAPER + (240,))
        x += tw + 44 + 14

    # --- Pastki chiziq va domen ------------------------------------------
    draw.line([(80, H - 82), (W - 80, H - 82)], fill=PAPER + (55,), width=1)
    site_w = draw.textlength(SITE, font=f_site)
    draw.text((W - 80 - site_w, H - 60), SITE, font=f_site, fill=PAPER + (185,))

    card = Image.alpha_composite(card, layer)

    out = PUBLIC / "og-cover.png"
    # `optimize=True` — hajm muhim: kartochkani har ulashishda bot yuklaydi.
    card.convert("RGB").save(out, "PNG", optimize=True)
    print(f"og-cover: {out} tayyor ({out.stat().st_size / 1024:.1f} KB, {W}x{H})")


if __name__ == "__main__":
    main()
