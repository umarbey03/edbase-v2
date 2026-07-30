/**
 * ZIN-NUR — SignalR yuklama testi.
 *
 * MAQSAD: "200 bir vaqtdagi foydalanuvchi qotmasdan ishlaydi" degan asosiy
 * da'voni AMALDA tekshirish. Bu butun arxitekturaning mavjudlik sababi —
 * shuning uchun taxminga tayanib bo'lmaydi.
 *
 * NIMA O'LCHANADI
 *   - ulanish vaqti (p50/p95/p99)
 *   - JoinSession javob vaqti
 *   - chat xabarining OXIRIGACHA yetib borish kechikishi (end-to-end)
 *   - xatolar va uzilishlar
 *
 * NIMA O'LCHANMAYDI (ataylab)
 *   - LiveKit media oqimi. Video backend'dan O'TMAYDI — u to'g'ridan-to'g'ri
 *     brauzer ↔ LiveKit orasida ketadi. Media yuklamasini o'lchash uchun
 *     alohida vosita kerak (livekit-cli load-test).
 *
 * HAR KLIENT — O'Z FOYDALANUVCHISI
 *   Server rate-limit'ni ham, presence'ni ham FOYDALANUVCHI bo'yicha
 *   kalitlaydi. Bitta tokenni ulashish o'lchovni buzadi (batafsil: seed.mjs).
 *   Shuning uchun skript kerakli sonda o'quvchi tayyorlaydi (idempotent) va
 *   har biri o'z tokeni bilan ulanadi.
 *
 * ISHLATISH
 *   node tests/load/signalr-load.mjs                 # 200 klient (default)
 *   USERS=50 node tests/load/signalr-load.mjs        # 50 klient
 *   USERS=200 DURATION=120 node tests/load/signalr-load.mjs
 *   SESSION_ID=282 GROUP_ID=4 node tests/load/signalr-load.mjs
 *
 * TALAB: `frontend/node_modules` o'rnatilgan bo'lishi kerak (@microsoft/signalr).
 * Skript uni avtomatik topadi.
 */
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { pickTarget, ensureUsers, issueTokens } from './seed.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '../../frontend/package.json'));
const signalR = require('@microsoft/signalr');

// ---------------------------------------------------------------- sozlamalar
const API = process.env.API_URL ?? 'http://localhost:5080';
const USERS = Number(process.env.USERS ?? 200);
const DURATION_SEC = Number(process.env.DURATION ?? 60);
const EMAIL = process.env.EMAIL ?? 'admin@zinnur.uz';
const PASSWORD = process.env.PASSWORD ?? 'Admin!2345';

/** Har klient necha sekundda bir xabar yozadi.
 *  Server chegarasi 1 xabar / 2 sek — undan tezroq yuborsak 429 olamiz.
 *  Real darsda 200 kishidan bir vaqtda 5-10 tasi yozadi, shuning uchun
 *  har klient uchun 20 sekund realistik. */
const MSG_INTERVAL_MS = Number(process.env.MSG_INTERVAL ?? 20_000);

/** Ulanishlarni PILLAPOYA bilan ochamiz — 200 tasi bir zumda ochilsa
 *  bu real stsenariy emas (dars boshlanishida ham odamlar 10-30 sekund
 *  ichida kiradi) va lokal port limitiga urilamiz. */
const RAMP_MS = Number(process.env.RAMP ?? 20_000);

// ---------------------------------------------------------------- statistika
const stats = {
  connectMs: [],
  joinMs: [],
  chatLatencyMs: [],
  connected: 0,
  failed: 0,
  disconnects: 0,
  messagesSent: 0,
  messagesReceived: 0,
  rateLimited: 0,

  /** `JoinSession` javobida ko'rilgan eng katta ishtirokchi soni.
   *  Bu 200 ta ALOHIDA foydalanuvchi haqiqatan bir xonada bo'lganining
   *  ISBOTI — bitta token bilan bu son 1 bo'lib qolardi. */
  presenceMax: 0,

  errors: new Map(),
};

const recordError = (e) => {
  const key = String(e?.message ?? e).slice(0, 90);
  stats.errors.set(key, (stats.errors.get(key) ?? 0) + 1);
};

