<div dir="rtl" align="center">

<img src="docs/logo.png" alt="شعار Nova Launcher" width="104" />

# Nova Launcher

**لانشر ماينكرافت سريع ومحمول ومدير مودباكات آمن لنظام ويندوز**  
*مزوّد بواجهة **Nova Workbench** المكتبية وأداة الأوامر `nova-cli`*

[**English**](README.md) · [**العربية**](README.ar.md) · [**سجل التغييرات**](CHANGELOG.md) · [**الأمان**](SECURITY.md) · [**المساهمة**](CONTRIBUTING.md)

<br />

[![أحدث إصدار](https://img.shields.io/github/v/release/abde00037-lang/nova-launcher?style=for-the-badge&label=RELEASE&color=e07a5f&logo=github)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![حالة البناء](https://img.shields.io/github/actions/workflow/status/abde00037-lang/nova-launcher/ci.yml?style=for-the-badge&label=CI%20BUILD&logo=githubactions&logoColor=white)](https://github.com/abde00037-lang/nova-launcher/actions)
[![المنصة](https://img.shields.io/badge/WINDOWS-10%20%2F%2011%20(64--BIT)-1f2937?style=for-the-badge&logo=windows)](https://github.com/abde00037-lang/nova-launcher/releases/latest)
[![الرخصة](https://img.shields.io/badge/LICENSE-MIT-0ea5e9?style=for-the-badge)](LICENSE)

<br />

[**⬇ تحميل المثبّت الكامل أوفلاين**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) · [**⚡ مثبّت الويب الخفيف**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) · [**📦 النسخة المحمولة `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) · [**🔐 بصمات التحقق SHA-256**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt)

</div>

---

<div dir="rtl">

## نظرة عامة

**Nova Launcher** (بواجهته المكتبية الحديثة **Nova Workbench**) هو لانشر ماينكرافت سريع ومفتوح المصدر مبني على **.NET 8 و WPF**، مصمم بفصل كامل بين ملفات البرنامج (`bin\`) وبيانات اللاعب (`instances\`, `saves\`, `mods\`, `accounts\`).

تعمل النسخة المحمولة من مجلد يمكن نقله، بعيداً عن `%APPDATA%\.minecraft`. يختار المثبّت افتراضياً `%LOCALAPPDATA%\NovaWorkbench` ويمكنه إنشاء اختصارات في ويندوز. احتفظ بنسخة احتياطية من بياناتك قبل التحديث.

<p align="center">
  <img src="docs/screenshots/showcase.gif" alt="جولة مصوّرة داخل Nova Launcher" width="96%" />
</p>

---

## لماذا Nova Launcher؟

*مقارنة حتى تاريخ نوفمبر 2026 — كل من Prism Launcher و Modrinth App خيارات ممتازة؛ يركّز Nova تحديداً على المحمولية الكاملة بدون كتابة في النظام، والتحديث الهوائي الخفيف لمجلد `bin\` فقط، والتكافؤ التام بين الواجهة وسطر الأوامر.*

| الميزة | Nova Launcher | Prism Launcher | Modrinth App |
| :--- | :---: | :---: | :---: |
| **محمول افتراضياً 100%** (لا يكتب شيئاً في `%APPDATA%`) | **نعم** | يتوفر كخيار محمول | تطبيق مثبّت |
| **تحديث هوائي خفيف** ينسخ ملفات التطبيق إلى `bin\` دون أن يكون تحديث delta | **نعم** | تحديث كامل للتطبيق | مدمج |
| **أنماط الحسابات المدعومة** | **Microsoft + حساب أوفلاين مستقل مباشرة** | Microsoft (+ أوفلاين بعد ربط MS) | Microsoft |
| **دعم المودلودرات بضغطة زر** (`Vanilla`, `Forge`, `Fabric`, `NeoForge`) | **نعم** | نعم (+ Quilt) | نعم (+ Quilt) |
| **مصادر المودات المدمجة** (مع حل الاعتماديات تلقائياً) | **Modrinth + CurseForge** | Modrinth + CurseForge | Modrinth |
| **كشف المود المسبب للانهيار + زر تعطيله بضغطة واحدة على شريط التنبيه** | **نعم** | عرض ورفع سجل الانهيار | عرض سجل الانهيار |
| **أداة سطر أوامر كاملة (`nova-cli`)** للتثبيت والفحص وتحديث المودات | **نعم** | خيارات تشغيل واستيراد | واجهة رسومية |

---

## التحميل والتثبيت ومتطلبات النظام

الحزم الكاملة تتضمن `.NET`، ويُحمَّل Java وملفات ماينكرافت عند الحاجة. الأحجام التالية للإصدار v2.1.0:

| الحزمة | الحجم الفعلي | الاستخدام الأنسب | الرابط المباشر |
| :--- | :---: | :--- | :---: |
| **`Nova-Setup-Full.exe`** *(موصى به)* | **72,878,592 B** | **المثبّت الكامل أوفلاين.** يحتوي على اللانشر وأداة `nova-cli` وحزمة `.NET 8` مدمجة بالكامل داخل ملف واحد؛ يثبّت في ثوانٍ بدون إنترنت وينشئ اختصار سطح المكتب. | [**تحميل `.exe` الكامل**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) |
| **`Nova-Web-Setup.exe`** | **68,096 B** | **مثبّت الويب الخفيف.** مثالي للإرسال السريع على Discord/Telegram؛ يجلب أحدث إصدار تلقائياً من GitHub. | [**تحميل `.exe` الخفيف**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) |
| **`Nova-Portable-Full-win-x64.zip`** | **72,819,042 B** | **الأرشيف المحمول الجاهز.** فك الضغط في أي مكان وشغّل `Start Nova.bat`. | [**تحميل `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) |

- **متطلبات التشغيل:** ويندوز 10 أو 11 بمعمارية **64-بت (`x64`)**، وذاكرة عشوائية 4 GB كحد أدنى (يُنصح بـ 8 GB+ للمودباكات الكبيرة).

---

## الحسابات والأمان وبصمات التحقق (`SHA-256`)

- **تسجيل الدخول (Microsoft + Offline):** يدعم تسجيل الدخول الرسمي بحسابات مايكروسوفت عبر رمز الجهاز الآمن (`OAuth2 Device Code` بدون إدخال كلمة المرور داخل اللانشر)، كما يدعم إنشاء حسابات محلية أوفلاين مباشرة بدون إنترنت.
- **تنبيه Windows SmartScreen وبرامج الحماية:** لأن المشروع مجاني ومفتوح المصدر بدون شهادة توقيع تجارية مدفوعة، قد تظهر نافذة SmartScreen عند أول تشغيل — اضغط **More info ← Run anyway**. وإذا اعترض برنامج الحماية على مثبّت الويب الصغير، استخدم المثبّت الكامل **`Nova-Setup-Full.exe`**.

### بصمات التحقق للإصدار `v2.1.0` (`SHA-256`)

يمكنك التحقق عبر الأمر `Get-FileHash <file> -Algorithm SHA256` ومطابقته مع ملف [`SHA256SUMS.txt`](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/SHA256SUMS.txt):

```text
a70327929f0d397de66446aee350f630aa0da984a0bef93df26a3266d7fd8315  Nova-Setup-Full.exe
7d7aee48ec9fb1de576f4aacb13ea91e1c41fc1df8c03dccafe3ad396e42b156  Nova-Web-Setup.exe
af01fcdcf97a3aeb1ec15e1fb85dfcfb5bae8549feaf27acbacc07ec71b41c4d  Nova-Portable-Full-win-x64.zip
71a46a2ff9a80f21356c04bc709730711dc56c044b4de343e66156c115c24efd  nova-bin-win-x64.zip
```

---

## الأسئلة الشائعة (FAQ)

**1. هل تحديث Nova Launcher يحذف عوالمي أو موداتي أو حساباتي؟**  
المحدّث ينسخ ملفات التطبيق إلى `bin\` ولا يتعمّد حذف بياناتك. احتفظ بنسخة احتياطية قبل التحديث؛ العملية ليست تصحيح delta ثنائياً.

**2. هل أحتاج إلى تثبيت Java أو .NET 8 يدوياً؟**  
**لا يحتاج التثبيت الكامل الأولي إلى تثبيت .NET منفصل.** المثبّت يحوي حزمة مضغوطة، وبعد فكها يحتوي مجلد `bin\` على التطبيق ومئات ملفات .NET 8/WPF، مثل `coreclr.dll` و `hostfxr.dll` و `hostpolicy.dll`. أما حزمة OTA الصغيرة فتحوي ملفات التطبيق المعتمدة على إطار العمل فقط، وليس الـ runtime. توافقها بعد تحديث التثبيت المستقل ما زال يحتاج اختباراً؛ احتفظ بنسخة احتياطية واستخدم المثبّت الكامل عند الحاجة. ويُحمَّل Java (`8` أو `17` أو `21`) عند الحاجة.

**3. هل يمكنني نقل مجلد اللانشر بالكامل إلى جهاز آخر أو فلاشة USB؟**  
**نعم.** جميع المسارات نسبية داخل مجلد `NovaLauncher/`، ويمكنك نقله بالكامل وتشغيله فوراً بكل نسخه وعوالمه.

---

## لقطات من داخل الواجهة

<p align="center">
  <img src="docs/screenshots/overview.png" alt="Nova Launcher — لوحة القيادة الرئيسية" width="96%" />
</p>
<p align="center">
  <img src="docs/screenshots/versions.png" alt="Nova Launcher — تثبيت الإصدارات والمودلودرات" width="96%" />
</p>
<p align="center">
  <img src="docs/screenshots/store.png" alt="Nova Launcher — متجر المودات" width="96%" />
</p>

---

## الترخيص والأمان

- للإبلاغ عن الثغرات الأمنية راجع [**SECURITY.md**](SECURITY.md)، وللمساهمة راجع [**CONTRIBUTING.md**](CONTRIBUTING.md).
- مرخّص بموجب رخصة [MIT](LICENSE). مشروع مجتمعي مستقل غير تابع لشركة Mojang Studios أو Microsoft.

</div>
