# ZIN-NUR v2 — QAT'IY TEXNIK SHARTNOMA (SPEC)

> **HOLAT:** Domain va Application qatlamlari YOZILGAN va kompilyatsiyadan o'tgan.
> Ular haqiqat manbai — quyidagi imzolar aynan kodga mos.
>
> Bu hujjat **majburiy shartnoma**. Barcha qismlar (backend qatlamlari, frontend,
> infratuzilma) shu nomlarga TAYANADI. Hech kim nom, imzo yoki portni o'zgartirmaydi.
> O'zgartirish kerak bo'lsa — avval shu fayl yangilanadi.

---

## 0. TEXNOLOGIYALAR (aniq versiyalar)

| Qism | Texnologiya | Versiya |
|---|---|---|
| Backend | .NET Web API | **9.0** |
| ORM | EF Core + Npgsql | 9.0.0 / 9.0.2 |
| Baza | PostgreSQL | **17-alpine** |
| Kesh/backplane | Redis | **7-alpine** |
| Realtime | ASP.NET SignalR + Redis backplane | 9.0.0 |
| Video | LiveKit (self-hosted) | `livekit/livekit-server:v1.8` |
| Frontend | Vue 3 (Composition API) + TypeScript | Vue 3.5, TS 5.6 |
| Build | Vite | 6.x |
| Uslub | Tailwind CSS | **v4** |
| Frontend serveri | Nginx | alpine |

---

## 1. PAPKA TUZILMASI (yaratilgan, o'zgarmaydi)

```
zinnur-v2/
├── backend/
│   ├── Directory.Build.props
│   ├── Zinnur.sln
│   ├── src/
│   │   ├── Zinnur.Domain/          # tashqi bog'liqlik YO'Q
│   │   ├── Zinnur.Application/     # -> Domain
│   │   ├── Zinnur.Infrastructure/  # -> Application
│   │   └── Zinnur.WebApi/          # -> Infrastructure
│   └── tests/{Zinnur.UnitTests,Zinnur.IntegrationTests}
├── frontend/
│   └── src/{app,pages,features,entities,shared}
├── infra/{nginx,livekit,postgres}
├── docs/
└── docker-compose.yml
```

**Namespace qoidasi:** papka yo'li = namespace.
Masalan `src/Zinnur.Domain/Entities/User.cs` → `namespace Zinnur.Domain.Entities;`

---

## 2. DOMAIN — ENUM'LAR

Barchasi `namespace Zinnur.Domain.Enums;`. **Postgres'da `int` sifatida saqlanadi.**

```csharp
public enum UserRole    { Student = 0, Teacher = 1, Assistant = 2, Academic = 3, Admin = 4 }
public enum SessionType { Teacher = 0, Assistant = 1 }
public enum SessionStatus    { Scheduled = 0, Live = 1, Ended = 2, Cancelled = 3 }
public enum AttendanceStatus { Absent = 0, Present = 1, Late = 2, Partial = 3 }
public enum MemberStatus     { Active = 0, Paused = 1, Stopped = 2, Moved = 3 }
```

## 3. DOMAIN — ENTITY'LAR

`namespace Zinnur.Domain.Entities;` — barchasi `BaseEntity` dan meros oladi.