const pct = (arr, p) => {
  if (!arr.length) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  return Math.round(sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))]);
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------------------------------------------------------- tayyorgarlik
// BITTA admin kirishi — o'quvchi tokenlari esa lokal imzolanadi (seed.mjs).
// Bitta so'rov `auth` rate-limit budjetiga (20/daqiqa) bemalol sig'adi.
async function loginAdmin() {
  const r = await fetch(`${API}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD }),
  });
  if (!r.ok) throw new Error(`login ${r.status}: ${await r.text()}`);
  return r.json();
}

// ---------------------------------------------------------------- bitta klient
async function runClient(index, token, sessionId, stopAt) {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${API}/hubs/live?access_token=${token}`, {
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets,
    })
    .configureLogging(signalR.LogLevel.None)
    .withAutomaticReconnect()
    .build();

  // Chat kechikishini o'lchash: xabar matniga vaqt muhrini yozamiz
  conn.on('ChatMessage', (m) => {
    stats.messagesReceived++;
    const stamp = /\|t=(\d+)\|/.exec(m.body ?? '');
    if (stamp) stats.chatLatencyMs.push(Date.now() - Number(stamp[1]));
  });
  conn.on('PresenceChanged', () => {});
  conn.on('HandRaised', () => {});

  // ★ ATAYLAB YOPISHNI UZILISHDAN AJRATAMIZ.
  //
  // `onclose` NORMAL `conn.stop()` da ham chaqiriladi. Bayroqsiz har klient
  // test oxirida o'zini "uzilgan" deb sanardi va `disconnects` DOIMO
  // `USERS` ga teng bo'lardi — ya'ni quyidagi baho sharti
  // (`disconnects > USERS * 0.05`) HAR YUGURTIRISHDA yiqilardi va test
  // hech qachon "muvaffaqiyatli" deb chiqmasdi. Amalda tekshirildi:
  // 5 klient, 0 xato, natija "5 ta kutilmagan uzilish".
  let closing = false;
  conn.onclose(() => { if (!closing) stats.disconnects++; });

  try {
    const t0 = Date.now();
    await conn.start();
    stats.connectMs.push(Date.now() - t0);
    stats.connected++;

    const t1 = Date.now();
    const joined = await conn.invoke('JoinSession', sessionId);
    stats.joinMs.push(Date.now() - t1);

    // Xonadagi ALOHIDA foydalanuvchilar soni (presence Redis'dan).
    if (joined?.count > stats.presenceMax) stats.presenceMax = joined.count;
  } catch (e) {
    stats.failed++;
    recordError(e);
    closing = true;
    try { await conn.stop(); } catch { /* ahamiyatsiz */ }
    return;
  }

  // Xabar yuborish sikli — har klient o'z fazasida (bir vaqtda portlamasin)
  await sleep(Math.random() * MSG_INTERVAL_MS);

  while (Date.now() < stopAt) {
    try {
      await conn.invoke('SendMessage', sessionId,
        `yuklama-${index}|t=${Date.now()}|`);
      stats.messagesSent++;
    } catch (e) {
      if (/tez|rate|kuting/i.test(String(e?.message))) stats.rateLimited++;
      else recordError(e);
    }
    await sleep(MSG_INTERVAL_MS + Math.random() * 2000);
  }

  closing = true;

  try {
    await conn.invoke('LeaveSession', sessionId);
    await conn.stop();
  } catch { /* tugatishda xato ahamiyatsiz */ }
}

