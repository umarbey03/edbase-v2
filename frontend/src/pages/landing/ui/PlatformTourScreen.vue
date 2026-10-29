<script setup lang="ts">
import { MascotAvatar } from '@/features/mascot'
import { AppIcon } from '@/shared/ui'

import type { TourScreen } from '../model/content'

/*
  ══════════════════════════════════════════════════════════════════════════
   KABINET EKRANLARINING HTML-SXEMASI — 2026-10-29
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA BU: o'quvchi kabinetining (`pages/student/*`) soddalashtirilgan
    nusxasi — telefon ramkasi ichida «Platforma ichidan» karuselida
    chiziladi. Haqiqiy komponentlar ISHLATILMAYDI: ular API so'raydi,
    login talab qiladi va landing to'plamini og'irlashtirardi.

  🔴 MA'LUMOT O'YLAB TOPILGAN. Ismlar — faqat ism va familiyaning bosh
     harfi («Aziza D.»), telefon, ID va summa yo'q. Arabcha misollar —
     betaraf maishiy so'zlar (كِتَاب, قَلَم, بَاب).

  ★ MATN SHU YERDA, `content.ts` DA EMAS — ATAYLAB: bu sotuv matni emas,
    rasmning bir qismi (demo ma'lumot). Haqiqiy skrinshot kelganda butun
    sxema o'chadi: slaydga `image` beriladi va `PlatformTour` bu
    komponentni chizmaydi (`content.ts` dagi `PLATFORM_TOUR` izohi).

  ★ `aria-hidden` — ekran mazmuni slaydning `alt` matnida aytiladi;
    sxemadagi mayda yozuvlarni ekran o'qigich o'qisa, faqat shovqin.
*/
defineProps<{ screen: TourScreen }>()

/** Ovozli yozuv to'lqini — tasodifiy emas, qat'iy (SSR bilan bir xil chiqsin). */
const WAVE = Array.from({ length: 22 }, (_, i) => 6 + ((i * 37) % 22))

/** O'quv yo'li — ilon izi: chapga/o'ngga siljish (px) va holat. */
const PATH = [
  { x: -40, state: 'done', label: '5' },
  { x: 20, state: 'done', label: '6' },
  { x: 56, state: 'now', label: '7' },
  { x: 20, state: 'lock', label: '8' },
  { x: -40, state: 'lock', label: '9' },
  { x: -64, state: 'lock', label: '10' },
] as const
</script>

