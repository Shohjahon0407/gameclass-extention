# TZ: Web admin panelda GameClass ulanishini boshqarish

**Kim uchun:** Manajoy web admin panel frontend jamoasi.
**Holat:** 2-bo'limdagi backend API **tayyor** (2026-10-07, `manajoy_backend/mana`, Swagger tegi «🕹 Admin - Kompyuter integratsiyalari»). Frontend (1-bo'lim) — qilinishi kerak.
**Bog'liq hujjatlar:**
- `GO-LIVE.md` — chiqarish tartibi;
- `field-kit/FIELD-GUIDE.md` — klub egasi qo'llanmasi;
- `src/Manajoy.GameClass.Bridge/README.md` — bridge.

---

## 0. Kontekst

GameClass — klublarning kompyuter boshqaruv dasturi. Klub egasi uni Manajoy'ga o'zi ulaydi:

1. **ManaJoy Biznes** ilovasida: *Kompyuterlar holati → GameClass orqali ulash*. Ilova 8 belgili **ulash kodini** beradi (`XXXX-XXXX`, 15 daqiqa amal qiladi, bir martalik).
2. Klub kompyuterida `ManajoyGameClass-Setup.exe` ni ochib, shu kodni kiritadi.
3. Backend integratsiyani yaratadi va **jonli holatni yoqadi**.
4. Mijozlar ilovada kompyuterlarning bo'sh/band holatini ko'radi.

Hozir admin bu jarayonni ko'ra olmaydi va unga yordam bera olmaydi. Faqat serverda `manage.py` buyrug'i bor. **Maqsad:** support va operatorlar web admin paneldan quyidagilarni qila olsin:

