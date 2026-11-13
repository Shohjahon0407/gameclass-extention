# TZ: ManaJoy Biznes — «Klub hisoboti» va klub haqida bildirishnomalar (GameClass klublari)

**Kim uchun:**
- ManaJoy Biznes ilovasi jamoasi (`seller_scanner`, Flutter) va dizayner;
- 6-bo'lim — backend jamoasi (`manajoy_backend/mana`).

**Holat:** loyiha, 2026-10-27.

**Bog'liq hujjatlar:**
- `manajoy_backend/mana/docs/GAMECLASS_ANALYTICS_BUSINESS_APP.md` — hisobot API'si (tayyor, maydonlar tavsifi shu yerda);
- bridge 0.9.0 — ma'lumotni yuboradigan dastur.

**Hozirgi holat:**
- `seller_scanner` da birinchi variant bor: `lib/screens/gaming/gaming_analytics_screen.dart`, `gaming_day_timeline.dart`. Bu TZ uni yakuniy ko'rinishga keltiradi.
- Bildirishnomalar (5–6-bo'limlar) — yangi ish.

---

## 0. Nima va nega

Klub egasi doim klubda bo'lmaydi. U telefondan quyidagilarni bilib turishi kerak:
- **hozir** klubda nima bo'lyapti: nechta kompyuter band, klub dasturi ishlayaptimi;
- **bugun / kecha / shu hafta / shu oy** qancha pul tushdi: kompyuter vaqti va bar;
- **qaysi kompyuter qachon, qancha vaqt band** bo'lgan, qaysilari bo'sh turibdi;
- **kassirlar** qancha ishladi, smena qachon yopildi va kassada qancha pul bo'ldi;
- muhim hodisalar haqida **bildirishnoma**: kunlik xulosa, smena yopildi, klub dasturi o'chib qoldi, qarzli seans.

Ma'lumotni klubdagi GameClass bridge yuboradi: jonli holat 15 soniyada, kunlik hisobot 10 daqiqada bir yangilanadi. Bridge'ni o'rnatish va ulash — alohida jarayon, u tayyor.

## 1. Kimga ko'rinadi

Bo'lim faqat quyidagi **hamma shartlar** bajarilganda ko'rinadi:

| Shart | Qayerdan bilinadi |
|---|---|
| Biznes turi — kompyuter klubi | `session.isGaming` |
| Klub **GameClass** orqali ulangan | `GET /seller/gaming/integration` → `provider == "gameclass"` |
| Rol — **egasi yoki menejer** | `session.canManageOrders` (pul ko'rsatiladigan boshqa joylardagi bilan bir xil) |

- **Operator (staff):** bo'lim, tab va pul haqidagi bildirishnomalar umuman ko'rinmaydi. Backend ham `403` qaytaradi.
- **iCafe klubi yoki integratsiyasi yo'q klub:** bo'lim ko'rinmaydi.
  - Faqat bitta istisno: profilda kichik reklama-karta. «GameClass'ni ulasangiz, klub hisoboti shu yerda bo'ladi» → mavjud `GameClassConnectScreen`. Karta faqat integratsiya umuman **yo'q** bo'lsa ko'rinadi.
- **Yoqish/o'chirish:** faqat **egasi** hisobot yuborishni yoqib-o'chira oladi va bildirishnoma sozlamalarini o'zgartira oladi. Menejer o'z bildirishnomalarini o'chira oladi (6.3).

## 2. Navigatsiya

1. **Pastki menyu — «Hisobot» tabi** (asosiy kirish).
   - GameClass klubida egasi va menejer uchun «Skaner» o'rniga **«Hisobot»** (ikonka `insights`). Salon uchun qilingan bilan bir xil yechim (`home_screen.dart`, `SalonAnalyticsScreen`).
   - QR skaner «Bronlar» sahifasining tepasidagi tugmaga ko'chadi (salondagidek).
   - Tab ustida badge (qizil nuqta) ko'rinadi: klub dasturi o'chiq bo'lsa yoki hisobot 3 soatdan beri kelmasa.
2. **Profil** → «Klub hisoboti» qatori (zaxira kirish).
3. **«Kompyuterlar holati» ekrani** → app bar'da «Hisobot» ikonkasi.
4. **Bildirishnomadan** — bosilganda kerakli joy ochiladi (5-bo'limdagi «Ochiladi» ustuni).

Ekran ichida yuqorida 5 ta tab bor:

**Umumiy · Kompyuterlar · Kun jadvali · Bar · Xodimlar**

Tab va davr tanlovi saqlanib qoladi (`SharedPreferences`). Ilova qayta ochilganda o'sha joydan davom etadi.

## 3. Ekranlar

Umumiy talablar:
- Pull-to-refresh hamma tabda.
- Ma'lumot kelguncha skeleton (karta shaklidagi kulrang bloklar). Spinner faqat tugma ichida.
- Tarmoq xatosi — kontent o'rnida `ErrorState` + «Qayta urinish». Eski ma'lumot bo'lsa, u qoladi va tepada kichik sariq «Yangilab bo'lmadi» qatori chiqadi.
- 320 px kenglikda ham hech narsa sig'may qolmasin. Raqamlar `FittedBox` / qisqartma bilan sig'adi.

### 3.1. Sarlavha va «Hozir» kartasi (har tabda tepada)

```
┌──────────────────────────────────────────┐
│ ←  Klub hisoboti            ⤓  ⋮        │  ⤓ = Excel, ⋮ = sozlamalar
│    Stark gaming                          │
├──────────────────────────────────────────┤
│ ● Jonli · hozir 18 / 24 band   75%       │  ← «Hozir» kartasi
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░                    │     (gradient, oq matn)
│ Bugun hozirgacha: 2 450 000 so'm  ▲ 8%   │
│ Yangilandi 2 daqiqa oldin                │
└──────────────────────────────────────────┘
```

**«Hozir» kartasi** — ilovaning brend gradienti (`AppTheme` dagi asosiy gradient), oq matn, `radius 20`.
- **Band soni:** `GET /seller/gaming/integration` → `live.counts`. Kartada `busy / (available + busy)` va progress chiziq. Ekran ochiq turganda **30 soniyada** yangilanadi.
- **«Bugun hozirgacha»:** `GET /seller/gaming/analytics?date_from=<bugun>&date_to=<bugun>` → `money.kassa`. Yonidagi ▲/▼ — o'tgan haftaning **shu kuni, shu soatgacha** bilan solishtiriladi (6.4, `today` bloki). Ma'lumot bo'lmasa ko'rsatilmaydi.
- **Holat nuqtasi va matni:**

| Holat | Belgi | Matn |
|---|---|---|
| live `ok` | yashil, pulsatsiya | «Jonli» |
| live `stale` | sariq | «Ma'lumot kechikyapti» |
| live yo'q / `server_alive=false` | qizil | «Klub dasturi javob bermayapti» |

  Qizil holatda karta qizil-ohangli fonga o'tadi va «Nima qilish kerak?» havolasi chiqadi. U pastki varaq (bottom sheet) ochadi: «GameClass Server ochiqligini tekshiring, kompyuter yoqilganmi, internet bormi».

### 3.2. Davr tanlash (Umumiy, Kompyuterlar, Bar, Xodimlar)

```
[ Bugun ][ Kecha ][ 7 kun ][ Oy ][ Yil ][ 📅 ]      ← SegmentedPills, gorizontal scroll
20.10 — 26.10.2026 · ish kuni 06:00 dan          ← kichik kulrang matn
```

- **«Bugun»** = joriy **ish kuni**. Ish kuni `day_start_hour` dan boshlanadi (odatda 06:00). Soat 03:00 da «Bugun» hali kechagi kechqurun boshlangan kun bo'ladi.
- **«Oy»** — joriy oy boshidan bugungacha. **«Yil»** — yil boshidan.
- **📅** — sana oralig'i tanlash: ko'pi bilan 366 kun, kelajak sanalar yopiq.
- **Guruhlash avtomatik:**
  - Bugun yoki Kecha → soatlik grafik;
  - ≤ 62 kun → kunlik;
  - Yil → oylik.

### 3.3. «Umumiy» tab

```
┌───────────────────┐┌───────────────────┐
│ Kassa jami        ││ Kompyuter vaqti   │
│ 31 290 000        ││ 19 333 000        │
│ so'm      ▲ 1,4%  ││ so'm      ▲ 0,2%  │
└───────────────────┘└───────────────────┘
┌───────────────────┐┌───────────────────┐
│ Bar               ││ Band soatlar      │
│ 12 877 000 ▼ 1,2% ││ 2 394 s   ▼ 4,7%  │
└───────────────────┘└───────────────────┘
┌───────────────────┐┌───────────────────┐
│ O'rtacha bandlik  ││ Seanslar          │
│ 61,8%  ▼ 0,5 p.p. ││ 1 141 · o'rt. 2s5d│
└───────────────────┘└───────────────────┘
┌───────────────────┐┌───────────────────┐
│ O'rtacha chek     ││ Eng ko'p band     │
│ 28 422 so'm       ││ 23 / 24 · 21.10   │
└───────────────────┘└───────────────────┘

Tushum                          ■ PC vaqti ■ Bar
 █  █  █  █  █  █  ▄               (ustma-ust ustunlar)
20 21 22 23 24 25 26

Band soatlar (chiziq yoki ustun)

Eng faol vaqtlar          (7 qator × 24 soat issiqlik xaritasi)

Kassa tarkibi · Zonalar · Seanslar va bronlar · Tariflar
```

**KPI kartalari** — 2 ustunli grid, `KpiCard` (`salon_kit.dart`).
- «Kassa jami» kartasi ajralib turadi: och ko'k fon va ingichka ko'k chegara.
- **Delta (▲/▼):** `previous` bilan solishtiriladi.
  - Foiz: `(joriy − oldingi) / oldingi`, bir xona kasr (< 10% bo'lsa).
  - Bandlik uchun **foiz punkt** («p.p.»).
  - Oldingi = 0 bo'lsa → «yangi».
  - Yashil ▲ / qizil ▼ (`DeltaChip`). Ostida kichik izoh: «o'tgan shunday davrga nisbatan».
- Karta bosilsa → tegishli grafikka scroll qiladi (masalan «Bar» → «Bar» tabi).

**Grafiklar** (paket qo'shmasdan — `SimpleBarChart` / `CustomPainter`):
- **Tushum:**
  - ustma-ust ustun: pastda «PC vaqti» (ko'k `#1B54E4`), ustida «Bar» (to'q sariq `#EB6834`), orada 2 px oq oraliq;
  - ustun tepasi 4 px yumaloq;
  - Y o'qi qisqartma bilan: «2 mln», «850 ming»;
  - ustun bosilsa — tooltip-varaq: sana, PC vaqti, bar, kassa, seanslar.
- **Band soatlar:** bitta rang (ko'k), xuddi shu o'q va bosish qoidalari.
- **Eng faol vaqtlar:**
  - 7 qator (Du…Ya) × 24 ustun;
  - soatlar ish kuni boshidan: 06, 07 … 05;
  - bitta ko'k rang 8 pog'onasi, eng och = «bo'sh»;
  - tagida shkala «0 … 21 PC»;
  - katakcha bosilsa: «Juma 21:00 — o'rtacha 18 / 24 PC band»;
  - telefonda gorizontal scroll yoki 24 soatni siqib 12 px katak — dizayner tanlaydi, matn o'qilishi shart.
- Grafik ostida kichik legend; ranglar hech qachon yolg'iz ma'lumot tashuvchisi bo'lmasin.

**«Kassa tarkibi» kartasi** (kv-ro'yxat):
- PC vaqti;
- Bar (alohida to'langan);
- Bar (seans hisobidan);
- Chop etish;
- Akkauntlarga to'ldirildi;
- − Qaytarildi;
- **Kassa jami** — qalin, tepasida chiziq;
- «Bar (akkaunt balansidan, kassaga kirmaydi)» — kulrang, alohida qator.

Ostida izoh: «GameClass smena hisobotidagi «Итого» bilan bir xil hisoblanadi».

**Zonalar:** har zona — nom, PC soni, bandlik foizi, ingichka progress chiziq.

**Seanslar va bronlar** (kv-ro'yxat):
- seanslar, o'rtacha davomiyligi, akkaunt bilan, qarzli;
- oldindan to'langan (bron), kutilayotgan, bekor qilingan.

Bronlar yonida «taxminan» belgisi va (i) izohi: «GameClass bronlarni alohida saqlamaydi».

**Tariflar:** nom · seanslar · soat · summa.

### 3.4. «Kompyuterlar» tab

```
Band soatlar 2 394 s · Eng ko'p ishlagan: PC 18 · Umuman ishlamagan: 2
Saralash: [ Band soat ▾ ]  (band soat / bandlik / tushum / seanslar / raqam)

PC 18  PlayStation        107,9 s   ▓▓▓▓▓▓▓▓▓░  67%
                           54 seans · 2 053 000 so'm · oxirgi 23:29
PC 13  Bootcamp           105,5 s   ▓▓▓▓▓▓▓▓░░  65%
...
PC 7   VIP                  0 s     ░░░░░░░░░░   0%   ⚠ ishlamagan
```

- Har qator — karta. Bandlik foizi rangli:
  - ≥ 60% — yashil;
  - 20–60% — ko'k;
  - < 20% — kulrang;
  - 0 — sariq ogohlantirish «ishlamagan».
- PC bosilsa → «Kun jadvali» shu PC qatori belgilangan holda ochiladi (sana — davrning oxirgi kuni).
- Zona bo'yicha filtr chiplari (zonalar 2+ bo'lsa).

### 3.5. «Kun jadvali» tab — qaysi PC qachon band bo'lgan

```
‹  Dushanba, 26.10.2026  ›      [Bugun]
■ Mehmon  ■ Akkaunt  ■ Bron      Jami band: 248 s

      06   08   10   12   14   16   18   20   22   00   02   04
PC 1  ██        ████      ██ ███  ████████   ▓▓▓▓▓▓│          10,2 s
PC 2       ██████    ▒▒▒▒   ██   ████ ████  ██████│           11,2 s
...                                                │← «hozir» chizig'i
```

**Gantt** (`CustomPainter`):
- qator = PC (sticky chap ustun: raqam);
- X o'qi — ish kuni 24 soati, belgi har 2 soatda;
- gorizontal scroll, ikki barmoq bilan kattalashtirish (1×–4×);
- o'ngda PC'ning shu kundagi band soatlari.

**Ranglar:**

| Blok turi | Ko'rinishi |
|---|---|
| mehmon | ko'k `#1B54E4` |
| akkaunt | yashil `#1BAF7A` |
| bron (oldindan to'langan) | sariq `#EDA100` |
| hali kelmagan bron | sariq, 45% shaffof, punktir chegara |
| hozir davom etayotgan | to'q chegara |

- **Bugungi kunda** qizil punktir «hozir» chizig'i va ustida vaqt.
- **Blok bosilsa** — pastki varaq:
  - «PC 3 · 18:20–20:05 (1 soat 45 daq)»;
  - tarif, summa;
  - belgilar: Mehmon / Akkaunt / Bron / Hozir davom etyapti.
- Kun hali kelmagan bo'lsa (`404 day_not_found`) → «Bu kun uchun ma'lumot yo'q» + «Bugun» tugmasi.
- Bugun ochiq turganda har 2 daqiqada yangilanadi.

### 3.6. «Bar» tab

- **3 KPI:**
  - Bar jami (▲/▼);
  - Alohida to'langan;
  - Seans va akkaunt hisobidan.
- Ustun grafik: davr bo'yicha bar tushumi (`series[].bar`).
- **Mahsulotlar ro'yxati** (summa bo'yicha kamayish):
  - nom, soni, summa;
  - ostida kichik «alohida X · seansdan Y · akkauntdan Z»;
  - eng ko'p sotilgan 3 tasi oldida 🥇🥈🥉 emas — oddiy raqamli belgilar 1, 2, 3.

### 3.7. «Xodimlar» tab

- **Kassirlar:** ism, seanslar, kompyuter tushumi, bar. Eng ko'p ishlagan — yuqorida.
- **Smenalar** (GameClass'da yopilgan):
  - vaqt, kassir, smena summasi, olib qo'yilgan pul;
  - eng yangisi tepada;
  - «Ko'proq» bilan 20 tadan.
- **Qarzlar:** PC, vaqt, seans summasi. Bo'sh bo'lsa yashil «Qarz yo'q ✓».

### 3.8. Holatlar (`sync.state`)

| state | Ko'rinish |
|---|---|
| `not_connected` | Illyustratsiya + «Klub GameClass orqali ulanmagan» + **«Ulash»** (`GameClassConnectScreen`, faqat egasi; menejerga «Egasidan so'rang») |
| `waiting` | «Hisobot hali kelmadi» + «Klub kompyuteridagi Manajoy GameClass dasturini 0.9.0 ga yangilang. Yangilangach birinchi hisobot 5 daqiqada keladi, eski tarix bir necha soatda to'liq yuklanadi.» |
| `disabled` | Sariq banner «Hisobot yuborish o'chirilgan» + egasiga **«Yoqish»**. Eski kunlar bo'lsa, ular ostida ko'rinaveradi |
| `ok` | Kontent. `sync.first_day` > davr boshi bo'lsa → kulrang chip «Ma'lumot 14.03.2025 dan bor» |
| hisobot 3 soatdan eski (`sync.last_at`) | Tepada sariq qator «Oxirgi hisobot 5 soat oldin keldi — klub kompyuteri o'chiq bo'lishi mumkin» |

Eski tarix hali yuklanayotgan bo'lsa (`first_day` har yangilanishda orqaga suriladi), «Ma'lumot … dan bor» chipi yonida aylanayotgan kichik belgi bo'ladi. Bu zarur emas, yaxshi bo'lsa qo'shiladi.

### 3.9. Sozlamalar (⋮ menyu → «Hisobot sozlamalari»)

- **«Hisobotni qabul qilish»** (faqat egasi) — switch. O'chirishda tasdiq dialogi: «Klub kompyuteri hisobot yubormaydi. Eski kunlar saqlanib qoladi.»
  - API: `PATCH /seller/gaming/integration {analytics_enabled}`.
- **Bildirishnomalar** — 5.3 dagi switch'lar va «Kunlik xulosa vaqti» (soat tanlash, standart 09:00).
- **Excel:** «Kunlar», «Kompyuterlar», «Bar», «Kassirlar», «Smenalar» → `GET /seller/gaming/analytics/export` → share sheet.

## 4. Vizual qoidalar

- **Mavzu:** mavjud `AppTheme`.

  | Nima | Rang |
  |---|---|
  | asosiy | `#1B54E4` |
  | fon | `#F5F7F8` |
  | matn | `#15181B` / `#6B7280` |
  | success | `#1E9E62` |
  | warning | `#E08A1E` |
  | danger | `#E0463A` |

  Kartalar: oq, `radius 16–20`, yengil soya, ichki padding 16.
- **Grafik ranglari faqat shular:**
  - PC vaqti `#1B54E4`, Bar `#EB6834`, Akkaunt `#1BAF7A`, Bron `#EDA100`;
  - issiqlik xaritasi — bitta ko'k shkala (`#EEF3FB` → `#0D366B`).
  - Status ranglari (yashil/sariq/qizil) grafikda ishlatilmaydi — ular holat uchun.
- **Raqamlar:**
  - minglar probel bilan («31 290 000»), `tabular figures`;
  - pul «so'm» bilan (GameClass valyutasi boshqa bo'lsa — o'sha matn);
  - soat «2 394 s», foiz «61,8%» (vergul — uz/ru);
  - ≥ 10 mln bo'lsa KPI'da «31,3 mln» ham mumkin, bosilsa to'liq summa.
- **Sana/vaqt:** «26.10.2026», «18:20». Hafta kunlari «Du…Ya» / «Пн…Вс».
- **Shrift:** ilovaning asosiy shrifti. KPI raqami 22–24 pt **w800**, yorliq 12–13 pt kulrang.
- **Animatsiya:** ustunlar pastdan o'sadi (`GrowBar`, 300 ms), KPI raqamlar sanab chiqadi (0 → qiymat, 600 ms). Faqat birinchi yuklashda; «reduce motion» yoqilgan bo'lsa — yo'q.
- **Accessibility:**
  - hamma tooltip ma'lumoti matn shaklida ham bor (ro'yxat/kartalar);
  - kontrast AA;
  - bosish nuqtasi ≥ 44 px.
- **Tillar:** o'zbek va rus — hammasi `tr()` orqali, prefiks `gma_` (`lib/core/strings_gaming.dart`).

## 5. Bildirishnomalar — telefondan xabardor bo'lish

Mavjud mexanizmdan foydalaniladi:
- `SellerNotification` — ilovadagi ro'yxat va qo'ng'iroqcha;
- FCM push;
- Telegram (ulangan bo'lsa).

**Kim oladi:** egasi va menejerlar (staff hech qachon). Har biri o'z sozlamasiga ko'ra (6.3).

### 5.1. Turlar

| Kod | Qachon | Matn (uz) | Daraja | Standart | Ochiladi |
|---|---|---|---|---|---|
| `gc_daily` | Har kuni, egasi tanlagan soatda (standart 09:00), **kecha**gi ish kuni tugaganidan keyin | **Kecha: 4 480 000 so'm** (▲12% o'tgan dushanbaga) · bandlik 64% · eng faol 21:00–23:00 · bar 1 980 000 | normal | yoqiq | Umumiy, «Kecha» |
| `gc_weekly` | Dushanba, o'sha soatda | **O'tgan hafta: 31,3 mln so'm** (▲1,4%) · bandlik 62% · eng yaxshi kun: shanba | normal | yoqiq | Umumiy, o'tgan hafta |
| `gc_monthly` | Oyning 1-kuni | **Oktyabr: 128 mln so'm** (▲6%) · 4 820 seans | normal | yoqiq | Umumiy, o'tgan oy |
| `gc_shift_closed` | GameClass'da smena yopildi (yangi `shifts` yozuvi) | **Smena yopildi** · Kassir 1 · 2 573 500 so'm · olindi 2 310 000 | normal | yoqiq | Xodimlar |
| `gc_offline` | Jonli holat **15 daqiqa** kelmayapti (bridge yoki internet o'chiq) | **Klub kompyuteri javob bermayapti** · 15 daqiqadan beri ma'lumot yo'q. Mijozlar ilovada «noma'lum» ko'radi | **urgent** | yoqiq | «Hozir» kartasi |
| `gc_gameclass_down` | Bridge ishlayapti, lekin `server_alive=false` **10 daqiqa** | **GameClass Server ishlamayapti** · klub dasturini tekshiring | **urgent** | yoqiq | «Hozir» kartasi |
| `gc_back_online` | `gc_offline` / `gc_gameclass_down` dan keyin holat tiklandi | **Klub yana onlayn** · 42 daqiqa uzilish bo'ldi | low | yoqiq | — |
| `gc_debt` | Yangi qarzli seans (`debts` ga yangi yozuv) | **Qarzli seans** · PC 7 · 24 500 so'm to'lanmay qoldi | high | yoqiq | Xodimlar → Qarzlar |
| `gc_full` | Bandlik ≥ 90% (jonli), kuniga ko'pi bilan 1 marta | **Klub deyarli to'la** · 22 / 24 band (21:40) | low | **o'chiq** | «Hozir» kartasi |
| `gc_low_day` | Kunlik xulosa paytida: kechagi kassa oxirgi 4 haftaning shu kuni o'rtachasidan **30%+ kam** | `gc_daily` matniga qo'shiladi: «⚠ Odatdagidan 35% kam» | — | — | — |

**Qoidalar:**
- **Ovoz va kanal:** `urgent` — ovozli. Qolganlari — jim kanal (`new_order_silent_ch`). Kunlik/haftalik/oylik xulosa — jim.
- **Takrorlanmaslik:**
  - `gc_offline` uzilish boshiga bir marta, `gc_back_online` bir marta;
  - smena va qarz — har yozuv bir marta (`seller + at + kassir` bo'yicha dedupe);
  - xulosalar — kuniga bir marta.
- **Tungi sukunat:** egasi belgilagan oraliqda (standart 00:00–08:00) faqat `urgent` keladi. Qolganlari ro'yxatga tushadi, push yuborilmaydi.
- **Hisobot yuborish o'chiq bo'lsa:** `gc_daily/weekly/monthly`, `gc_shift_closed` va `gc_debt` yuborilmaydi. `gc_offline` va `gc_gameclass_down` ishlayveradi — ular jonli holatdan.

### 5.2. Ilovada

- **Ro'yxat va ikonka:** bildirishnomalar umumiy ro'yxatda o'z ikonkasi bilan:
  - xulosa — `insights`;
  - smena — `point_of_sale`;
  - offline — `wifi_off`, qizil;
  - qarz — `warning_amber`.
- **Bosilganda:** `extra_data.screen` / `extra_data.period` / `extra_data.date` ga qarab kerakli tab va davr ochiladi (deep link).
- **Push ichida summa** odatda ko'rinadi. Qulflangan ekranda begona ko'rmasin desa, egasi sozlamada «Summalarni yashirish»ni (standart o'chiq) yoqadi: «Kecha: ••• so'm».

### 5.3. Sozlamalar (Hisobot sozlamalari → Bildirishnomalar)

```
Kunlik xulosa                      [●]   soat: 09:00 ▾
Haftalik va oylik xulosa           [●]
Smena yopilganda                   [●]
Klub uzilib qolsa (shoshilinch)    [●]
Qarzli seans                       [●]
Klub to'lganda                     [ ]
Tungi sukunat 00:00–08:00          [●]
Summalarni yashirish               [ ]
```

Sozlama **har bir foydalanuvchiga alohida**: egasi va menejer o'zinikini tanlaydi.

## 6. Backend (yangi qism)

Hisobot API tayyor (`GAMECLASS_ANALYTICS_BUSINESS_APP.md`). Bildirishnomalar uchun quyidagilar kerak.

### 6.1. Turlar va saqlash

- `SellerNotification.TYPE_CHOICES` ga `gameclass` turi.
  - Kichik tur `extra_data.kind`: `gc_daily`, `gc_weekly` va h.k.
  - Bog'lanish: `extra_data.screen` (`overview|pcs|day|bar|staff`), `extra_data.period` / `date`.
- Push mavjud `notifications.py` yo'li orqali, Android kanali qoidasi bilan.

### 6.2. Qayerdan ishga tushadi

| Tur | Manba |
|---|---|
| `gc_shift_closed`, `gc_debt` | `ingest_analytics` ichida (`fast_api/services/integrations/gameclass/analytics.py`). Yangi kelgan kunda yangi `shifts` / `debts` yozuvi bo'lsa. Dedupe: Redis `gc:notified:{seller}:{hash}` (30 kun TTL). **Eski tarix yuklanayotganda (kun 2 kundan eski) yuborilmaydi.** |
| `gc_daily/weekly/monthly`, `gc_low_day` | Celery beat, har 15 daqiqada. Har klub uchun mahalliy vaqt foydalanuvchi tanlagan soatga yetgan va kecha kuni `complete` bo'lsa yuboradi. Kun kelmagan bo'lsa 12:00 gacha kutadi, keyin «Kechagi hisobot kelmadi» deb `gc_offline` mantiqiga topshiradi |
| `gc_offline`, `gc_gameclass_down`, `gc_back_online`, `gc_full` | Celery beat, har 5 daqiqada. `GameCenterIntegration` (provider=gameclass, polling_enabled) → `last_success_at` va jonli envelope (`server_alive`, `counts`). Holat Redis'da (`gc:alert:{seller}`) — boshlanish vaqti va yuborildimi |

### 6.3. Sozlamalar endpointi

- `GET /seller/gaming/analytics/notifications`
- `PUT /seller/gaming/analytics/notifications`

Faqat egasi va menejer, **har foydalanuvchi uchun** (`SellerMembership` bo'yicha):

```json
{
  "daily": true, "daily_time": "09:00",
  "weekly_monthly": true,
  "shift_closed": true,
  "offline": true,
  "debt": true,
  "full": false,
  "quiet_hours": { "enabled": true, "from": "00:00", "to": "08:00" },
  "hide_amounts": false
}
```

- **Saqlash:** yangi model `GameClassNotifyPrefs(membership OneToOne, …)` yoki `SellerSettings` ga JSON maydon — backend tanlaydi.
- **Bog'liqlik:** umumiy `notify_system` o'chiq bo'lsa ham bu sozlamalar ishlaydi. Ular alohida.

### 6.4. «Bugun hozirgacha» solishtirish

`GET /seller/gaming/analytics` javobiga, davr bitta «bugun» bo'lsa, `today` bloki qo'shiladi:

```json
"today": { "kassa_so_far": 2450000, "same_time_last_week": 2268000 }
```

Hisob: o'tgan haftaning shu ish kuni `hourly_money` dan hozirgi soatgacha yig'iladi.

### 6.5. Testlar

- Har tur uchun trigger va dedupe tekshiriladi.
- Tungi sukunat.
- Staff olmasligi.
- Hisobot o'chiq bo'lganda faqat jonli turlar keladi.
- Eski tarix yuklanayotganda smena/qarz push'i bo'lmasligi.

## 7. API xulosasi (ilova uchun)

| Maqsad | So'rov |
|---|---|
| Ko'rinish sharti va jonli holat | `GET /seller/gaming/integration` (`provider`, `live`, `analytics_enabled`) |
| Hisobot (davr) | `GET /seller/gaming/analytics?date_from&date_to&group` |
| Kun jadvali | `GET /seller/gaming/analytics/day?date` |
| Excel | `GET /seller/gaming/analytics/export?table&format` |
| Yuborishni yoqish/o'chirish (egasi) | `PATCH /seller/gaming/integration {analytics_enabled}` |
| Bildirishnoma sozlamalari | `GET/PUT /seller/gaming/analytics/notifications` (6.3, yangi) |

## 8. Qabul mezonlari

1. GameClass klubi egasi va menejerida pastki menyuda «Hisobot» tabi bor. Staff'da, iCafe klubida va boshqa biznes turlarida yo'q.
2. «Bugun / Kecha / 7 kun / Oy / Yil» raqamlari klub kompyuteridagi bridge «Hisobot» oynasi bilan **aynan bir xil**. Lab'da tekshirilgan: 3–26.10.2026 kassa 99 244.
3. «Hozir» kartasi 30 soniyada yangilanadi. Klub dasturi o'chganda 1 daqiqada qizil holatga o'tadi.
4. Kun jadvalida har seans to'g'ri PC qatorida, to'g'ri vaqtda. Bosilganda tafsilot ochiladi. Bugungi kunda «hozir» chizig'i bor.
5. **Bildirishnomalar:**
   - kunlik xulosa tanlangan soatda bir marta keladi;
   - smena yopilgach 10 daqiqa ichida xabar keladi;
   - klub 15 daqiqa javob bermasa shoshilinch xabar, tiklanganda «yana onlayn».
   - Har biri bosilganda kerakli joy ochiladi. Tungi sukunat ishlaydi.
6. 4 holat (`not_connected`, `waiting`, `disabled`, `ok`) va xato holati dizayn bo'yicha ko'rinadi. 320 px kenglikda hech narsa sig'may qolmaydi.
7. O'zbek va rus tillari to'liq. Hamma raqam va sana formati 4-bo'limdagidek.
8. **Dizayner topshiradi:**
   - Umumiy, Kompyuterlar, Kun jadvali, Bar, Xodimlar, 4 holat, Sozlamalar ekranlari;
   - push ko'rinishi (Android, iOS);
   - och mavzu; ilovada qorong'i mavzu bo'lsa, u ham.