<template>
  <div
    class="tour-screen relative h-full overflow-hidden bg-ink-950 text-[12px] text-slate-200"
    aria-hidden="true"
  >
    <!-- ═══════════════════════════════════════════ BOSH SAHIFA ═══ -->
    <template v-if="screen === 'home'">
      <div class="scr-top">
        <b class="font-display text-lg text-slate-50">Salom, Aziza!</b>
        <span class="scr-ava">AD</span>
      </div>
      <div class="grid gap-2.5 px-3.5">
        <div class="mcard flex items-center gap-2.5 border-amber-800! bg-amber-950!">
          <MascotAvatar
            mood="happy"
            :size="40"
          />
          <div>
            <p class="text-[12.5px] font-bold text-slate-50">
              Zo‘r ketyapsiz!
            </p>
            <p class="scr-muted">
              Ketma-ket 3 dars — davom etamiz.
            </p>
          </div>
        </div>
        <div class="mcard">
          <p class="scr-muted">
            Bugungi dars
          </p>
          <p class="mt-0.5 text-[12.5px] font-bold text-slate-50">
            2-modul · 14-dars
          </p>
          <p class="scr-muted">
            Video · 18 daqiqa · vazifa va test
          </p>
          <span class="scr-btn">Davom etish</span>
        </div>
        <div class="mcard flex items-center gap-3">
          <span class="scr-ring">
            <i>64%</i>
          </span>
          <div>
            <p class="text-[12.5px] font-bold text-slate-50">
              Kurs progressi
            </p>
            <p class="scr-muted">
              1-modul: 22 / 34 dars
            </p>
            <p class="mt-1.5 text-[12.5px] font-bold text-slate-50">
              320 ball · 4-o‘rin
            </p>
          </div>
        </div>
      </div>
      <div class="scr-tabbar">
        <span class="on" /><span /><span /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════ O'QUV YO'LI ═══ -->
    <template v-else-if="screen === 'path'">
      <div class="scr-top">
        <b class="font-display text-lg text-slate-50">O‘quv yo‘li</b>
        <span class="rounded-full bg-green-950 px-2 py-0.5 text-[10.5px] font-bold text-brand-500">1-modul</span>
      </div>
      <div class="grid justify-items-center gap-3.5 pb-16 pt-2">
        <span
          v-for="node in PATH"
          :key="node.label"
          class="path-node"
          :class="`path-node--${node.state}`"
          :style="{ marginLeft: `${node.x}px` }"
        >
          <AppIcon
            v-if="node.state === 'done'"
            name="check"
            :size="20"
          />
          <AppIcon
            v-else-if="node.state === 'lock'"
            name="lock"
            :size="16"
          />
          <template v-else>{{ node.label }}</template>
        </span>
      </div>
      <div class="scr-tabbar">
        <span /><span class="on" /><span /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════ DARS SAHIFASI ═══ -->
    <template v-else-if="screen === 'lesson'">
      <div class="pt-9" />
      <div class="scr-video">
        <span
          class="scr-ar absolute right-4 top-2 text-[40px] text-white/25"
          lang="ar"
        >ع غ</span>
        <span class="grid size-11 place-items-center rounded-full bg-brand-500 text-on-brand ring-[6px] ring-white/15">
          <AppIcon
            name="play"
            :size="18"
          />
        </span>
      </div>
      <div class="grid gap-2.5 px-3.5 pt-3">
        <div>
          <p class="text-[14px] font-bold text-slate-50">
            7-dars: «Ayn» va «G‘ayn»
          </p>
          <p class="scr-muted">
            Video · 3 qism · 18 daqiqa
          </p>
        </div>
        <div class="flex gap-1.5">
          <span class="scr-seg scr-seg--on">Vazifa</span>
          <span class="scr-seg">Test</span>
          <span class="scr-seg">Savol-javob</span>
        </div>
        <div class="mcard">
          <p class="text-[12.5px] font-bold text-slate-50">
            Matnni o‘qing va ovozli yozuv yuboring
          </p>
          <p class="scr-muted">
            Muddat: ertaga 23:59
          </p>
          <span class="scr-btn">Topshirish</span>
        </div>
      </div>
      <div class="scr-tabbar">
        <span /><span class="on" /><span /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════ VAZIFALAR ═══ -->
    <template v-else-if="screen === 'tasks'">
      <div class="scr-top">
        <b class="font-display text-lg text-slate-50">Vazifalarim</b>
        <span class="scr-ava">AD</span>
      </div>
      <div class="grid gap-2.5 px-3.5">
        <div class="mcard">
          <p class="text-[12.5px] font-bold text-slate-50">
            Ovozli o‘qish: 6-dars matni
          </p>
          <div class="my-1.5 flex h-7 items-center gap-[3px]">
            <i
              v-for="(height, index) in WAVE"
              :key="index"
              class="w-1 rounded-full bg-brand-500"
              :style="{ height: `${height}px` }"
            />
          </div>
          <p class="scr-muted">
            0:42 · yuborildi
          </p>
        </div>
        <div class="mcard border-green-800! bg-green-950!">
          <p class="text-[12.5px] font-bold text-brand-500">
            Tekshirildi · 9/10
          </p>
          <p class="scr-muted mt-0.5">
            Izoh: «Talaffuz yaxshi. <span
              class="scr-ar text-[13px]"
              lang="ar"
            >ح</span> ni yumshoqroq ayting.»
          </p>
        </div>
        <div class="mcard">
          <p class="text-[12.5px] font-bold text-slate-50">
            Daftar surati: harflar yozuvi
          </p>
          <p class="scr-muted">
            Kutilmoqda · ertaga
          </p>
        </div>
      </div>
      <div class="scr-tabbar">
        <span /><span class="on" /><span /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════════ TEST ═══ -->
    <template v-else-if="screen === 'test'">
      <div class="scr-top">
        <b class="font-display text-lg text-slate-50">Test</b>
        <span class="scr-muted tabular-nums">3 / 10</span>
      </div>
      <div class="grid gap-2.5 px-3.5">
        <div class="h-1.5 overflow-hidden rounded-full bg-green-900">
          <i class="block h-full w-[30%] rounded-full bg-brand-500" />
        </div>
        <p class="mt-1 text-[14px] font-bold text-slate-50">
          Qaysi so‘z «kitob» degani?
        </p>
        <div
          v-for="option in [
            { key: 'A', word: 'قَلَم', ok: false },
            { key: 'B', word: 'كِتَاب', ok: true },
            { key: 'C', word: 'بَاب', ok: false },
          ]"
          :key="option.key"
          class="flex items-center gap-2 rounded-xl border px-2.5 py-1.5 font-semibold"
          :class="option.ok
            ? 'border-green-400 bg-green-950 text-green-100'
            : 'border-line bg-ink-900 text-slate-300'"
        >
          <b
            class="grid size-5 place-items-center rounded-md text-[10px]"
            :class="option.ok ? 'bg-green-400 text-white' : 'bg-ink-800 text-slate-400'"
          >{{ option.key }}</b>
          <span
            class="scr-ar text-[20px] leading-tight"
            lang="ar"
            dir="rtl"
          >{{ option.word }}</span>
        </div>
        <div class="mcard text-center">
          <p class="text-[12.5px] font-bold text-slate-50">
            Natija darhol
          </p>
          <p class="scr-muted">
            Xato javoblar izohi bilan ko‘rsatiladi
          </p>
        </div>
      </div>
      <div class="scr-tabbar">
        <span /><span class="on" /><span /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════════ REYTING ═══ -->
    <template v-else-if="screen === 'rating'">
      <div class="scr-top">
        <b class="font-display text-lg text-slate-50">Reyting</b>
        <span class="rounded-full bg-ink-800 px-2 py-0.5 text-[10.5px] font-bold text-slate-300">Noyabr</span>
      </div>
      <div class="grid gap-2.5 px-3.5">
        <div class="flex items-end justify-center gap-2 pt-1">
          <div
            v-for="pod in [
              { who: 'MR', place: 2, height: 46 },
              { who: 'SN', place: 1, height: 64 },
              { who: 'JT', place: 3, height: 34 },
            ]"
            :key="pod.who"
            class="w-[4.25rem] text-center"
          >
            <AppIcon
              v-if="pod.place === 1"
              class="mx-auto mb-0.5 text-amber-500"
              name="trophy"
              :size="16"
            />
            <span
              class="mx-auto mb-1.5 grid size-10 place-items-center rounded-full font-extrabold"
              :class="pod.place === 1 ? 'pod-gold' : 'bg-green-400 text-white'"
            >{{ pod.who }}</span>
            <span
              class="pod-block grid place-items-center rounded-t-xl font-extrabold text-white"
              :style="{ height: `${pod.height}px` }"
            >{{ pod.place }}</span>
          </div>
        </div>
        <div class="mcard px-2.5! py-1.5!">
          <div
            v-for="row in [
              { n: 4, who: 'AD', name: 'Aziza D. (siz)', points: 320, me: true },
              { n: 5, who: 'BK', name: 'Bekzod K.', points: 305, me: false },
              { n: 6, who: 'LM', name: 'Lola M.', points: 290, me: false },
            ]"
            :key="row.n"
            class="flex items-center gap-2 border-b border-dashed border-line py-1.5 last:border-b-0"
            :class="row.me ? 'rounded-lg border-b-0! bg-green-950 px-1.5' : ''"
          >
            <span class="w-4 font-extrabold text-slate-500">{{ row.n }}</span>
            <span class="grid size-6 place-items-center rounded-full bg-ink-800 text-[10px] font-extrabold">{{ row.who }}</span>
            <span class="font-semibold">{{ row.name }}</span>
            <span class="ml-auto font-extrabold text-brand-500">{{ row.points }}</span>
          </div>
        </div>
      </div>
      <div class="scr-tabbar">
        <span /><span /><span class="on" /><span /><span />
      </div>
    </template>

    <!-- ═══════════════════════════════════════════════ JONLI DARS ═══ -->
    <template v-else>
      <div class="live-screen">
        <span class="live-badge">
          <i
            class="announce-dot-lite"
            aria-hidden="true"
          />JONLI
        </span>
        <div class="grid flex-1 grid-cols-2 gap-1.5">
          <div class="live-tile live-tile--teacher col-span-2">
            Ustoz
            <small>Asosiy ustoz</small>
          </div>
          <div
            v-for="tile in [
              { who: 'AD', name: 'Aziza D.' },
              { who: 'BK', name: 'Bekzod K.' },
              { who: 'LM', name: 'Lola M.' },
              { who: '+15', name: 'guruh' },
            ]"
            :key="tile.who"
            class="live-tile"
          >
            {{ tile.who }}
            <small>{{ tile.name }}</small>
          </div>
        </div>
        <div class="flex justify-center gap-2.5">
          <span class="live-ctrl"><AppIcon
            name="mic"
            :size="16"
          /></span>
          <span class="live-ctrl"><AppIcon
            name="camera"
            :size="16"
          /></span>
          <span class="live-ctrl"><AppIcon
            name="hand"
            :size="16"
          /></span>
          <span class="live-ctrl live-ctrl--end"><AppIcon
            name="leave"
            :size="16"
          /></span>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.scr-ar {
  font-family: var(--font-arabic);
  unicode-bidi: isolate;
}

