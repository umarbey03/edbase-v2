/**
 * ZIN-NUR — yuklama testi uchun HAQIQIY o'quvchilarni tayyorlaydi (idempotent).
 *
 * ========================================================================
 * NIMA UCHUN BU FAYL KERAK BO'LDI
 * ========================================================================
 * Yuklama skripti ilgari BITTA admin tokenini 200 klientga ulashardi. Server
 * esa ikki narsani ham FOYDALANUVCHI bo'yicha kalitlaydi:
 *
 *   • rate-limit  — `chatrate:{sessionId}:{userId}`  (LiveClassHub.SendMessage)
 *   • presence    — `presence.AddAsync(sessionId, entry)`, entry kaliti UserId
 *
 * Ya'ni bitta token bilan o'lchov IKKI joyda yolg'on chiqadi:
 *
 *  1) 200 ta ulanish bitta "1 xabar / 2 sekund" budjetini bo'lishadi.
 *     Amalda tekshirildi: 5 klient 20 sekundda 8 xabar yubordi va 12 tasi
 *     rate-limit bo'ldi — ya'ni chat kechikishi deyarli o'lchanmaydi.
 *
 *  2) Presence to'plamida 200 ta yozuv o'rniga BITTA yozuv qoladi. Natijada
 *     `JoinSession` javobi (to'liq ro'yxat!) va delta broadcast REAL
 *     narxidan bir necha barobar arzon ko'rinadi — aynan o'sha narx esa
 *     "200 kishi bitta xonada" da'vosining o'zagi.
 *
 * Shuning uchun har klient O'Z foydalanuvchisi va O'Z tokeni bilan ulanadi.
 *
 * ========================================================================
 * NIMA UCHUN TOKENNI O'ZIMIZ IMZOLAYMIZ (parol bilan kirmaymiz)
 * ========================================================================
 * Ilgari skript har o'quvchi uchun `POST /api/v1/auth/login` qilardi va shu
 * yerda "200 ta kirish ~3 sekund, o'lchovga ta'sir qilmaydi" deb yozilgan
 * edi. O'SHA HISOB ENDI TO'G'RI EMAS.
 *
 * Sabab: kirish endpointi parol topishga qarshi CHEKLANDI
 * (`[EnableRateLimiting("auth")]`, IP bo'yicha 20 so'rov/daqiqa). Bitta
 * IP'dan 200 ta ketma-ket kirish — bu aynan cheklov to'sishi KERAK bo'lgan
 * naqsh. Ya'ni tanlov "tez" va "sekin" o'rtasida emas edi:
 *
 *   • yuklama uchun cheklovni bo'shatish   -> himoyani o'chirish demak;
 *   • skriptni 10+ daqiqa kutishga majburlash -> test amalda ishlamay qoladi.
 *
 * Shuning uchun tayyorgarlik bosqichi kirish endpointidan UMUMAN
 * foydalanmaydi: tokenlar `JWT_SECRET` bilan lokal imzolanadi (HS256 —
 * server ham aynan shuni tekshiradi, ya'ni token HAQIQIY).
 *
 * BU O'LCHOVNI BUZMAYDI: test SignalR hub'ining sig'imini o'lchaydi, kirish
 * endpointining unumdorligini emas. Kirish o'lchov BOSHLANISHIDAN oldin
 * tugardi va natijaga baribir kirmasdi. Yutuq esa bor: 200 ta BCrypt
 * (WorkFactor=11) hisobi ~24 sekund CPU yeyardi — endi u yo'q.
 *
 * NARXI: skript `JWT_SECRET` ni bilishi kerak (`.env` dan yoki muhitdan).
 * Bu maqbul — u allaqachon admin paroli bilan ishlaydi va faqat o'z
 * stack'ingizga qarshi yugurtiriladi.
 */