```csharp
// Zinnur.Domain/Common/BaseEntity.cs
public abstract class BaseEntity
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

| Entity | Maydonlar (aniq nomlar) |
|---|---|
| **User** | `FullName:string`, `Email:string`(unique), `PasswordHash:string`, `Phone:string?`(unique), `TelegramId:long?`(unique), `Role:UserRole`, `IsActive:bool=true`, `TokenVersion:int=0` |
| **Course** | `Name:string`, `Description:string?`, `IsActive:bool=true`, `Position:int` |
| **CourseModule** | `CourseId:long`, `Course:Course?`, `Name:string`, `Position:int` |
| **ModuleLesson** | `ModuleId:long`, `Module:CourseModule?`, `Name:string`, `Description:string?`, `Position:int`, `DurationMin:int?` |
| **Group** | `Name:string`, `CourseId:long?`, `TeacherId:long?`, `AssistantId:long?`, `StartDate:DateOnly`, `IsActive:bool=true`, `RecordEnabled:bool=false` |
| **GroupMember** | `GroupId:long`, `StudentId:long`, `Status:MemberStatus=Active`, `JoinedAt:DateTimeOffset` |
| **LiveSession** | `GroupId:long`, `Group:Group?`, `HostId:long?`, `Title:string?`, `Type:SessionType`, `Status:SessionStatus=Scheduled`, `ScheduledStart:DateTimeOffset`, `ScheduledEnd:DateTimeOffset`, `ActualStart:DateTimeOffset?`, `ActualEnd:DateTimeOffset?`, `RoomName:string`(**unique**), `RecordingUrl:string?`, `ExtendedMin:int=0` |
| **Attendance** | `SessionId:long`, `StudentId:long`, `Status:AttendanceStatus=Absent`, `FirstJoinAt:DateTimeOffset?`, `LastJoinAt:DateTimeOffset?`, `LeftAt:DateTimeOffset?`, `DurationSeconds:int=0` — **unique(SessionId,StudentId)** |
| **ChatMessage** | `SessionId:long`, `SenderId:long`, `SenderName:string`, `Body:string`, `SentAt:DateTimeOffset` |

**MUHIM (eski tizim buglaridan saqlanish):**
- `LiveSession.RoomName` — **UNIQUE indeks majburiy**, format: `s-{Id}-{8 belgili random}`
- `Attendance` — `FirstJoinAt` (o'zgarmaydi) va `LastJoinAt` (har kirishda yangilanadi) **alohida**.
  Davomiylik `LastJoinAt` dan hisoblanadi, aks holda qayta ulanishda vaqt ikki marta qo'shiladi.
- `User.TokenVersion` — parol/rol o'zgarganda oshiriladi; JWT'dagi `ver` claim mos kelmasa token bekor.

---

## 4. APPLICATION — PORT INTERFEYSLARI

`namespace Zinnur.Application.Common.Interfaces;`

```csharp
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseModule> Modules { get; }
    DbSet<ModuleLesson> ModuleLessons { get; }
    DbSet<Group> Groups { get; }
    DbSet<GroupMember> GroupMembers { get; }
    DbSet<LiveSession> LiveSessions { get; }
    DbSet<Attendance> Attendances { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ILiveKitTokenService
{
    string CreateAccessToken(LiveKitTokenRequest request);
    string ServerUrl { get; }   // BRAUZERGA beriladigan manzil = LiveKit:PublicUrl
}

public interface IJwtTokenService
{
    string CreateAccessToken(User user);           // 15 daqiqa
    string CreateRefreshToken(User user);          // 14 kun
    (long UserId, int TokenVersion)? ValidateRefreshToken(string token);
}

public interface IPasswordHasher
{
    // ASYNC: BCrypt ~250ms CPU yeydi -> Task.Run orqali thread pool'ga chiqariladi
    Task<string> HashAsync(string password, CancellationToken ct = default);
    Task<bool> VerifyAsync(string password, string hash, CancellationToken ct = default);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}

public interface IPresenceService
{
    Task AddAsync(long sessionId, PresenceEntry entry, CancellationToken ct = default);
    Task RemoveAsync(long sessionId, long userId, CancellationToken ct = default);
    Task<IReadOnlyList<PresenceEntry>> ListAsync(long sessionId, CancellationToken ct = default);
    Task SetHandRaisedAsync(long sessionId, long userId, bool raised, CancellationToken ct = default);
    Task<int> CountAsync(long sessionId, CancellationToken ct = default);
    Task ClearAsync(long sessionId, CancellationToken ct = default);
}

public interface ICurrentUser { long? UserId { get; } UserRole? Role { get; } bool IsAuthenticated { get; } }

public interface IChatMessageWriter { ValueTask EnqueueAsync(ChatMessage message, CancellationToken ct = default); }
```

`namespace Zinnur.Application.Common.Models;`

```csharp
public sealed record LiveKitTokenRequest(
    string RoomName, string Identity, string DisplayName,
    bool CanPublish, bool IsHost, TimeSpan? Ttl = null);

public sealed record PresenceEntry(
    long UserId, string DisplayName, string Role,
    bool HandRaised, DateTimeOffset JoinedAt);
```

---

## 5. REST API SHARTNOMASI

Baza yo'l: `/api/v1`. Autentifikatsiya: `Authorization: Bearer <accessToken>`.

| Metod | Yo'l | Ruxsat | Javob |
|---|---|---|---|
| POST | `/api/v1/auth/login` | anonim | `AuthResponse` |
| POST | `/api/v1/auth/refresh` | anonim | `AuthResponse` |
| POST | `/api/v1/auth/logout` | auth | `204` |
| GET | `/api/v1/auth/me` | auth | `UserDto` |
| GET | `/api/v1/live-sessions` | auth | `LiveSessionDto[]` |
| GET | `/api/v1/live-sessions/{id}` | auth | `LiveSessionDto` |
| POST | `/api/v1/live-sessions/{id}/start` | Teacher/Assistant/Admin | `LiveSessionDto` |
| POST | `/api/v1/live-sessions/{id}/end` | Teacher/Assistant/Admin | `LiveSessionDto` |
| **POST** | **`/api/v1/live-sessions/{id}/token`** | auth (a'zo/host) | **`LiveKitJoinDto`** |
| GET | `/api/v1/live-sessions/{id}/messages?take=50` | auth | `ChatMessageDto[]` |
| GET | `/health` / `/health/ready` | anonim | health |

**DTO'lar** (`namespace Zinnur.WebApi.Contracts;` yoki Application/Dtos):

```csharp
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, string RefreshToken, UserDto User);
public sealed record UserDto(long Id, string FullName, string Email, string Role);

