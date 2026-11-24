<div dir="rtl" align="center">

<img src="docs/logo.png" alt="شعار Nova Launcher" width="104" />

# Nova Launcher

**لانشر ماينكرافت سريع ومحمول ومدير مودباكات آمن لنظام ويندوز**  
*مزوّد بواجهة **Nova Workbench** المكتبية وأداة الأوامر `nova-cli`*

[**English**](README.md) · [**العربية**](README.ar.md) · [**سجل التغييرات**](CHANGELOG.md) · [**المساهمة**](CONTRIBUTING.md)

<br />

[![أحدث إصدار](https://img.shields.io/github/v/release/abde00037-lang/nova-launcher?style=for-the-badge&label=RELEASE&color=e07a5f&logo=github)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![حالة البناء](https://img.shields.io/github/actions/workflow/status/abde00037-lang/nova-launcher/ci.yml?style=for-the-badge&label=CI%20BUILD&logo=githubactions&logoColor=white)](https://github.com/abde00037-lang/nova-launcher/actions)
[![المنصة](https://img.shields.io/badge/WINDOWS-10%20%2F%2011%20(64--BIT)-1f2937?style=for-the-badge&logo=windows)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![الرخصة](https://img.shields.io/badge/LICENSE-MIT-0ea5e9?style=for-the-badge)](LICENSE)

<br />

[**⬇ تحميل المثبّت الكامل أوفلاين (`.exe`)**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) · [**⚡ مثبّت الويب الخفيف (`66 KB`)**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) · [**📦 النسخة المحمولة `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) · [**🔐 بصمات التحقق SHA-256**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt)

</div>

---

<div dir="rtl">

## نظرة عامة

**Nova Launcher** (بواجهته المكتبية الحديثة **Nova Workbench**) هو لانشر ماينكرافت سريع ومفتوح المصدر مبني على **.NET 8 و WPF**، مصمم بفصل كامل بين ملفات البرنامج (`bin\`) وبيانات اللاعب (`instances\`, `saves\`, `mods\`, `accounts\`).

بفضل هذا التصميم المحمول، يعمل اللانشر مباشرة من أي مجلد أو فلاشة USB دون بعثرة ملفات داخل `%APPDATA%\.minecraft`، ويستقبل التحديثات الهوائية (OTA) في 3 ثوانٍ **دون المساس بعوالمك أو موداتك إطلاقاً**.

<p align="center">
  <img src="docs/screenshots/showcase.gif" alt="جولة مصوّرة داخل Nova Workbench" width="96%" />
</p>

---

## لماذا Nova Launcher؟

| الميزة | Nova Launcher | Prism Launcher | Modrinth App |
| :--- | :---: | :---: | :---: |
| **محمول بالكامل 100%** (لا يكتب شيئاً في `%APPDATA%`) | **نعم** | نسخة مضغوطة | لا (تثبيت نظام) |
| **تحديث هوائي ذاتي آمن (3 ثوانٍ)** يحدّث `bin\` فقط دون لمس العوالم | **نعم** | يدوي / مثبّت | مدمج |
| **حسابات Microsoft الرسمية + حسابات أوفلاين محلية** جاهزة فوراً | **كلاهما** | يشترط حساب مايكروسوفت أولاً | مايكروسوفت فقط |
| **دعم 4 مودلودرات بضغطة زر** (`Vanilla`, `Forge`, `Fabric`, `NeoForge`) | **نعم** | نعم | نعم |
| **متجر مزدوج** (Modrinth + CurseForge مع حل الاعتماديات تلقائياً) | **كلاهما** | كلاهما | Modrinth فقط |
| **تحليل الانهيارات مع زر تعطيل المود المسبب بضغطة زر** | **نعم** | عرض السجل فقط | عرض السجل فقط |
| **أداة سطر أوامر كاملة (`nova-cli`)** بنفس محرك الواجهة | **نعم** | جزئي | لا |

---

## التحميل والتثبيت ومتطلبات النظام

لا تحتاج إلى تثبيت `.NET` أو `Java` يدوياً — كل شيء مجهّز تلقائياً:

| الحزمة | الحجم | الاستخدام الأنسب | الرابط المباشر |
| :--- | :---: | :--- | :---: |
| **`Nova-Setup-Full.exe`** *(موصى به)* | **~69.5 MB** | **المثبّت الكامل أوفلاين.** يحتوي على اللانشر وأداة `nova-cli` وحزمة `.NET 8` مدمجة بالكامل داخل ملف واحد؛ يثبّت في ثوانٍ بدون إنترنت وينشئ اختصار سطح المكتب. | [**تحميل `.exe` الكامل**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) |
| **`Nova-Web-Setup.exe`** | **~66 KB** | **مثبّت الويب الخفيف.** مثالي للإرسال السريع على Discord/Telegram؛ يجلب أحدث إصدار تلقائياً من GitHub. | [**تحميل `.exe` الخفيف**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) |
| **`Nova-Portable-Full-win-x64.zip`** | **~69.5 MB** | **الأرشيف المحمول الجاهز.** فك الضغط في أي مكان وشغّل `Start Nova.bat`. | [**تحميل `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) |

- **متطلبات التشغيل:** ويندوز 10 أو 11 بمعمارية **64-بت (`x64`)**، وذاكرة عشوائية 4 GB كحد أدنى (يُنصح بـ 8 GB+ للمودباكات الكبيرة).

---

## الحسابات والأمان والتحقق من الملفات (SHA-256)

- **تسجيل الدخول (Microsoft + Offline):** يدعم تسجيل الدخول الرسمي بحسابات مايكروسوفت عبر رمز الجهاز الآمن (`OAuth2 Device Code` بدون إدخال كلمة المرور داخل اللانشر)، كما يدعم إنشاء حسابات محلية أوفلاين للعب الفردي والشبكة المحلية (LAN).
- **تنبيه Windows SmartScreen وبرامج الحماية:** لأن المشروع مجاني ومفتوح المصدر بدون شهادة توقيع تجارية مدفوعة، قد تظهر نافذة SmartScreen عند أول تشغيل — اضغط **More info ← Run anyway**. وإذا اعترض برنامج الحماية على مثبّت الويب الصغير (`66 KB`)، استخدم المثبّت الكامل **`Nova-Setup-Full.exe`** المبني بالكامل عبر GitHub Actions مع ملف التحقق [`SHA256SUMS.txt`](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt).

---

## لقطات من داخل الواجهة

<p align="center">
  <img src="docs/screenshots/overview.png" alt="لوحة القيادة الرئيسية" width="96%" />
</p>
<p align="center">
  <img src="docs/screenshots/versions.png" alt="تثبيت الإصدارات والمودلودرات" width="96%" />
</p>
<p align="center">
  <img src="docs/screenshots/store.png" alt="متجر المودات Mod Catalog" width="96%" />
</p>

---

## الترخيص

مرخّص بموجب رخصة [MIT](LICENSE).  
*مشروع مجتمعي مستقل غير تابع لشركة Mojang Studios أو Microsoft.*

</div>