.scr-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 2.75rem 1rem 0.65rem;
}

.scr-ava {
  display: grid;
  place-items: center;
  width: 1.9rem;
  height: 1.9rem;
  border-radius: 50%;
  background: var(--color-green-900);
  color: var(--color-green-400);
  font-size: 11px;
  font-weight: 800;
}

.mcard {
  border: 1px solid var(--color-line);
  border-radius: 1rem;
  background: var(--color-ink-900);
  padding: 0.75rem;
}

.scr-muted {
  color: var(--color-slate-400);
  font-size: 11px;
}

.scr-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 2rem;
  margin-top: 0.5rem;
  border-radius: 0.65rem;
  background: var(--color-brand-500);
  color: var(--color-on-brand);
  font-size: 11.5px;
  font-weight: 700;
}

.scr-ring {
  display: grid;
  flex: none;
  place-items: center;
  width: 4rem;
  height: 4rem;
  border-radius: 50%;
  background: conic-gradient(var(--color-brand-500) 0 64%, var(--color-ink-800) 64% 100%);
}

.scr-ring i {
  display: grid;
  place-items: center;
  width: 3.1rem;
  height: 3.1rem;
  border-radius: 50%;
  background: var(--color-ink-900);
  color: var(--color-slate-50);
  font-size: 13px;
  font-style: normal;
  font-weight: 800;
}