public sealed record LiveSessionDto(
    long Id, long GroupId, string GroupName, string? Title,
    string Type, string Status,
    DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualStart, DateTimeOffset? EndsAt, bool IsHost);

/// Frontend LiveKit'ga shu bilan ulanadi
public sealed record LiveKitJoinDto(
    string ServerUrl, string Token, string RoomName, bool IsHost, DateTimeOffset? EndsAt);

public sealed record ChatMessageDto(
    long Id, long SenderId, string SenderName, string Body, DateTimeOffset SentAt);
```

**Xato formati (RFC 7807 ProblemDetails)** — global middleware qaytaradi:
```json
{ "type":"...", "title":"...", "status":400, "detail":"...", "traceId":"..." }
```

---

## 6. SIGNALR SHARTNOMASI (eng muhim qism)

**Hub yo'li:** `/hubs/live`
**Autentifikatsiya:** `?access_token=<jwt>` (WebSocket header qo'llab-quvvatlamaydi)
**Backplane:** Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`)
**SignalR guruh nomi:** `session-{sessionId}`

### Klient → Server (Hub metodlari)

| Metod | Argument | Izoh |
|---|---|---|
| `JoinSession` | `long sessionId` | Guruhga qo'shiladi, presence yoziladi |
| `LeaveSession` | `long sessionId` | |
| `SendMessage` | `long sessionId, string body` | Server `SentAt` va `SenderName`ni O'ZI qo'yadi |
| `RaiseHand` | `long sessionId, bool raised` | |

### Server → Klient (klient shu nomlarni tinglaydi)

| Hodisa | Payload | Izoh |
|---|---|---|
| `ChatMessage` | `ChatMessageDto` | Yangi xabar |
| `PresenceChanged` | `{ userId, displayName, role, joined: bool, count: int }` | Kirdi/chiqdi |
| `HandRaised` | `{ userId, displayName, raised: bool }` | |
| `SessionEnded` | `{ sessionId }` | Hamma chiqariladi |

### 200 FOYDALANUVCHI UCHUN MAJBURIY QOIDALAR
1. **To'liq ro'yxat broadcast QILINMAYDI.** `PresenceChanged` faqat delta (kim + umumiy son).
   To'liq ro'yxat faqat `JoinSession` javobida bir marta beriladi.
2. **Chat serverda rate-limit:** foydalanuvchiga **1 xabar / 2 sekund** (Redis hisoblagich).
3. **Xabar uzunligi 500 belgi**, server tomonda kesiladi.
4. **Chat DB'ga fon navbatida yoziladi** (`Channel<T>` + `BackgroundService`), broadcast bloklanmaydi.
5. Presence **Redis'da** (in-memory `Dictionary` TAQIQLANADI — ko'p instance buziladi).

---

## 7. LIVEKIT TOKEN FORMATI (aniq)

HS256 JWT, `LIVEKIT_API_SECRET` bilan imzolanadi.