import { createHmac, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PREFIX = 'zload';
const DOMAIN = 'zinnur.test';

/** Yuklama foydalanuvchilarining paroli — bu HAQIQIY tizimda ishlatilmaydi. */
export const LOAD_PASSWORD = 'ZinnurLoad!2345';

const emailFor = (i) => `${PREFIX}-${String(i).padStart(4, '0')}@${DOMAIN}`;
const nameFor = (i) => `Yuklama Oquvchi ${i}`;

// ---------------------------------------------------------------- HTTP yordamchi

const call = async (api, path, { token, method = 'GET', body } = {}) => {
  const res = await fetch(`${api}${path}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });

  const text = await res.text();
  const data = text ? JSON.parse(text) : null;

  if (!res.ok) {
    const detail = data?.detail ?? data?.title ?? text.slice(0, 200);
    const error = new Error(`${method} ${path} -> ${res.status}: ${detail}`);
    error.status = res.status;
    throw error;
  }

  return data;
};

/**
 * Cheklangan parallellik. 200 so'rovni bir zumda otish lokal port va
 * Postgres ulanish hovuzini (30) keraksiz band qiladi — bu yuklama testi
 * EMAS, shunchaki tayyorgarlik.
 */
const mapLimit = async (items, limit, fn) => {
  const out = new Array(items.length);
  let next = 0;

  const worker = async () => {
    while (next < items.length) {
      const i = next++;
      out[i] = await fn(items[i], i);
    }
  };

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
};

// ---------------------------------------------------------------- nishon dars

/**
 * Yuklama uchun dars tanlaydi.
 *
 * KURATOR guruhi ATAYLAB chetlab o'tiladi: unga o'quvchi TO'G'RIDAN-TO'G'RI
 * qo'shilmaydi (`GroupService.EnsureAcceptsDirectMembers` 409 beradi) — uning
 * o'quvchilari bog'langan ustoz guruhlaridan keladi.
 */
export const pickTarget = async (api, token) => {
  if (process.env.SESSION_ID && process.env.GROUP_ID) {
    return {
      sessionId: Number(process.env.SESSION_ID),
      groupId: Number(process.env.GROUP_ID),
      title: '(env orqali berilgan)',
    };
  }

  const groups = await call(api, '/api/v1/groups?page=1&pageSize=100', { token });

  const usable = new Map(
    groups.items
      .filter((g) => g.isActive && g.type !== 'Curator')
      .map((g) => [g.id, g]),
  );

  const sessions = await call(api, '/api/v1/live-sessions', { token });

  // Jonli dars birinchi navbatda — u eng real stsenariy.
  const ordered = [...sessions].sort(
    (a, b) => (b.status === 'Live') - (a.status === 'Live'),
  );

  const target = ordered.find((s) => usable.has(s.groupId));

  if (!target) {
    throw new Error(
      'Yuklama uchun mos dars topilmadi: kurator bo\'lmagan FAOL guruhda dars yo\'q. '
      + 'SESSION_ID va GROUP_ID ni qo\'lda bering.',
    );
  }

  return {
    sessionId: target.id,
    groupId: target.groupId,
    title: `${target.title} (${target.status})`,
  };
};

// ---------------------------------------------------------------- foydalanuvchilar

/** Mavjud yuklama foydalanuvchilarini email -> id xaritasi qilib qaytaradi. */
const existingUsers = async (api, token) => {
  const found = new Map();

  for (let page = 1; ; page++) {
    const res = await call(
      api,
      `/api/v1/users?search=${PREFIX}&page=${page}&pageSize=100`,
      { token },
    );

    for (const u of res.items) found.set(u.email, u.id);

    if (page >= res.totalPages || res.items.length === 0) break;
  }

  return found;
};

/**
 * `count` ta o'quvchi MAVJUDLIGINI kafolatlaydi va guruhga a'zo qiladi.
 * Idempotent: ikkinchi yugurtirishda hech nima yaratilmaydi.
 */
export const ensureUsers = async (api, token, count, groupId, onProgress) => {
  const found = await existingUsers(api, token);

  const wanted = Array.from({ length: count }, (_, i) => i + 1);
  const missing = wanted.filter((i) => !found.has(emailFor(i)));

  let created = 0;

  await mapLimit(missing, 10, async (i) => {
    const res = await call(api, '/api/v1/users', {
      token,
      method: 'POST',
      body: {
        fullName: nameFor(i),
        email: emailFor(i),
        role: 'Student',
        password: LOAD_PASSWORD,
        isActive: true,
      },
    });

    found.set(emailFor(i), res.user.id);
    onProgress?.('yaratildi', ++created, missing.length);
  });

  const users = wanted.map((i) => ({
    index: i,
    email: emailFor(i),
    id: found.get(emailFor(i)),
  }));

  // ---- guruhga a'zolik ----
  const members = await call(api, `/api/v1/groups/${groupId}/members`, { token });
  const active = new Set(
    members.filter((m) => m.status === 'Active').map((m) => m.studentId),
  );

  const toAdd = users.filter((u) => !active.has(u.id));
  let added = 0;

  await mapLimit(toAdd, 10, async (u) => {
    try {
      await call(api, `/api/v1/groups/${groupId}/members`, {
        token,
        method: 'POST',
        body: { studentId: u.id },
      });
    } catch (e) {
      // 409 = allaqachon a'zo (parallel qo'shishda bo'lishi mumkin) — zararsiz.
      if (e.status !== 409) throw e;
    }

    onProgress?.('a\'zo qilindi', ++added, toAdd.length);
  });

  return { users, created, added };
};

// ---------------------------------------------------------------- tokenlar

/**
 * `.env` ni o'qiydi (oddiy `KALIT=qiymat`). Muhit o'zgaruvchisi USTUN —
 * boshqa stack'ga qarshi yugurtirish uchun `JWT_SECRET=... node ...` yetarli.
 */
const dotEnv = () => {
  const file = path.join(
    path.dirname(fileURLToPath(import.meta.url)), '../..', '.env',
  );

  if (!fs.existsSync(file)) return {};

  const values = {};

  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (match) values[match[1]] = match[2].trim();
  }

  return values;
};

const jwtConfig = () => {
  const env = { ...dotEnv(), ...process.env };

  if (!env.JWT_SECRET) {
    throw new Error(
      'JWT_SECRET topilmadi. `.env` faylini tekshiring yoki '
      + 'JWT_SECRET=... qilib bering — tokenlar lokal imzolanadi '
      + '(sabab: kirish endpointi rate-limit ostida).',
    );
  }

  return {
    secret: env.JWT_SECRET,
    issuer: env.JWT_ISSUER ?? 'zinnur',
    audience: env.JWT_AUDIENCE ?? 'zinnur-web',
  };
};

const b64url = (value) => Buffer.from(value).toString('base64url');

/**
 * Bitta kirish tokeni. Claim'lar `JwtTokenService.CreateAccessToken` bilan
 * AYNAN bir xil bo'lishi shart — aks holda server tokenni qabul qiladi-yu,
 * hub'da foydalanuvchi "Noma'lum" bo'lib chiqadi yoki rol topilmaydi.
 *
 * `ver` (TokenVersion) = 0: bu foydalanuvchilarni shu skriptning o'zi
 * yaratadi va ularning paroli hech qachon almashtirilmaydi, "hamma
 * qurilmadan chiqish" ham qilinmaydi — ya'ni versiya 0 bo'lib qolaveradi.
 */
const signAccessToken = (user, config, ttlSeconds) => {
  const now = Math.floor(Date.now() / 1000);

  const header = b64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));

  const payload = b64url(JSON.stringify({
    sub: String(user.id),
    jti: randomUUID().replaceAll('-', ''),
    ver: '0',
    token_use: 'access',
    role: 'Student',
    name: user.fullName,
    iss: config.issuer,
    aud: config.audience,
    iat: now,
    nbf: now,
    exp: now + ttlSeconds,
  }));

  const data = `${header}.${payload}`;
  const signature = createHmac('sha256', config.secret).update(data).digest('base64url');

  return `${data}.${signature}`;
};

/**
 * Har foydalanuvchi uchun kirish tokeni yasaydi (kirish endpointiga
 * tegmasdan — sabab fayl boshida).
 *
 * TTL uzun (1 soat): eng uzun yuklama yugurtirishi ham unga sig'adi va
 * o'lchov o'rtasida token muddati tugab, "uzilish" statistikasini
 * yolg'on to'ldirib qo'ymaydi.
 */
export const issueTokens = (users, ttlSeconds = 3600) => {
  const config = jwtConfig();

  return users.map((u) => {
    const fullName = nameFor(u.index);
    return { ...u, fullName, token: signAccessToken({ ...u, fullName }, config, ttlSeconds) };
  });
};