// ---------------------------------------------------------------- asosiy
const main = async () => {
  console.log(`
╔══════════════════════════════════════════════════════╗
║  ZIN-NUR — SignalR yuklama testi                     ║
╚══════════════════════════════════════════════════════╝
  API           : ${API}
  Klientlar     : ${USERS}
  Davomiylik    : ${DURATION_SEC} sekund
  Ramp-up       : ${RAMP_MS / 1000} sekund
  Xabar oralig'i: ${MSG_INTERVAL_MS / 1000} sekund/klient
`);

  const auth = await loginAdmin();
  const target = await pickTarget(API, auth.accessToken);

  console.log(`  Dars #${target.sessionId} · guruh #${target.groupId} · ${target.title}`);
  console.log('  o\'quvchilar tayyorlanmoqda...');

  const { users, created, added } = await ensureUsers(
    API, auth.accessToken, USERS, target.groupId,
    (what, done, total) => process.stdout.write(`    ${what}: ${done}/${total}   \r`),
  );

  console.log(`    yaratildi: ${created} · a'zo qilindi: ${added} · jami: ${users.length}   `);

  // Tokenlar LOKAL imzolanadi — kirish endpointiga tegilmaydi (u parol
  // topishga qarshi rate-limit ostida; batafsil sabab: seed.mjs).
  const ready = issueTokens(users);

  console.log(`    token: ${ready.length}/${users.length} tayyor   \n`);

  const stopAt = Date.now() + DURATION_SEC * 1000;
  const gap = RAMP_MS / USERS;

  const clients = [];
  for (let i = 0; i < USERS; i++) {
    clients.push(runClient(i, ready[i].token, target.sessionId, stopAt));
    if (gap >= 1) await sleep(gap);

    if ((i + 1) % 25 === 0)
      process.stdout.write(`  ulandi: ${stats.connected}/${i + 1}\r`);
  }

  console.log(`  ulandi: ${stats.connected}/${USERS}          \n`);
  console.log('  yuklama ostida...\n');

  await Promise.all(clients);

  // ------------------------------------------------------------ hisobot
  const okConnect = stats.connected;
  const rate = ((okConnect / USERS) * 100).toFixed(1);

  console.log(`
╔══════════════════════════════════════════════════════╗
║  NATIJA                                              ║
╚══════════════════════════════════════════════════════╝

  ULANISH
    muvaffaqiyatli   : ${okConnect}/${USERS}  (${rate}%)
    yiqilgan         : ${stats.failed}
    uzilish          : ${stats.disconnects}
    vaqt p50/p95/p99 : ${pct(stats.connectMs, 50)} / ${pct(stats.connectMs, 95)} / ${pct(stats.connectMs, 99)} ms

  JOIN SESSION
    p50/p95/p99      : ${pct(stats.joinMs, 50)} / ${pct(stats.joinMs, 95)} / ${pct(stats.joinMs, 99)} ms
    xonadagi eng ko'p ishtirokchi : ${stats.presenceMax}

  CHAT
    yuborildi        : ${stats.messagesSent}
    qabul qilindi    : ${stats.messagesReceived}
    rate-limit       : ${stats.rateLimited}
    kechikish p50/p95/p99 : ${pct(stats.chatLatencyMs, 50)} / ${pct(stats.chatLatencyMs, 95)} / ${pct(stats.chatLatencyMs, 99)} ms
`);

  if (stats.errors.size) {
    console.log('  XATOLAR');
    for (const [msg, n] of [...stats.errors].sort((a, b) => b[1] - a[1]).slice(0, 8))
      console.log(`    ${String(n).padStart(4)} × ${msg}`);
    console.log();
  }

  // ------------------------------------------------------------ baho
  const p95Chat = pct(stats.chatLatencyMs, 95);
  const problems = [];

  if (okConnect < USERS * 0.98) problems.push(`ulanishlarning ${(100 - rate).toFixed(1)}% i yiqildi`);
  if (p95Chat > 1000) problems.push(`chat kechikishi p95 = ${p95Chat} ms (chegara 1000)`);
  if (pct(stats.connectMs, 95) > 3000) problems.push('ulanish p95 > 3 sekund');
  if (stats.disconnects > USERS * 0.05) problems.push(`${stats.disconnects} ta kutilmagan uzilish`);

  // Presence to'plami to'lmagan bo'lsa test o'zi ISHONCHSIZ: bu holda
  // xonada da'vo qilingan sondan kam odam bo'lgan va broadcast narxi ham,
  // JoinSession javobi ham haqiqiy yuklamani ko'rsatmagan.
  if (stats.presenceMax < USERS * 0.9)
    problems.push(`presence faqat ${stats.presenceMax}/${USERS} ishtirokchini ko'rdi`);

  if (problems.length === 0) {
    console.log(`  ✅ ${USERS} FOYDALANUVCHI MUAMMOSIZ KO'TARILDI\n`);
  } else {
    console.log('  ⚠️  MUAMMOLAR:');
    for (const p of problems) console.log(`     - ${p}`);
    console.log();
    process.exitCode = 1;
  }
};

main().catch((e) => {
  console.error('\n❌ Test yiqildi:', e.message);
  process.exit(1);
});