.scr-tabbar {
  position: absolute;
  inset: auto 0 0;
  display: flex;
  align-items: center;
  justify-content: space-around;
  height: 3.6rem;
  padding-bottom: 0.4rem;
  border-top: 1px solid var(--color-line);
  background: var(--color-ink-900);
}

.scr-tabbar span {
  width: 1.4rem;
  height: 1.4rem;
  border-radius: 0.45rem;
  background: var(--color-ink-800);
}

.scr-tabbar span.on {
  background: var(--color-brand-500);
}

/* O'quv yo'li tugunlari — `CourseLessonPath` uslubida (soya — "tugma"). */
.path-node {
  display: grid;
  place-items: center;
  width: 3.25rem;
  height: 3.25rem;
  border-radius: 50%;
  font-size: 15px;
  font-weight: 800;
}

.path-node--done {
  background: var(--color-brand-500);
  color: var(--color-on-brand);
  box-shadow: 0 5px 0 var(--color-green-100);
}

/* 🔴 Oltin — Nuri palitrasi (logotip kabi qat'iy qiymat, token emas). */
.path-node--now {
  background: #efbd4e;
  color: #5a3a06;
  box-shadow: 0 5px 0 #cf972b, 0 0 0 8px rgb(239 189 78 / 0.25);
  animation: path-pulse 2s ease-in-out infinite;
}