```jsonc
{
  "iss": "<LIVEKIT_API_KEY>",     // majburiy
  "sub": "<userId>",              // identity
  "name": "<Foydalanuvchi ismi>",
  "nbf": 1730000000,
  "exp": 1730021600,              // default +6 soat
  "video": {
    "roomJoin": true,
    "room": "<RoomName>",
    "canPublish": true,
    "canSubscribe": true,
    "canPublishData": true,
    "roomAdmin": false            // host uchun true
  }
}
```
> `video` claim'i **camelCase** bo'lishi SHART — LiveKit boshqa nomni tanimaydi.

---

## 8. DOCKER XIZMATLARI (nomlar va portlar — o'zgarmaydi)

| Xizmat | Образ | Ichki port | Host port (dev) |
|---|---|---|---|
| `postgres` | `postgres:17-alpine` | 5432 | 5440 |
| `redis` | `redis:7-alpine` | 6379 | 6390 |

> **Redis eviction siyosati:** `noeviction` yoki `volatile-lru` bo'lishi SHART.
> `allkeys-lru` da presence kalitlari jimgina o'chiriladi va davomat buziladi —
> hech qanday xato ko'rinmaydi.
| `api` | build `./backend` | 8080 | 5080 |
| `web` | build `./frontend` (nginx) | 80 | 5173 |
| `livekit` | `livekit/livekit-server:v1.8` | 7880 | 7880 |

**LiveKit portlari:** TCP **7880** (WS/HTTP), TCP **7881** (RTC/TCP zaxira),
UDP **7882** (RTC mux — barcha media shu bitta portdan).
UDP mux ishlatiladi (diapazon emas) — firewall sozlash oson va 200 foydalanuvchi uchun yetarli.

**Tarmoq:** `zinnur-net` (bridge). `postgres` va `redis` host'ga **prod'da chiqarilmaydi**.

### Muhit o'zgaruvchilari (aniq nomlar)

```
# api
ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=zinnur;Username=zinnur;Password=...;Maximum Pool Size=30;Minimum Pool Size=2
# Pool CHEGARALANGAN: Npgsql default 100 — bu Postgres max_connections'ning hammasi.
# Ikkinchi api replikasi qo'shilsa 'too many clients' xatosi chiqadi.
ConnectionStrings__Redis=redis:6379
Jwt__Issuer=zinnur
Jwt__Audience=zinnur-web
Jwt__Secret=<32+ bayt>
Jwt__AccessMinutes=15
Jwt__RefreshDays=14
# DIQQAT: IKKI ALOHIDA manzil (deploy audit topilmasi).
#  Url       -> backend server API uchun (konteyner ichida)
#  PublicUrl -> BRAUZERGA qaytariladi. Prod'da HTTPS sahifadan ws:// bloklanadi,
#               shuning uchun bu doim wss:// bo'lishi SHART.
LiveKit__Url=http://livekit:7880
LiveKit__PublicUrl=ws://localhost:7880   # prod: wss://livekit.domen.uz
LiveKit__ApiKey=devkey
LiveKit__ApiSecret=<32+ bayt>
Cors__AllowedOrigins__0=http://localhost:5173

# web (build-time)
VITE_API_URL=http://localhost:5080
VITE_HUB_URL=http://localhost:5080/hubs/live

# livekit
LIVEKIT_KEYS=devkey: <ApiSecret bilan BIR XIL>
```

---

## 9. KOD SIFATI QOIDALARI (majburiy)

1. **Bog'liqlik faqat ichkariga.** `Domain` hech nimani import qilmaydi.
2. **Controller yupqa** — 20 satrdan oshmasin; biznes mantiq `Application`da.
3. **`DbContext` faqat `Infrastructure`da.** Controller'da `Where()` yozilmaydi.
4. **`async/await` hamma joyda**, `CancellationToken` uzatiladi.
5. **`.Result` / `.Wait()` TAQIQLANADI** (deadlock).
6. **Pul va vaqt:** `decimal` va `DateTimeOffset` (UTC).
7. **Parol `BCrypt`**, `Verify` — `Task.Run` orqali off-thread.
8. **Hech qanday sir kodda emas** — faqat konfiguratsiya.
9. Frontend: **`v-html` taqiqlanadi**, `<script setup lang="ts">` majburiy.
10. Har `.csproj` da `TreatWarningsAsErrors=true` (allaqachon `Directory.Build.props`da).
