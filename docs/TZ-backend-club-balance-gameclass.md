# TZ: GameClass klublarida mijoz hisobi, balans va bron (backend)

**Kim uchun:** Manajoy backend jamoasi (`manajoy_backend/mana`, FastAPI + Django ORM).
**Holat:** loyiha. Qarorlarni klub egasi 2026-10-22 da tasdiqlagan (0-bo'lim).
**Bog'liq:**
- `docs/TZ-admin-panel-gameclass.md` — GameClass ulanishi;
- `GO-LIVE.md`;
- iCafe'dagi tayyor qismlar: `fast_api/services/integrations/icafe/members.py`, `topup.py`.

---

## 0. Kontekst va qarorlar

GameClass klublarida hozir faqat jonli holat ishlaydi: bridge PC holatini Manajoy'ga yuboradi. Klub hisobi va balansni to'ldirish **faqat iCafe** uchun bor. `members._integration_or_none` GameClass'ni chiqarib tashlaydi, ilova esa «mavjud emas» deydi.

**Maqsad:** mijoz GameClass klubida ham Manajoy orqali hisob ochsin, pul tashlasin va o'sha puldan klubda to'lasin. Admin mijozni va balansini **bridge'da** ko'radi va puldan **bridge'ning o'zidan** yechadi.

**Tasdiqlangan qarorlar:**

| # | Qaror |
|---|---|
| 1 | Pul oqimi iCafe'dagidek: to'lov Manajoy merchant'iga tushadi, **butun summa darhol klub hamyoniga** (`SellerWallet`) yoziladi, klubga `ClubTopupPayout` bilan o'tkaziladi |
| 2 | To'ldirishdan komissiya **0** |
| 3 | Yechish mijoz tasdig'isiz bo'ladi, lekin har safar mijozga **xabar** boradi |
| 4 | **Balans Manajoy'da yashaydi.** GameClass'ning `Accounts` jadvaliga hech narsa yozilmaydi. Admin seansni GameClass'da mehmon sifatida ochadi va pulni bridge orqali yechadi |
| 5 | Mijoz ishlatilmagan pulni **qaytarib olish so'rovini** yuboradi |
| 6 | Bridge mijozning **ismi va telefonini** ko'rsatadi (kim pul tashlaganini bilish uchun) |

**Qat'iy cheklov:** bridge GameClass bazasiga yozmaydi (bron yozish sinovi NO-GO bo'lgan, `LAB-STATUS.md`). Bu TZ'dagi barcha yozish amallari **bridge → Manajoy backend** yo'nalishida ketadi.

---

## 1. Ma'lumotlar modeli

### 1.1. `ExternalMemberAccount` — qayta ishlatiladi

Yangi jadval ochilmaydi. iCafe'dagi model, `ClubBalanceTopUp` FK, `/buyer/club-accounts` va admin hisobotlari tayyor. GameClass uchun farqlar:

| Maydon | iCafe | GameClass |
|---|---|---|
| `member_account` | iCafe login | `base_login(user)` (telefon raqamlari) — faqat unikallik uchun, hech qayerda login sifatida ishlatilmaydi |
| `password_encrypted` | Fernet parol | `''` (parol yo'q) |
| `external_member_id` | iCafe member_id | `''` |
| `missing_since` | iCafe'da topilmadi | ishlatilmaydi |

**Yangi maydonlar:**
- `balance` — `Decimal(12,2)`, default 0. Daftarning (1.2) joriy yig'indisi. Faqat 2-bo'limdagi servis funksiyalari `select_for_update` ostida yozadi.
- `held` — `Decimal(12,2)`, default 0. Bron uchun band qilingan summa (5-bo'lim). Mavjud balans = `balance − held`.

`balance` va `held` faqat GameClass akkauntlarida ishlatiladi. iCafe balansi avvalgidek iCafe'dan o'qiladi.

**Migratsiya:**
- `password_encrypted` ga `blank=True`;
- `CheckConstraint(balance >= 0)`;
- `CheckConstraint(held >= 0)`;
- `CheckConstraint(held <= balance)`.

### 1.2. `ClubBalanceEntry` — balans daftari (yangi, `integrations`)

Har bir pul harakati alohida yozuv bo'ladi. Yozuvlar **o'zgartirilmaydi va o'chirilmaydi**; xato faqat teskari yozuv bilan tuzatiladi.

| Maydon | Tur | Izoh |
|---|---|---|
| `account` | FK `ExternalMemberAccount`, PROTECT | |
| `kind` | choice | `topup`, `debit`, `debit_reversal`, `hold`, `hold_release`, `hold_capture`, `refund` |
| `amount` | Decimal(12,2) | ishorali: `balance`ni oshirsa `+`, kamaytirsa `−`. `hold`/`hold_release` uchun `held` o'zgaradi, `balance` esa 0 |
| `balance_after`, `held_after` | Decimal | yozuvdan keyingi holat (audit va tarix ekrani uchun) |
| `topup` | FK `ClubBalanceTopUp`, null | `kind=topup` |
| `order` | FK `orders.Order`, null | bron bilan bog'liq yozuvlar |
| `refund_request` | FK 1.3, null | `kind=refund` |
| `reverses` | FK self, null, unique | `debit_reversal` qaysi `debit`ni qaytarayotgani |
| `source` | choice | `system`, `bridge`, `seller_app`, `admin`, `buyer` |
| `operator_name` | char(80), blank | bridge'dan kelgan kassir nomi (erkin matn) |
| `machine_name` | char(64), blank | bridge kompyuteri |
| `pc_name` | char(32), blank | ixtiyoriy: qaysi PC uchun yechildi |
| `comment` | char(200), blank | |
| `idempotency_key` | char(64), null, **unique** | bridge har bir yechish uchun UUID yuboradi |
| `created_by` | FK User, null | seller ilovasi va admin uchun |
| `created_at` | datetime | |

**Indekslar:** `(account, -created_at)` va `(account__integration, -created_at)` (klub jurnali).

### 1.3. `ClubBalanceRefundRequest` — qaytarish so'rovi (yangi)

| Maydon | Izoh |
|---|---|
| `account` | FK |
| `amount` | so'ralgan summa, ≤ mavjud balans |
| `status` | `pending` → `approved` → `paid`; yoki `rejected`, `cancelled` (mijoz o'zi bekor qildi) |
| `reason` | mijoz izohi, ixtiyoriy |
| `card_hint` | qaysi kartaga qaytariladi (oxirgi 4 raqam, ixtiyoriy) |
| `decided_by`, `decided_at`, `decision_note` | |
| `paid_by`, `paid_at`, `payout_reference` | admin pulni o'tkazgach to'ldiriladi |

Bir akkauntda bir vaqtda faqat bitta `pending` so'rov bo'ladi (partial unique constraint).

---

## 2. Servis qatlami

Yangi modul: `fast_api/services/integrations/gameclass/club_balance.py`. Barcha pul amallari shu yerdan o'tadi.

**Umumiy qoidalar:**
- Har bir funksiya `transaction.atomic()` ichida `ExternalMemberAccount` qatorini `select_for_update()` bilan oladi, tekshiradi, `ClubBalanceEntry` yozadi va `balance`/`held` ni yangilaydi.
- Summa butun so'm bo'ladi: `amount > 0`, kasr qismi yo'q (`validate_amount` dan foydalaning).
- Klub GameClass provider'li bo'lishi va `integration.topup_enabled = True` bo'lishi shart.

| Funksiya | Nima qiladi |
|---|---|
| `credit_topup(topup)` | `+amount`, `kind=topup`. Idempotent: bitta `topup` uchun bitta yozuv |
| `debit(account, amount, *, idempotency_key, source, operator_name, machine_name, pc_name, comment)` | `amount ≤ balance − held` bo'lmasa `InsufficientBalance`. Shu `idempotency_key` bilan yozuv bor bo'lsa, **o'sha yozuv qaytariladi** (qayta yechilmaydi) |
| `reverse_debit(entry, *, source, ...)` | Faqat `kind=debit` uchun, yaratilganidan keyin **10 daqiqa** ichida va bir marta (`reverses` unique) |
| `hold(account, order, amount)` / `release(order)` / `capture(order)` | bron uchun (5-bo'lim) |
| `refund(request)` | `−amount`, `kind=refund`; 4-bo'limda batafsil |

Har bir yozuvdan keyin `transaction.on_commit` orqali xabar (6-bo'lim) yuboriladi va bridge'ning `club_version` i oshiriladi (3.1).

---

## 3. Bridge API (`X-Bridge-Key`)

**Joylashuv:** `fast_api/routers/integrations/gameclass_bridge.py`, prefiks `/integrations/gameclass/{seller_id}`.

**Auth:** mavjud `_load_integration` + `_check_key` (`ingest.py`) bilan bir xil: kalit noto'g'ri bo'lsa yoki provider GameClass bo'lmasa — 401. `topup_enabled=False` bo'lsa, 3.2–3.6 endpointlari `409 {code: "club_balance_disabled"}` qaytaradi.

### 3.1. Snapshot javobiga versiya qo'shiladi

Bridge har ~15 soniyada `POST /snapshot` yuboradi. Uning javobiga quyidagi maydon qo'shiladi:

```json
"club": { "members_version": "a1b2c3", "bookings_version": "d4e5f6", "pending_refunds": 1 }
```

Versiya — Redis kaliti `gameclass:club_ver:{seller_id}:{members|bookings}`. U har bir balans yozuvi, yangi akkaunt va bron o'zgarishida oshadi. Bridge versiya o'zgargandagina ro'yxatni qayta so'raydi, shuning uchun alohida WebSocket kerak emas. Kechikish ≤ 15 s.

### 3.2. `GET /members`

**Parametrlar:** `q` (telefon raqamlari, ism yoki uning qismi), `limit` (≤ 100, default 50), `cursor`.

**Javob:**
```json
{
  "items": [{
    "account_id": 812,
    "name": "Ali Valiyev",
    "phone": "+998901234567",
    "balance": 50000, "held": 0, "available": 50000,
    "last_topup_at": "2026-10-22T14:03:00+05:00",
    "created_at": "2026-10-20T10:00:00+05:00"
  }],
  "next_cursor": null,
  "currency": "UZS",
  "version": "a1b2c3"
}
```

Tartib: oxirgi harakat bo'yicha, eng yangisi tepada.

### 3.3. `GET /members/{account_id}`

Akkaunt va oxirgi 30 ta `ClubBalanceEntry` qaytariladi: tur, summa, keyingi balans, vaqt, operator, PC va izoh. Bu admin «kim, qachon, qancha» degan savolga javob topishi uchun.

### 3.4. `POST /members/{account_id}/debit` — balansdan yechish

```json
{ "amount": 24000, "idempotency_key": "6f1c…uuid", "operator_name": "Kassir 1",
  "machine_name": "CYBER-ADMIN", "pc_name": "7", "comment": "2 soat" }
```

| Kod | Holat |
|---|---|
| 200 | `{entry, balance, available}`. Bir xil `idempotency_key` bilan qayta so'ralsa, o'sha natija qaytadi |
| 404 | akkaunt bu klubga tegishli emas |
| 409 `insufficient_balance` | `available` javobda bo'ladi |
| 422 | summa noto'g'ri |

### 3.5. `POST /balance-entries/{entry_id}/reverse` — xato yechimni qaytarish

Faqat 10 daqiqa ichida ishlaydi. Muddat o'tgan bo'lsa `409 reverse_window_closed`, oldin qaytarilgan bo'lsa `409 already_reversed`. Mijozga «qaytarildi» xabari boradi.

### 3.6. Bronlar (5-bo'lim bilan birga, P3)

- **`GET /bookings?from=&to=`** — shu klubning `confirmed`/`pending` gaming bronlari.
  - Har bir bron uchun maydonlar: `order_id`, `status`, `start`/`end`, PC'lar, mijoz (ism, telefon), `paid_from_balance`, `held_amount`.
  - PC'lar ikki xil nom bilan beriladi: `resource_name` va GameClass PC nomi (`ExternalStationMap.external_name`).
- **`POST /bookings/{order_id}/accept`**, **`/reject {reason}`**, **`/no-show`**, **`/complete`** — mavjud seller servis funksiyalarini chaqiradi (`routers/seller/orders.py` dagi mantiq). `source=bridge` deb belgilanadi.

### 3.7. Qaytarish so'rovlari (P2)

- `GET /refund-requests?status=pending`
- `POST /refund-requests/{id}/approve` va `/reject {note}`

### 3.8. Cheklovlar

- **Rate limit:** kalit bo'yicha 120 so'rov/daqiqa, `debit` uchun alohida 30/daqiqa.
- **Loglarga** telefon raqami va ism **yozilmaydi**, faqat `account_id`.
- **Bridge ma'lumotni saqlamaydi:** u telefonni faqat ekranda ko'rsatadi, diskka va hisobot zip'iga yozmaydi. Bu bridge TZ'sida ham yoziladi.

---

## 4. Pul oqimi

### 4.1. To'ldirish

Mavjud `topup.create_topup` va `credit_topup` kengaytiriladi.

**`members._integration_or_none`**
- GameClass klubini ham qaytaradi.
- Chaqiruvchilar provider bo'yicha tarmoqlanadi.

**`members.create_account`, GameClass uchun**
- iCafe'ga murojaat qilinmaydi.
- Yozuv 1.1-jadvaldagidek yaratiladi.
- `members_version` oshiriladi.
- Javobda parol bo'lmaydi va `kind: "manajoy_balance"` qaytadi.

**`members.get_balance`, GameClass uchun**
- Balans bazadan, ya'ni `balance`/`held` dan olinadi; kesh yo'q.
- Javob formati o'zgarmaydi. Yangi maydon: `held`.

**`create_topup`, GameClass uchun**
- `_ensure_member_id` qadami tashlab ketiladi.
- Qolgani o'zgarishsiz: `Order(order_type='topup')`, komissiya 0, Payme/Click.

**`credit_topup`, GameClass uchun**
- Tashqi chaqiruv bo'lmaydi: `club_balance.credit_topup(topup)`, keyin `_mark_credited(topup)`.
- `_mark_credited` hamyon yozuvini qiladi (`add_order_income`, butun summa) va xabarlarni yuboradi. Bular o'zgarmaydi.
- Qayta urinish va `comment_tag` qidiruvi GameClass uchun kerak emas.

**Natija:** to'lov kelgandan keyin 1–2 soniyada balans oshadi va bridge buni keyingi push'da (≤ 15 s) ko'radi.

### 4.2. Yechish

**Hamyonga tegilmaydi.** Pul to'ldirish paytidayoq klub hamyoniga o'tgan (qaror 1). Yechish faqat mijozning shu klubdagi daftaridagi harakat. `Order` yaratilmaydi.

### 4.3. Qaytarish so'rovi

1. **Mijoz so'rov yuboradi** (7.3). Summa `available` dan oshmasligi kerak. So'rov `pending` holatida turganda shu summa **band qilinadi** (`held += amount`), shunda mijoz uni klubda ishlatib qo'ya olmaydi. Mijoz so'rovni bekor qilsa, band bo'shatiladi.
2. **Klub tasdiqlaydi yoki rad etadi.** Buni bridge (3.7) yoki ManaJoy Biznes (8) orqali qiladi. Klub 72 soat ichida javob bermasa, so'rov admin navbatiga tushadi.
3. **Tasdiqlansa (`approved`):**
   - balansdan `refund` yozuvi (`−amount`) tushadi va band bo'shatiladi;
   - klub hamyonidan `WalletTransaction(type='refund', −amount)` yechiladi, chunki pul klubda edi;
   - hamyonda pul yetmasa, manfiy qoldiq — klubning qarzi sifatida ko'rinadi.
4. **Admin pulni mijozga o'tkazadi** (Payme/Click kabineti yoki karta). Shundan keyin `paid` qilib, `payout_reference` ni to'ldiradi. Bu mavjud qo'lda qaytarish tartibiga o'xshaydi (`refund.py`).
5. **Rad etilsa:** band bo'shatiladi, mijozga sabab bilan xabar boradi.

---

## 5. Bronni klub balansidan to'lash (P3)

Mavjud `book_stations` / `quote` (`fast_api/services/buyer/gaming.py`) kengaytiriladi.

**To'lov usuli**
- `Order.PAYMENT_METHOD_CHOICES` ga `('club_balance', 'Klub balansi')` qo'shiladi.
- Faqat GameClass klubida va mijozda akkaunt bo'lsa ruxsat etiladi.

**`quote` javobi** ikki yangi maydon oladi:
- `club_balance_available`;
- `can_pay_from_balance` — mavjud balans `prepaid_amount` dan katta yoki teng bo'lsa `true`.

**`book` paytida**
- `prepaid_amount` (split qoidasi bo'yicha depozit) **band qilinadi** (`hold`). Pul hali yechilmaydi.
- Order to'g'ridan-to'g'ri `pending` holatiga o'tadi; `auto_accept_orders` yoqilgan bo'lsa `confirmed`. `awaiting_payment` bosqichi va `payment_url` bo'lmaydi.

**Bron holati o'zgarganda**

| Hodisa | Balans |
|---|---|
| Klub rad etdi / mijoz muddatida bekor qildi | `release` — band bo'shaydi |
| `completed` (mijoz keldi) | `capture` — band summa yechiladi (`hold_capture`). Qolgan seans pulini kassir oddiy `debit` bilan yechadi |
| `no_show` yoki bekor qilish muddati o'tgach bekor qilindi | `capture` — mavjud depozit qoidasi (`cancel_policy.py`) bilan bir xil |
| Bron muddati o'tib, hech kim holatni o'zgartirmadi | mavjud avto-yakunlash yoki no-show vazifasiga ulanadi — band osilib qolmasligi kerak |

**Hamyon:** `capture` hamyonga yozilmaydi (pul allaqachon klubda). Shu sababli `club_balance` bilan to'langan orderlar uchun `add_order_income` **chaqirilmasligi** kerak. Order yakunlanganda ikki marta yozilib qolmasligini test bilan tekshiring.

**GameClass bu brondan bexabar.** Ogohlantirishni bridge o'zi chiqaradi: u bronni (3.6) va PC holatini taqqoslaydi. Backend'dan qo'shimcha ish talab qilinmaydi.

`cancel_icafe_booking` hali yo'q va GameClass uchun kerak emas. Bu TZ unga tegmaydi.

---

## 6. Xabarlar

Barchasi `NotificationService` orqali yuboriladi: inbox + FCM, uz/ru/en matnlari bilan, xuddi `notify_club_topup` kabi.

| Hodisa | Kimga | Matn (uz) | `data.type` |
|---|---|---|---|
| To'ldirish tushdi | mijoz, klub | mavjud `notify_club_topup` | `club_topup` |
| Yechildi | mijoz | «{klub}: −24 000 so'm. Qoldiq: 26 000 so'm» | `club_balance_debit` |
| Yechim qaytarildi | mijoz | «{klub}: 24 000 so'm hisobingizga qaytarildi» | `club_balance_reversal` |
| Bron uchun band qilindi / bo'shatildi / yechildi | mijoz | bron xabarlariga qo'shimcha qator | `club_balance_hold` |
| Qaytarish so'rovi keldi | klub (egasi, menejer) | «Ali qaytarish so'radi: 30 000 so'm» | `club_refund_request` |
| So'rov tasdiqlandi / rad etildi / to'landi | mijoz | | `club_refund_status` |

Yechish xabari tasdiq so'ramaydi (qaror 3). Xabarni bosganda ilovada shu klubning balans tarixi (7.2) ochiladi.

---

## 7. Mijoz API (`/buyer`)

Mavjud endpointlar GameClass klublari uchun ham ishlashi kerak. Javob formati o'zgarmaydi, faqat maydonlar qo'shiladi.

**7.1. Mavjud endpointlar**
- `GET/POST /gaming/{seller_id}/club-account`:
  - GameClass klubida `available=true` qaytaradi;
  - akkaunt maydonida `kind: "manajoy_balance"` bo'ladi, `password` esa `null`.
- `GET /club-accounts` — yangi `kind` maydoni bilan.

**7.2. Balans va tarix**
- `GET /gaming/{seller_id}/club-account/balance` — `balance`, `held`, `available`. GameClass uchun tarmoqqa chiqilmaydi, ma'lumot bazadan olinadi.
- `GET /gaming/{seller_id}/club-account/history?limit=&cursor=` (**yangi**) — `ClubBalanceEntry` lar ro'yxati.
  - Mijozga `operator_name`, `machine_name` ko'rsatilmaydi.
  - `pc_name` va `comment` ko'rsatiladi.

**7.3. Qaytarish so'rovlari (yangi)**
- `POST /gaming/{seller_id}/club-account/refund-requests` — `{amount, reason?, card_hint?}`.
  - `409 refund_pending` — ochiq so'rov allaqachon bor.
  - `409 insufficient_balance` — summa mavjud balansdan katta.
- `GET .../refund-requests` — so'rovlar ro'yxati.
- `DELETE .../refund-requests/{id}` — faqat `pending` holatidagi so'rovni bekor qiladi.

**7.4. Bron**
- `quote` va `book` endpointlari `payment_method: "club_balance"` ni qabul qiladi (5-bo'lim).

---

## 8. Klub API (`/seller`, ManaJoy Biznes)

Ruxsat — egasi va menejer; operatorga faqat ko'rish.

- `GET /seller/gaming/members?q=&cursor=` — mijozlar va balanslar (3.2 bilan bir xil ma'lumot).
- `GET /seller/gaming/balance-entries?from=&to=&kind=&account_id=` — klub jurnali: kim, qachon, qancha, qaysi kassir. Bu yechishda tasdiq bo'lmagani uchun kassir ustidan nazorat vositasi.
- `GET /seller/gaming/balance-summary` — mijozlar balanslari yig'indisi, bu davrdagi to'ldirishlar va yechimlar.
- `GET /seller/gaming/refund-requests` va `POST .../{id}/approve|reject`.
- Mavjud `/seller/gaming/topups*` GameClass klublari uchun ham ishlaydi; o'zgarish kerak emas, faqat test bilan tasdiqlansin.

## 9. Admin API

- Mavjud `admin/club_topups` GameClass to'ldirishlarini ham ko'rsatadi; filtrga `provider` qo'shiladi.
- **Yangi:** `GET /admin/club-refund-requests` (holat filtri bilan), `POST .../{id}/approve|reject` (72 soatdan oshganlar uchun), `POST .../{id}/mark-paid {payout_reference}`.
- **Yangi:** `GET /admin/club-balance-entries` — klub va mijoz bo'yicha jurnal, CSV eksport bilan.

---

## 10. Chegaraviy holatlar

| Holat | Xatti-harakat |
|---|---|
| Bir yechish ikki marta yuborildi (tarmoq uzildi, bridge qayta yubordi) | `idempotency_key` tufayli bitta yozuv bo'ladi |
| Ikki kassir bir vaqtda yechdi | `select_for_update`; ikkinchisi yetmasa 409 |
| Klub bridge'ni o'chirdi yoki kalitni bekor qildi | balanslar saqlanadi; mijoz balansni ko'radi va qaytarishni so'ray oladi; to'ldirish `topup_enabled=False` bo'lsa yopiladi |
| Klub provider'ni iCafe'ga almashtirmoqchi | GameClass akkauntlarida `balance > 0` bo'lsa almashtirish **rad etiladi** (admin oldin balanslarni hal qiladi) |
| Mijoz akkauntini o'chirmoqchi | `balance > 0` bo'lsa avval qaytarish so'rovi |
| Mijoz Manajoy profilini o'chirdi | `CASCADE` emas — `ExternalMemberAccount.user` FK uchun balansli akkauntlarda o'chirish bloklanadi yoki admin navbatiga tushadi (hozir `CASCADE`, **o'zgartirish kerak**) |
| KRW klublar | `ensure_online_payment_allowed` hozirgidek bloklaydi |

---

## 11. Bosqichlar

| Bosqich | Tarkib | Bridge'dan nima kerak |
|---|---|---|
| **P1** | 1.1, 1.2, 2 (`debit`, `reverse_debit`), 3.1–3.5, 4.1–4.2, 6 (yechish xabarlari), 7.1–7.2, 8 (`members`, `balance-entries`) | «Mijozlar» bo'limi va «Balansdan yechish» oynasi |
| **P2** | 1.3, 3.7, 4.3, 7.3, 8 (`refund-requests`), 9 | Qaytarish so'rovlari ro'yxati |
| **P3** | 5, 3.6, 7.4 | «Bronlar» bo'limi va xaritadagi bron belgisi |

## 12. Qabul mezonlari (testlar)

1. **To'ldirish:** GameClass klubida akkaunt yaratiladi, Payme sandbox orqali 50 000 to'lanadi.
   - Natija: `balance=50000`, bitta `topup` yozuvi, klub hamyonida +50 000, mijoz va klubga xabar.
2. **Yechish:** bridge kaliti bilan `debit 24000` yuboriladi.
   - Natija: `balance=26000`, mijozga xabar.
   - Shu `idempotency_key` bilan qayta yuborilsa, balans o'zgarmaydi.
3. **Mablag' yetmasligi:** `debit 30000` yuboriladi.
   - Natija: 409 `insufficient_balance`, javobda `available=26000`.
4. **Qaytarish (yechimni bekor qilish):**
   - 10 daqiqa ichida `reverse` — balans tiklanadi.
   - 11-daqiqada — 409.
   - Ikkinchi marta — 409.
5. **Kalit tekshiruvi:**
   - Boshqa klubning kaliti bilan `GET /members` — 401.
   - Boshqa klub akkauntiga `debit` — 404.
6. **Parallel yechish:** 20 ta parallel `debit 5000` 50 000 balansda.
   - Natija: 10 tasi o'tadi, 10 tasi 409; `balance=0`, manfiy qoldiq yo'q.
7. **Qaytarish so'rovi (P2):**
   - `refund-request 20000` → band qilinadi, bridge'da ham bu summa ishlatib bo'lmaydi.
   - `approve` → `refund` yozuvi va hamyondan −20 000.
   - `mark-paid` → mijozga xabar.
8. **Bron (P3):** `club_balance` bilan bron qilinadi.
   - Bron paytida: `held` oshadi, `payment_url` yo'q.
   - Rad etilganda: `held` bo'shaydi.
   - `completed` bo'lganda: `hold_capture` yoziladi, `add_order_income` **chaqirilmaydi**.
9. **iCafe regressiyasi:** iCafe to'ldirish va akkaunt testlari (`tests/...icafe...`) o'zgarishsiz o'tadi.
10. **Shaxsiy ma'lumot:** loglarda telefon va ism uchramaydi. Buni log satrlarini tekshiruvchi test qiladi.

## 13. Ochiq savollar (P2/P3 dan oldin hal qilinsin)

1. Qaytarish so'rovini **klub** tasdiqlaydimi yoki to'g'ridan-to'g'ri **admin**mi? TZ'dagi taklif: klub, 72 soatdan keyin admin.
2. Bron uchun band qilinadigan summa — split'dagi depozitmi yoki bronning **to'liq** narximi?
3. Yechimlar uchun kunlik limit (masalan, bir kunda bir mijozdan 500 000 so'mdan ortiq yechilmasin) kerakmi?