.path-node--lock {
  background: var(--color-ink-750);
  color: var(--color-slate-500);
  box-shadow: 0 5px 0 var(--color-line-strong);
}

@keyframes path-pulse {
  50% {
    box-shadow: 0 5px 0 #cf972b, 0 0 0 14px rgb(239 189 78 / 0.12);
  }
}

.scr-video {
  position: relative;
  display: grid;
  place-items: center;
  height: 9.5rem;
  background: linear-gradient(135deg, #0d1310, #05432a);
}

.scr-seg {
  flex: 1;
  padding: 0.45rem 0;
  border-radius: 0.65rem;
  background: var(--color-ink-800);
  color: var(--color-slate-400);
  font-size: 11px;
  font-weight: 700;
  text-align: center;
}

.scr-seg--on {
  background: var(--color-green-950);
  color: var(--color-brand-500);
}

.pod-gold {
  background: #efbd4e;
  color: #5a3a06;
  box-shadow: 0 0 0 3px #fff, 0 0 0 5px #efbd4e;
}

.pod-block {
  background: linear-gradient(var(--color-green-500), var(--color-green-100));
}

/*
  Jonli dars xonasi — ilovadagidek TO'Q (LiveRoomPage). Ranglar qat'iy:
  xona yorug' landing ichida ham to'q bo'lib qolishi kerak.
*/
.live-screen {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  height: 100%;
  padding: 2.75rem 0.65rem 0.75rem;
  background: #0d1310;
  color: #fff;
}

.live-badge {
  position: absolute;
  top: 3rem;
  left: 1.1rem;
  z-index: 1;
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.1rem 0.45rem;
  border-radius: 0.4rem;
  background: #d92d20;
  font-size: 10px;
  font-weight: 800;
}

.announce-dot-lite {
  width: 0.35rem;
  height: 0.35rem;
  border-radius: 50%;
  background: #fff;
}

.live-tile {
  position: relative;
  display: grid;
  place-items: center;
  border-radius: 0.75rem;
  background: #1f2a24;
  color: #abefc6;
  font-weight: 800;
}

.live-tile--teacher {
  background: linear-gradient(135deg, #05603a, #022a1a);
  font-size: 13px;
}

.live-tile small {
  position: absolute;
  bottom: 0.35rem;
  left: 0.5rem;
  color: #cfd8d2;
  font-size: 9.5px;
  font-weight: 600;
}

.live-ctrl {
  display: grid;
  place-items: center;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 50%;
  background: #26302a;
  color: #cfd8d2;
}

.live-ctrl--end {
  width: 3.25rem;
  border-radius: 1.1rem;
  background: #d92d20;
  color: #fff;
}

@media (prefers-reduced-motion: reduce) {
  .path-node--now {
    animation: none;
  }
}
</style>
