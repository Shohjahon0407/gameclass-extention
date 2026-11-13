# GameClass klubini Manajoy'da jonli ko'rsatish: ishga tushirish tartibi

Natija: Manajoy ilovasida klubning har bir kompyuteri `available` / `busy` / `offline` / `unknown` ko'rinadi. Band PC uchun qancha daqiqadan keyin bo'shashi ham ko'rinadi.

Ma'lumot yo'li: GameClass bazasi → bridge (klub kompyuteri, faqat o'qiydi) → `POST /api/integrations/gameclass/{seller_id}/snapshot` → Redis `icafe:live:{seller_id}` → ilovaning mavjud jonli holat endpointlari. Bron Manajoy'dan GameClass'ga yuborilmaydi.

## 1. Backend'ni chiqarish (bir marta)

`manajoy_backend/mana` dagi o'zgarishlar hali commit qilinmagan:

| Fayl | Nima |
|---|---|
| `integrations/models.py`, `integrations/migrations/0012_gamecenterintegration_provider_gameclass.py` | `provider = gameclass` |
| `fast_api/routers/integrations/gameclass_bridge.py`, `fast_api/main.py` | push endpointi (`X-Bridge-Key`) |
| `fast_api/services/integrations/gameclass/` | snapshot validatsiyasi, envelope, metadata sync |
| `integrations/management/commands/gameclass_bridge_key.py` | kalit yaratish/almashtirish, yoqish/o'chirish |
| `fast_api/services/integrations/icafe/sync_service.py` | `metadata_client`: GameClass uchun oxirgi push ustida sync |
| `fast_api/services/seller/gaming_integration.py` | seller "sync" va "yagona manba" GameClass'da ham ishlaydi |
| `fast_api/services/integrations/icafe/members.py` | klub akkaunti/to'ldirish faqat iCafe'da (GameClass klubida 500 o'rniga "mavjud emas") |
| `integrations/test_gameclass.py`, `pytest.ini` | testlar |
| `fast_api/services/integrations/gameclass/pairing.py` | **v0.3:** juftlash kodi (issue/redeem), `rotate_bridge_key`, bridge status |
| `fast_api/routers/integrations/gameclass_bridge.py` | **v0.3:** `POST /pair` (public, brute-force limit), `GET /{seller_id}/status` (`X-Bridge-Key`) |
| `fast_api/routers/seller/gaming_integration.py`, `fast_api/schemas/seller/gaming_integration.py` | **v0.3:** `POST /seller/gaming/integration/gameclass/pairing-code` (owner) |
| `fast_api/services/seller/gaming_integration.py` | **v0.3:** iCafe `connect` GameClass kalitini bosib ketmaydi (409) |
| `fast_api/services/integrations/gameclass/payload.py`, `icafe/normalizer.py` | **v0.3:** bridge `offline` deb yuborgan PC mijozlarga «bo'sh» emas, `offline` ko'rinadi |
| `fast_api/middleware.py`, `config/settings.py` | **v0.3:** `/pair` brute-force ro'yxatida; `GAMECLASS_SETUP_DOWNLOAD_URL` |
| `integrations/tests_gameclass_pairing.py`, `gameclass/tests/pairing_test.py`, `offline_test.py` | **v0.3:** testlar |
| `fast_api/services/integrations/gameclass/layout.py`, `fast_api/routers/integrations/gameclass_bridge.py` | **v0.4:** klub xaritasi bridge bilan sinxron: `GET`/`PUT /integrations/gameclass/{seller_id}/layout` (`X-Bridge-Key`). Faqat GameClass'ga bog'langan stansiyalarning `ResourceLayout` nuqtalari; versiya ziddiyatida 409. Migratsiya yo'q |
| `integrations/tests_gameclass_layout.py` | **v0.4:** testlar |

| `integrations/models.py`, `integrations/migrations/0016_gameclass_analytics.py` | **v0.9:** `GameClassDayReport` (har ish kuni hisoboti) va integratsiyada `analytics_*` maydonlari |
| `fast_api/services/integrations/gameclass/analytics_payload.py`, `analytics.py` | **v0.9:** hisobot payload validatsiyasi, davr yig'indisi (pure), qabul qilish va o'qish |
| `fast_api/routers/integrations/gameclass_bridge.py` | **v0.9:** `POST /integrations/gameclass/{seller_id}/analytics` (`X-Bridge-Key`; o'chirilgan bo'lsa 409) |
| `fast_api/routers/seller/gaming_analytics.py`, `fast_api/main.py` | **v0.9:** `GET /seller/gaming/analytics`, `/day`, `/export` (owner + manager) |
| `fast_api/routers/admin/gaming_integrations.py`, `services/admin/gaming_integrations.py`, `integrations/admin.py` | **v0.9:** admin: klub hisoboti, barcha klublar xulosasi, o'chirishda sabab; Django admin ro'yxati |
| `integrations/tests_gameclass_analytics.py`, `gameclass/tests/analytics_test.py` (+ `fixtures/analytics_push.json`) | **v0.9:** testlar |
| `docs/GAMECLASS_ANALYTICS_BUSINESS_APP.md`, `docs/GAMECLASS_ANALYTICS_ADMIN_PANEL.md` | **v0.9:** frontend hujjatlari |

Chiqarishdan keyin bajariladigan buyruq: `python manage.py migrate integrations`. (v0.9 uchun `0016_gameclass_analytics` ham shu bilan qo'llanadi; undan oldin prod'da `0014`/`0015` bormi — tekshiring). Celery yoki beat'ga o'zgarish kerak emas.

Muhitga qo'shing: `GAMECLASS_SETUP_DOWNLOAD_URL=<Telegram post havolasi>`. Bu havola Biznes ilovasidagi «Telegram'dan yuklab olish» tugmasida ochiladi; standart qiymati `https://t.me/manajoy_uz`.

**Diqqat:** `.gitignore` dagi `test_*.py` qoidasi `integrations/test_gameclass.py` ni yashiradi. Uni `git add -f` bilan qo'shing yoki `tests_gameclass.py` deb qayta nomlang. Yangi `tests_gameclass_pairing.py` bu qoidaga tushmaydi.

`seller_scanner` (ManaJoy Biznes) o'zgarishlari:
- yangi `lib/screens/gameclass_connect_screen.dart`;
- «Kompyuterlar holati» ekranida «GameClass orqali ulash» tugmasi;
- sozlamalarda GameClass rejimi;
- `api_service.dart`, `strings.dart` (uz/ru);
- `test/gameclass_pairing_test.dart`.

**v0.9 (klub hisoboti):** `lib/screens/gaming/gaming_analytics_screen.dart`, `gaming_day_timeline.dart`, `lib/models/gaming_analytics.dart`, `lib/core/strings_gaming.dart`, profil va «Kompyuterlar holati» dagi kirish tugmalari, `test/gaming_analytics_model_test.dart`.

**Tartib (v0.9):** avval backend (aks holda 0.9 bridge `404` oladi va 30 daqiqagacha kutib qayta urinadi — zarari yo'q), keyin bridge 0.9.0 va Biznes ilova. Bridge yangilangach birinchi 2 daqiqadan keyin oxirgi 7 kunni, so'ng GameClass'dagi butun tarixni yangisidan eskisiga 14 kunlik paketlarda yuboradi.

Ilova yangi versiya sifatida chiqarilishi kerak.

## 2. Klubni ulash (har klub uchun) — klub egasining o'zi

1. Klub Manajoy'da `seller_type=gaming` bo'lishi kerak. Unda iCafe integratsiyasi bo'lmasligi kerak: klubda faqat bitta jonli holat manbai bo'ladi.
2. Sozlash faylini (`field-kit/out/ManajoyGameClass-Setup-<ver>.exe`, ~45 MB) Manajoy Telegram kanaliga joylang. Post havolasini `GAMECLASS_SETUP_DOWNLOAD_URL` ga yozing.
3. Klub egasi `field-kit/FIELD-GUIDE.md` bo'yicha:
   - ilovada kod oladi;
   - faylni GameClass Server kompyuterida ochadi;
   - tekshiruv → baza → o'rnatish bosqichlaridan o'tadi;
   - kodni kiritadi.

   Kod ishlatilganda backend integratsiyani yaratadi (yoki kalitini almashtiradi) va jonli holatni **yoqadi**. Admin, `manage.py` yoki `--enable` kerak emas. Ilova ekrani ulanishni o'zi aniqlab, «Ulandi!» ni ko'rsatadi.
   - Zaxira yo'l (support): `python manage.py gameclass_bridge_key --seller-id <ID> [--enable]` va texnik kit (`install-service.ps1 -PushUrl`).
4. **PC'larni stansiyalarga bog'lash.** Bu birinchi push'dan keyin qilinadi:
   - Klubda Manajoy stansiyalari **yo'q** bo'lsa, birinchi push har GameClass PC uchun stansiya yaratib, o'zi bog'laydi.
   - Stansiyalar **bor** bo'lsa, klub egasi (owner tokeni bilan) quyidagilardan birini bajaradi:
     - nomi mos keladigan PC'larni tasdiqlaydi: `GET /api/seller/gaming/integration/stations`, keyin `POST .../stations/accept-suggestions` yoki `POST .../stations/{map_id}/confirm`;
     - katalogni GameClass'dan qayta quradi: `POST /api/seller/gaming/integration/adopt-stations`. Avval `{"confirm": false}` bilan rejani ko'radi, keyin `{"confirm": true}` yuboradi. Broni bor eski stansiyalar o'chirilmaydi, arxivlanadi.
   - Takliflar har push'dagi metadata sync'da yangilanadi. `adopt-stations` va `sync` esa oxirgi 5 daqiqadagi push ro'yxati ustida ishlaydi. Push kelmagan bo'lsa, "GameClass bridge'dan so'nggi 5 daqiqada ma'lumot kelmadi" xatosi chiqadi.
5. Tekshiring:
   - `GET /api/buyer/gaming/<ID>/live-status` → `state: ok`, `counts.total` = GameClass PC soni.
   - GameClass'da sessiya oching. Lab'da 4–6 soniyada `busy` va `free_in_minutes` chiqdi, yopilganda bir necha soniyada `available` bo'ldi.

## 3. Holatlar

| Ko'rinish | Ma'nosi |
|---|---|
| `state: stale` (90 s'dan eski) | Bridge yuborolmayapti: klubda internet yoki kompyuter o'chgan |
| hamma PC `unknown` | GameClass Server yopiq yoki operator kirmagan |
| `offline` | O'sha PC'ning GameClass client'i bilan aloqa yo'q |

## 4. O'chirish

- **Manajoy'da yashirish:** `gameclass_bridge_key --seller-id <ID> --disable`. Bridge yuborishda davom etadi, lekin javob 409 bo'ladi.
- **Kalitni almashtirish:** `gameclass_bridge_key --seller-id <ID>`. Eski kalit darhol ishlamay qoladi.
- **Klubda to'xtatish:** `install-service.ps1 -DisablePush`.

## Lab dalili

`gameclass-installer/lab-logs/acceptance/2026-10-06-live-status-e2e/`:
- GameClass GUI'da sessiya ochish/yopish;
- `busy.json`, `free.json` (buyer `live-status`);
- `club-detail-busy.json` (stansiyada `live_state: busy`).
