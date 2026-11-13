# TZ: sayt orqali Manajoy GameClass yangilanishlarini tarqatish

## 1. Maqsad va ish hajmi

Windows uchun Manajoy GameClass yangi versiyani `www.manajoy.uz` saytidan tekshirishi va installerini yuklab olishi kerak. Foydalanuvchi keyingi yangilanishlar uchun Telegram'dan fayl qidirib yurmaydi.

Vazifa — tayyor, imzolangan release fayllarini barqaror HTTPS URL'larda tarqatish. Backend API, ma'lumotlar bazasi, login yoki admin panel yaratish talab qilinmaydi. Yangilashni bajarish, Windows xizmatini qayta boshlash va sozlamalarni saqlash desktop dastur vazifasi.

Sayt: Next.js, Vercel. Lokal repo: `user-app-frontend/mana`.
`public/gameclass/` fayllari va `next.config.ts` headerlari lokal nusxada allaqachon tayyorlangan; mavjud o'zgarishlarni tekshirib deploy qilish kerak.

## 2. Fayllar va URL shartnomasi

Quyidagi uchta fayl autentifikatsiyasiz, redirect bo'lmasdan **HTTP 200** bilan ochilsin:

- `https://www.manajoy.uz/gameclass/latest.json`
- `https://www.manajoy.uz/gameclass/latest.json.sig`
- `https://www.manajoy.uz/gameclass/download/ManajoyGameClass-Setup-0.11.0.exe`

Saytdagi joylashuv:

```text
public/gameclass/
  latest.json
  latest.json.sig
  download/
    ManajoyGameClass-Setup-0.11.0.exe
```

`latest.json` uchun hozirgi release misoli:

```json
{
  "version": "0.11.0",
  "released": "2026-11-03",
  "file": "download/ManajoyGameClass-Setup-0.11.0.exe",
  "size": 33244929,
  "sha256": "38ef5ebde6b1427121a31e3ea1cd1c003109da9809a4134c8d9a3f75c31d26cd"
}
```

- `file` — `latest.json` manziliga nisbatan yo'l; aynan `download/ManajoyGameClass-Setup-<version>.exe` shaklida.
- `size` — installerning aniq byte soni; MB qiymati emas. Desktop mijoz 200 000 000 byte'gacha qabul qiladi.
- `sha256` — exe byte'larining 64 belgili SHA-256 qiymati.
- `.sig` — manifest uchun RSA/SHA-256 **binary imzo**. Uni JSON, matn yoki Base64'ga aylantirmang.
- JSON, imzo va exe release paketidan aynan ko'chiriladi. JSON'ni qayta formatlash, maydonlarini almashtirish, BOM yoki yangi qator qo'shish ham imzoni buzishi mumkin. Sayt buildidagi formatterlar bu faylni o'zgartirmasin.

## 3. HTTP va routing talablari

- `latest.json`: `Content-Type: application/json`; `Cache-Control: no-store, max-age=0`.
- `latest.json.sig`: `Content-Type: application/octet-stream`; `Cache-Control: no-store, max-age=0`.
- `download/*.exe`: binary fayl javobi; `Content-Disposition: attachment`; `Cache-Control: public, max-age=31536000, immutable`.
- Fayllar o'rniga Next.js HTML sahifasi, login, CAPTCHA, SPA fallback yoki xato sahifasi HTTP 200 bilan qaytmasin. Yo'q fayl HTTP 404 qaytarsin.
- CDN/WAF desktop dastur so'rovlarini browser challenge bilan to'smasin.
- Boshqa domen, apex domen yoki vaqtinchalik download URL'ga redirect ishlatilmasin: desktop mijoz redirectlarni qabul qilmaydi.
- CORS uchun alohida sozlama kerak emas: yuklashni desktop dasturining HTTP mijozi bajaradi.

## 4. Release va deploy tartibi

Bridge jamoasi har release uchun `latest.json`, `latest.json.sig` va versionli exe beradi. Tayyor paket: `ManajoyGameClass-UpdateFeed-0.11.0.zip`; ichidagi `gameclass/` papkasi saytning `public/` papkasiga mos keladi.

1. Uchta faylni bitta Vercel deployment tarkibida chiqaring. Manifest boshqa release'ning imzosi yoki exe'si bilan aralashmasin.
2. Agar fayllar alohida yuklansa, avval exe to'liq joylansin, so'ng manifest va imzo muvofiqlashtirilgan holda almashtirilsin. Tavsiya — atomik deployment.
3. E'lon qilingan versiyaning exe'sini boshqa byte'lar bilan almashtirmang. Tuzatish bo'lsa yangi versiya raqami va yangi imzolangan paket kerak.
4. Oldingi versiyali download URL'lar mavjudligicha qolsin: yangilanishni tekshirib bo'lgan mijoz hali o'sha faylni yuklayotgan bo'lishi mumkin.
5. Private signing key frontend repo, Vercel environment yoki `public/` ichiga qo'yilmaydi. Sayt release'ni o'zi imzolamaydi.

## 5. Qabul qilish mezonlari

- Production'da uchala URL aynan shu domen orqali HTTP 200 qaytaradi; redirect va autentifikatsiya yo'q.
- Manifest va imzodagi cache headerlari yuqoridagi talablarga mos.
- Production'dan olingan JSON va imzo release paketidagi byte'lar bilan bir xil.
- Yuklangan exe uzunligi va SHA-256 qiymati manifestga mos.
- Mavjud bo'lmagan exe URL'i HTTP 404 qaytaradi.
- Oddiy sayt sahifalari va mavjud routing ishlashda davom etadi.

Tekshirish namunasi (`curl` uchun redirectga ergashadigan `-L` ishlatmang):

```bash
curl -fS -D - -o /tmp/latest.json https://www.manajoy.uz/gameclass/latest.json
curl -fS -D - -o /tmp/latest.json.sig https://www.manajoy.uz/gameclass/latest.json.sig
curl -fS -D - -o /tmp/gameclass-setup.exe \
  https://www.manajoy.uz/gameclass/download/ManajoyGameClass-Setup-0.11.0.exe
sha256sum /tmp/gameclass-setup.exe
wc -c /tmp/gameclass-setup.exe
```

Har bir javob headerida HTTP 200 ekanini tekshiring. Fayl hajmi va hashni yuklangan manifest bilan solishtiring.

Desktop bilan yakuniy tekshiruv: 0.11.0 dasturidagi **Sozlamalar → Dastur yangilanishi → Yangilanishni tekshirish** serverga ulanadi. Feed ham 0.11.0 bo'lsa, «Eng so'nggi versiya o'rnatilgan» chiqishi to'g'ri. «Yangi versiya mavjud» va to'liq yangilash sinovi uchun Bridge jamoasi haqiqiy keyingi versiyaning imzolangan paketini berishi kerak; test uchun manifestdagi raqamni qo'lda o'zgartirmang.

0.11.0 dan oldingi dasturlarda internet orqali yangilash yo'q: foydalanuvchi 0.11.0 ni bir marta qo'lda o'rnatadi.

## 6. Topshiriladigan natija

Frontend o'zgarishlari, production deployment URL/commit va yuqoridagi HTTP, cache, hajm va hash tekshiruvlari natijasini bering. Faqat lokal fayllar yoki preview deployment mavjudligi production vazifasi bajarilganini anglatmaydi.