- **Ko'rish:** qaysi klublar ulangan va ular hozir ishlayaptimi (jonli holat, oxirgi ma'lumot qachon kelgan).
- **Yordam:** egasi ilovaga kira olmasa, ulash kodini uning nomidan yaratish.
- **Boshqarish:**
  - mijozlarga ko'rsatishni yoqish/o'chirish;
  - ulanishni bekor qilish (masalan, kompyuter o'g'irlangan bo'lsa);
  - kompyuterlarni Manajoy stansiyalariga bog'lash.
- **Muammoni topish:** xato matni, bridge versiyasi, GameClass Server holati.

iCafeCloud integratsiyasi xuddi shu ma'lumot modelida (`provider`) yashaydi. Ekranlar ikkala provayderni ko'rsatadi, lekin **ulash kodi faqat GameClass uchun**.

## 1. Ekranlar

### 1.1. «Kompyuter integratsiyalari» monitoring sahifasi (yangi, P1)

**Joylashuvi:** chap menyu → *Sotuvchilar* guruhi → «Kompyuter integratsiyalari». URL: `/sellers/integrations`.

**Yuqoridagi kartochkalar** (`GET /admin/gaming-integrations/summary`):

| Kartochka | Qiymat | Rang |
|---|---|---|
| Jami ulangan | `total` | neytral |
| Jonli ishlayapti | `live_ok` | yashil |
| Ma'lumot kelmayapti | `stale` + `unavailable` | qizil |
| Mijozlarga o'chiq | `disabled` | kulrang |

Kartochkani bosish jadvalni shu holat bo'yicha filtrlaydi.

**Filtrlar:**
- qidiruv (klub nomi yoki ID);
- provayder: Hammasi / GameClass / iCafeCloud;
- holat: Hammasi / Jonli / Eskirgan / Ma'lumot yo'q / O'chirilgan / Xato.

Filtrlar URL query'da saqlanadi.

**Jadval** (`GET /admin/gaming-integrations`, sahifalash 20 tadan):

| Ustun | Manba | Ko'rinishi |
|---|---|---|
| Klub | `shop_name`, `seller_id` | nom + kulrang `#ID`; seller sahifasiga link |
| Provayder | `provider` | badge: «GameClass» (binafsha) / «iCafeCloud» (ko'k) |
| Jonli holat | `live.state`, `live.age_seconds` | pill. `ok` → yashil «Jonli · 4 s oldin»; `stale` → sariq «Eskirgan · 3 daq oldin»; `unavailable` → qizil «Ma'lumot yo'q» |
| Mijozlarga | `polling_enabled` | «Ko'rinadi» / «O'chiq» |
| Kompyuterlar | `live.counts` | `12 bo'sh · 8 band · 1 o'chiq` (jami tooltip'da) |
| Stansiyalar | `stations_confirmed / stations_total` | `22 / 25`. `stations_unmapped > 0` bo'lsa sariq belgi va «3 ta bog'lanmagan» |
| Oxirgi ma'lumot | `last_success_at` | nisbiy vaqt; tooltip'da aniq sana |
| Versiya | `gameclass.bridge_version` | faqat GameClass uchun, masalan `0.3.0` |
| Xato | `error_message` | bo'lsa qizil ikonka + tooltip |

- **Saralash:** standart bo'yicha «muammoli birinchi» (`unavailable` → `stale` → `ok`); ustun sarlavhasini bosib o'zgartiriladi.
- **Yangilash:** har 30 soniyada fonda (sahifa ko'rinib turganda), «yangilandi N s oldin» yozuvi bilan.
- **Bo'sh holat:** «Hali hech bir klub ulanmagan».

### 1.2. Seller sahifasida «Kompyuterlar» tabi (P1)

Faqat `seller_type = gaming` bo'lgan sotuvchilarda ko'rinadi. URL: `/sellers/{id}?tab=computers`.

**Ma'lumot:** `GET /admin/sellers/{id}/gaming-integration`. Tab ochiq turganda har 15 soniyada yangilanadi.

#### A. Integratsiya yo'q (`exists=false`)

Ikki ustunli bo'sh holat:

- **«GameClass orqali ulash»** kartasi (asosiy):
  - Matn: «Klub egasi odatda ManaJoy Biznes ilovasidan o'zi ulaydi. Agar ilovaga kira olmasa, kodni shu yerdan yarating va egasiga yuboring».
  - Tugma: **«Ulash kodini yaratish»** → 1.3-modal.
- **«iCafeCloud»** kartasi (ma'lumot uchun): «iCafeCloud'ni klub egasi ilovada Cafe ID va API kalit bilan ulaydi». Tugma yo'q.

#### B. Integratsiya bor

**1) Holat sarlavhasi (karta):**

| Element | Manba |
|---|---|
| Provayder badge | `provider` |
| Katta holat pill | Jonli / Eskirgan / Ma'lumot yo'q / Xato (`status=error` bo'lsa `error_message` bilan) |
| «Oxirgi ma'lumot: 5 s oldin» | `last_success_at` |
| Faqat GameClass: «Bridge v0.3.0» | `gameclass.bridge_version` |
| Faqat GameClass: «GameClass Server: ishlayapti / operator kirmagan» | `gameclass.server_alive` (`true` / `false` / `null`) |
| Faqat GameClass: «Kalit: ••••xEPE» | `api_key_hint` |
| Faqat GameClass: «Ulagan: Ism (sana)» | `updated_by`, `updated_at` |

**2) Hisoblar qatori:** Bo'sh / Band / O'chiq / Ta'mirda / Noma'lum (`live.counts`). Ranglar mijoz ilovasidagi bilan bir xil.

**3) Sozlamalar:**
- Switch **«Mijozlarga ko'rsatish»** (`polling_enabled`). O'chirishda tasdiq: «Mijozlar bu klub kompyuterlari holatini ko'rmay qoladi. Bridge ma'lumot yuborishda davom etadi».
- Switch **«Stansiyalarni avtomatik boshqarish»** (`auto_manage_stations`).
- So'rov oralig'i (`poll_interval_seconds`) **faqat iCafe uchun** ko'rsatiladi. GameClass o'zi yuboradi.

**4) Amallar:**

| Tugma | Kimga | API | Izoh |
|---|---|---|---|
| Sinxronlash | ikkalasi | `POST …/sync` | Natija toast'da: yaratildi / bog'landi / bog'lanmagan. 429 kelsa: «10 soniyadan keyin» |
| Yangi ulash kodi | GameClass | `POST …/gameclass/pairing-code` | 1.3-modal. Ogohlantirish: «Kod ishlatilganda eski kalit bekor bo'ladi» |
| Kalitni bekor qilish | GameClass | `POST …/gameclass/revoke-key` | Xavfli: tasdiq + «sabab» maydoni (majburiy). Klub kompyuteridagi bridge darhol rad etiladi (401); qayta ulash faqat yangi kod bilan |
| Yagona manba qilish | ikkalasi | `POST …/adopt-stations` | Ikki bosqich: avval `confirm=false` reja modali, keyin `confirm=true` |
| Integratsiyani uzish | ikkalasi | `POST …/disconnect` | Xavfli: tasdiq + sabab. «Stansiyalar o'chmaydi, bog'lanishlar o'chadi» |

**5) Stansiyalar jadvali** (`GET …/stations`):

| Ustun | Manba |
|---|---|
| Tashqi PC | `external_name` (GameClass'da PC raqami) |
| Zona | `external_area_name` |
| Jonli holat | `live_state` (rangli nuqta) |
| Manajoy stansiyasi | `resource_name` yoki «— bog'lanmagan» |
| Taklif | `suggested_resource_name` + `match_confidence`% |
| Amal | «Tasdiqlash» (taklifni) / «Boshqasini tanlash» (stansiya select, `GET /admin/sellers/{id}/gaming-resources`) / «Uzish» |

- Jadval tepasidagi tugma: **«Hamma takliflarni qabul qilish»** (`accept-suggestions`), faqat takliflar bo'lsa.
- Filtr: Hammasi / Bog'lanmagan / Takliflar.

### 1.3. «Ulash kodi» modali (P1)

**Ochilishi:** `POST /admin/sellers/{id}/gaming-integration/gameclass/pairing-code`.

**Ko'rinishi:**
- Katta monospace kod `ABCD-2345` va «Nusxalash» tugmasi.
- Teskari taymer `14:59`, `expires_in_seconds` dan hisoblanadi (brauzer soati bo'yicha).
- Muddati tugaganda kod xira bo'ladi va **«Yangi kod»** tugmasi chiqadi.
- **«Klub egasiga yuborish uchun matn»** bloki va «Nusxalash» tugmasi:
  ```
  Manajoy GameClass ulash:
  1) Dasturni yuklab oling: {download_url}
  2) GameClass Server kompyuterida oching (administrator ruxsatiga «Ha»).
  3) «Manajoy'ga ulash» qadamida kodni kiriting: ABCD-2345 (15 daqiqa amal qiladi).
  ```
- **Holatni kuzatish:** modal ochiq turganda har 4 soniyada `GET …/gaming-integration` so'raladi. `provider = gameclass` bo'lib, `api_key_hint` modal ochilgandagidan boshqacha bo'lsa — kod ishlatildi. Shunda modal **«Ulandi! ✓ {N} ta kompyuter»** holatiga o'tadi va tab yangilanadi. Mobil ilovadagi `GameClassConnectScreen` ham xuddi shu mantiqda ishlaydi.

**Xatolar:**
- **409:** «Klubda iCafeCloud ulangan. Avval uni uzing». Integratsiyani uzish tugmasiga havola bilan.
- **403:** «Faqat game club uchun».

### 1.4. Sotuvchilar ro'yxati (P2)

- Ro'yxatga (`/sellers`) «Integratsiya» filtri qo'shiladi: yo'q / GameClass / iCafe. *(Backend'da hali yo'q — P2'da `/admin/sellers` ga qo'shiladi. Hozircha badge uchun `GET /admin/gaming-integrations?page_size=100` ishlatsa bo'ladi.)*
- Gaming sotuvchilar qatorida kichik badge: jonli holat rangi + provayder.

## 2. API kontrakti (backend — tayyor)

**Umumiy qoidalar:**
- Barcha endpoint'lar `get_current_admin_user` bilan himoyalangan: `is_staff` yoki `is_superuser`; SMS (`amr=otp`) tokeni qabul qilinmaydi. Prefiks: `/api`.
- **Javoblar.** Muvaffaqiyatli javob: oddiy JSON obyekt. Xato javobi: `{"success": false, "error": "<matn>" | {"code","message"}, "status_code": N}`.
- **Sabab.** Xavfli amallar (`revoke-key`, `disconnect`, `adopt-stations confirm=true`, `PATCH polling_enabled=false`) `reason` talab qiladi (3–500 belgi). Bo'lmasa: `revoke-key`/`disconnect` → 422 (tana validatsiyasi), `PATCH`/`adopt` → 400 «Sabab kiritilishi shart».
- **Audit.** Har bir yozish amali `AdminActionLog` ga yoziladi (kim, qachon, nima, sabab). Ko'rish: `GET /api/admin/audit?target_type=seller&target_id={id}`. Amal nomlari: `gaming_pairing_code`, `gaming_revoke_key`, `gaming_settings` (payload'da `changes: {maydon: [eski, yangi]}`), `gaming_disconnect`, `gaming_sync`, `gaming_adopt`, `gaming_station` (payload'da `op: confirm|accept_suggestions|unlink`). Ulash kodi va kalit audit'ga yozilmaydi.
- **Mavjud logikadan qayta foydalanish.** Seller tomonidagi mantiq `fast_api/services/seller/gaming_integration.py` va `fast_api/services/integrations/gameclass/pairing.py` da allaqachon bor. Admin endpoint'lar ularni `seller_id` bo'yicha chaqiradi. Kalit **hech qachon** javobda qaytmaydi, faqat `api_key_hint`.

| Metod va yo'l | Tana | Javob |
|---|---|---|
| `GET /admin/gaming-integrations/summary` | — | `{total, live_ok, stale, unavailable, disabled, error, by_provider: {gameclass, icafe_cloud}}` |
| `GET /admin/gaming-integrations?provider=&state=&search=&ordering=&page=&page_size=` | — | `{items: [IntegrationRow], total, page, page_size}` |
| `GET /admin/sellers/{id}/gaming-integration` | — | `IntegrationDetail` (pastda) |
| `PATCH /admin/sellers/{id}/gaming-integration` | `{polling_enabled?, auto_manage_stations?, poll_interval_seconds?, reason?}` | `IntegrationDetail` |
| `POST /admin/sellers/{id}/gaming-integration/gameclass/pairing-code` | `{}` | `{code, expires_at, expires_in_seconds, download_url}` (seller endpoint bilan bir xil) |
| `POST /admin/sellers/{id}/gaming-integration/gameclass/revoke-key` | `{reason}` | `{success, detail}`: kalit almashtiriladi va ko'rsatilmaydi, bridge push 401 oladi, jonli envelope va kutilayotgan ulash kodi o'chiriladi. iCafe klubda 409 |
| `POST /admin/sellers/{id}/gaming-integration/sync` | `{}` | `GamingIntegrationSyncResult` (mavjud sxema) |
| `POST /admin/sellers/{id}/gaming-integration/adopt-stations` | `{confirm, reason?}` | `AdoptStationsResult` (mavjud) |
| `POST /admin/sellers/{id}/gaming-integration/disconnect` | `{reason}` | `{success, detail}`. Lokal stansiyalar qoladi, bog'lanishlar va jonli envelope o'chadi |
| `GET /admin/sellers/{id}/gaming-integration/stations` | — | `StationMapsResponse` (mavjud) |
| `POST /admin/sellers/{id}/gaming-integration/stations/{map_id}/confirm` | `{resource_id}` | `SimpleResult` |
| `POST /admin/sellers/{id}/gaming-integration/stations/accept-suggestions` | `{}` | `SimpleResult` |
| `POST /admin/sellers/{id}/gaming-integration/stations/{map_id}/unlink` | — | `SimpleResult` |
| `GET /admin/sellers/{id}/gaming-resources` | — | `{items: [{id, name, group_id, group_name, is_active}], total}`, stansiya tanlash uchun |

**`IntegrationRow`**:
```json
{
  "seller_id": 26, "shop_name": "Cyber Arena", "provider": "gameclass",
  "status": "connected", "state": "ok", "error_message": null,
  "polling_enabled": true,
  "live": {"state": "ok", "age_seconds": 4,
           "counts": {"total": 25, "available": 12, "busy": 8, "offline": 1, "disabled": 0, "unknown": 4}},
  "stations_total": 25, "stations_confirmed": 22, "stations_unmapped": 3,
  "last_success_at": "2026-10-07T07:32:13Z",
  "gameclass": {"bridge_version": "0.3.0", "server_alive": true, "heartbeat_age_seconds": 3}
}
```

**`IntegrationDetail`**: seller'ning `GamingIntegrationStatus` sxemasi, ustiga quyidagilar qo'shiladi:
- `seller_id`, `shop_name`;
- `updated_by: {id, full_name} | null`, `updated_at`;
- `gameclass: {bridge_version, server_alive, heartbeat_age_seconds} | null` (oxirgi envelope'dan).

`state` (ro'yxat uchun yakuniy holat, filtr `state=` shu bo'yicha): `error` (`status=error`) > `disabled` (`polling_enabled=false`) > `live.state`. Standart tartib `ordering=problem`: error → unavailable → stale → disabled → ok; boshqalari `name`, `last_success`.

**Xatolar:** 401 — token yo'q; 403 — admin emas yoki SMS-token; 404 — seller yo'q yoki `gaming` emas; 409 — boshqa provayder ulangan (pairing-code, revoke-key); 429 — sync cooldown.

`live.state` qiymatlari:
- `ok` — so'nggi 90 soniyada ma'lumot kelgan;
- `stale` — keyinroq kelgan;
- `unavailable` — envelope yo'q.

## 3. Xatti-harakat va chekka holatlar

| Holat | Frontend ko'rsatadi |
|---|---|
| `live.state = stale` 3 daqiqadan ko'p | «Klub kompyuteri yoki internet o'chgan bo'lishi mumkin». Holat kartasida yordam havolasi (`FIELD-GUIDE.md` → Muammolar) |
| GameClass, `server_alive = false` | «GameClass Server'da operator kirmagan, holatlar "noma'lum"» |
| `status = error`, `error_code = auth` (iCafe) | «API kalit noto'g'ri yoki server IP allowlist'da emas» |
| `stations_unmapped > 0` | Stansiyalar jadvalida «Bog'lanmagan» filtri avtomatik tanlangan banner |
| 429 (sync/test cooldown) | Tugma N soniya bloklanadi, sanoq bilan |
| 409 `other_provider` | Kod yaratilmaydi, tushuntirish chiqadi |
| Admin va ega bir vaqtda kod yaratsa | Yangi kod eskisini bekor qiladi; modalda ogohlantirish: «Bu kod ilovadagi kodni bekor qiladi» |
| Seller `gaming` emas | Tab ko'rinmaydi |

## 4. UI/UX talablari

- **Uslub:** admin panelning mavjud UI kit'i (jadval, pill, modal, toast, switch).
- **Ranglar** (mijoz va Biznes ilova bilan bir xil):
  - bo'sh — `#1E9E62`;
  - band — `#1B54E4`;
  - o'chiq — `#9AA5B1`;
  - ta'mir/noma'lum — `#E08A1E`;
  - xato — `#E0463A`.
- **Til:** o'zbek (asosiy) va rus; kalitlar panelning mavjud i18n tizimida. Atamalar mobil ilova bilan bir xil: «Kompyuterlar holati», «Ulash kodi», «Mijozlarga ko'rsatish», «Stansiya».
- **Vaqt:** nisbiy («5 s oldin»), tooltip'da Toshkent vaqti bilan to'liq sana.
- **Xavfsizlik:** kod nusxalanadi, lekin log'ga yoki analitikaga yozilmaydi. Kalit hech qayerda to'liq ko'rsatilmaydi.
- **Xavfli tugmalar:** qizil, ikki bosqichli tasdiq, majburiy «sabab» maydoni.
- **Yuklanish:** skeleton holatlari; avtomatik yangilashda jadval «sakramasligi» kerak (scroll va tanlangan filtrlar saqlanadi).

## 5. Qabul mezonlari

1. Monitoring sahifasi jami / jonli / eskirgan klublarni to'g'ri ko'rsatadi. Filtrlar va qidiruv ishlaydi, sahifa 30 soniyada o'zi yangilanadi.
2. Integratsiyasi yo'q gaming klub uchun admin kod yaratadi. Kod klub kompyuteridagi sozlash dasturida ishlatilgach, modal 4–8 soniya ichida «Ulandi!» ga o'tadi, tab esa ulangan holatni ko'rsatadi.
3. «Mijozlarga ko'rsatish» o'chirilgach, mijoz ilovasida klubning jonli holati yo'qoladi (`/buyer/gaming/{id}/live-status` → `enabled: false`). Qayta yoqilganda qaytadi.
4. «Kalitni bekor qilish» dan keyin klub bridge'i ma'lumot yubora olmaydi (401). Jadvalda holat 90 soniyadan keyin «Eskirgan», keyin «Ma'lumot yo'q» bo'ladi.
5. Stansiyani tasdiqlash yoki uzish jadvalda darhol aks etadi va keyingi yangilanishdan keyin ham saqlanadi.
6. Barcha xavfli amallar sababi bilan audit jurnalida ko'rinadi.
7. iCafe ulangan klubda GameClass kod tugmasi ko'rinmaydi yoki 409 sababini aniq tushuntiradi.
8. Ikkala til to'liq tarjima qilingan; 1366×768 ekranda gorizontal scroll yo'q.

## 6. Bosqichlar

| Bosqich | Tarkib |
|---|---|
| P1 | 2-bo'lim backend endpoint'lari (summary, ro'yxat, detail, patch, pairing-code, revoke, stations, sync) + 1.1, 1.2, 1.3 ekranlari |
| P2 | Sotuvchilar ro'yxatidagi filtr va badge (1.4), adopt-stations modali, audit jurnali sahifasi |
| Keyin | Bildirishnoma: klub 10 daqiqadan ko'p «Ma'lumot yo'q» bo'lsa support Telegram guruhiga xabar |

## 7. Sinov uchun

- **Lab backend:** `DB_NAME=manajoy_gc_e2e`, `REDIS_URL=redis://127.0.0.1:6379/9`, `uvicorn config.asgi:app --host 192.168.122.1 --port 8099`. Lab klubi: seller #1 «GameClass Lab Club», 25 PC.
- **Sozlash oynasi demo rejimi** (klub kompyuterisiz):
  ```
  Manajoy.GameClass.Bridge.exe --setup-demo
  ```
  Haqiqiy kod uchun lab VM'dagi «Manajoy (lab backend)» yorlig'i ishlatiladi.
- **Mavjud seller endpoint'lari** (mantiqni solishtirish uchun): `/api/seller/gaming/integration*`. Mobil ilova ekranlari: `seller_scanner/lib/screens/gameclass_connect_screen.dart`, `icafe_settings_screen.dart`.
