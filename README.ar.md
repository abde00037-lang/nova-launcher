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

## آخر تحديث — v2.1.6

- استكمال تنزيل التحديث بعد الانقطاع والتحقق من SHA-256.
- التحقق من ملفات التثبيت ومحاولة استرجاع النسخة السابقة عند فشل النسخ.
- احتفظ بنسخة احتياطية؛ الاسترجاع لا يضمن التعافي من كل انقطاع.

[تحميل v2.1.6](https://github.com/abde00037-lang/nova-launcher/releases/tag/v2.1.6) · [سجل التغييرات](CHANGELOG.md)

---

## لماذا Nova Launcher؟

*مقارنة حتى تاريخ نوفمبر 2026 — كل من Prism Launcher و Modrinth App خيارات ممتازة؛ يركّز Nova تحديداً على المحمولية الكاملة بدون كتابة في النظام، والتحديث الهوائي لمجلد `bin\`.*

| الميزة | Nova Launcher | Prism Launcher | Modrinth App |
| :--- | :---: | :---: | :---: |
| **محمول افتراضياً 100%** (لا يكتب شيئاً في `%APPDATA%`) | **نعم** | يتوفر كخيار محمول | تطبيق مثبّت |
| **تحديث هوائي** ينسخ ملفات التطبيق وبيئة التشغيل إلى `bin\` دون أن يكون تحديث delta | **نعم** | تحديث كامل للتطبيق | مدمج |
| **أنماط الحسابات المدعومة** | **Microsoft + حساب أوفلاين مستقل مباشرة** | Microsoft (+ أوفلاين بعد ربط MS) | Microsoft |
| **دعم المودلودرات بضغطة زر** (`Vanilla`, `Forge`, `Fabric`, `NeoForge`) | **نعم** | نعم (+ Quilt) | نعم (+ Quilt) |
| **مصادر المودات المدمجة** (مع حل الاعتماديات تلقائياً) | **Modrinth + CurseForge** | Modrinth + CurseForge | Modrinth |
| **كشف المود المسبب للانهيار + زر تعطيله بضغطة واحدة على شريط التنبيه** | **نعم** | عرض ورفع سجل الانهيار | عرض سجل الانهيار |
| **أداة سطر أوامر كاملة (`nova-cli`)** للتثبيت والفحص وتحديث المودات | **نعم** | خيارات تشغيل واستيراد | واجهة رسومية |

---

## التحميل والتثبيت ومتطلبات النظام

> **التحديث من v2.1.0:** يعالج v2.1.1 خللاً في إعدادات runtime للتحديث. إذا لم تعد النسخة القديمة تعمل بعد تحديث سابق، انسخ بياناتك احتياطياً قبل استخدام المثبّت الكامل للاستعادة. بدءاً من v2.1.1 تضم حزمة OTA بيئة التشغيل كاملة ولذلك حجمها أكبر.

الحزم الكاملة تتضمن `.NET`، ويُحمَّل Java وملفات ماينكرافت عند الحاجة. راجع صفحة الإصدار لمعرفة الأحجام والبصمات الدقيقة:

| الحزمة | الحجم | الاستخدام الأنسب | الرابط المباشر |
| :--- | :---: | :--- | :---: |
| **`Nova-Setup-Full.exe`** *(موصى به)* | راجع الإصدار | **المثبّت الكامل أوفلاين.** يحتوي على اللانشر وأداة `nova-cli` وحزمة `.NET 8` مدمجة بالكامل داخل ملف واحد؛ يثبّت في ثوانٍ بدون إنترنت وينشئ اختصار سطح المكتب. | [**تحميل `.exe` الكامل**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Setup-Full.exe) |
| **`Nova-Web-Setup.exe`** | راجع الإصدار | **مثبّت الويب الخفيف.** مثالي للإرسال السريع على Discord/Telegram؛ يجلب أحدث إصدار تلقائياً من GitHub. | [**تحميل `.exe` الخفيف**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Web-Setup.exe) |
| **`Nova-Portable-Full-win-x64.zip`** | راجع الإصدار | **الأرشيف المحمول الجاهز.** فك الضغط في أي مكان وشغّل `Start Nova.bat`. | [**تحميل `.zip`**](https://github.com/abde00037-lang/nova-launcher/releases/latest/download/Nova-Portable-Full-win-x64.zip) |
| **`nova-bin-win-x64.zip`** | راجع الإصدار | حزمة تحديث OTA شاملة بيئة التشغيل. | تُستخدم تلقائياً |

- **متطلبات التشغيل:** ويندوز 10 أو 11 بمعمارية **64-بت (`x64`)**، وذاكرة عشوائية 4 GB كحد أدنى (يُنصح بـ 8 GB+ للمودباكات الكبيرة).

---

## الحسابات والأمان وبصمات التحقق (`SHA-256`)

- **تسجيل الدخول (Microsoft + Offline):** يدعم تسجيل الدخول الرسمي بحسابات مايكروسوفت عبر رمز الجهاز الآمن (`OAuth2 Device Code` بدون إدخال كلمة المرور داخل اللانشر)، كما يدعم إنشاء حسابات محلية أوفلاين مباشرة بدون إنترنت.
- **تنبيه Windows SmartScreen وبرامج الحماية:** لأن المشروع مجاني ومفتوح المصدر بدون شهادة توقيع تجارية مدفوعة، قد تظهر نافذة SmartScreen عند أول تشغيل — اضغط **More info ← Run anyway**. وإذا اعترض برنامج الحماية على مثبّت الويب الصغير، استخدم المثبّت الكامل **`Nova-Setup-Full.exe`**.

### بصمات التحقق (`SHA-256`)

استخدم `Get-FileHash <file> -Algorithm SHA256` وقارن النتيجة بملف `SHA256SUMS.txt` المرفق **بنفس الإصدار** على [صفحة الإصدارات](https://github.com/abde00037-lang/nova-launcher/releases/latest). البصمة للتحقق من سلامة التنزيل وليست توقيعاً مستقلاً.

---

## الأسئلة الشائعة (FAQ)

**1. هل تحديث Nova Launcher يحذف عوالمي أو موداتي أو حساباتي؟**  
المحدّث ينسخ ملفات التطبيق إلى `bin\` ولا يتعمّد حذف بياناتك. احتفظ بنسخة احتياطية قبل التحديث؛ العملية ليست تصحيح delta ثنائياً.

**2. هل أحتاج إلى تثبيت Java أو .NET 8 يدوياً؟**  
**لا يحتاج التثبيت الكامل الأولي إلى تثبيت .NET منفصل.** المثبّت يحوي حزمة مضغوطة، وبعد فكها يحتوي مجلد `bin\` على التطبيق ومئات ملفات .NET 8/WPF، مثل `coreclr.dll` و `hostfxr.dll` و `hostpolicy.dll`. حزمة OTA في v2.1.0 كانت تعتمد على إطار عمل مثبت وقد تعطل التثبيت المستقل. ابتداءً من v2.1.1 تضم حزمة التحديث التطبيق وبيئة التشغيل كاملة، لذا أصبح حجمها أكبر. ويُحمَّل Java عند الحاجة للإصدارات المدعومة.

**3. هل يمكنني نقل مجلد اللانشر بالكامل إلى جهاز آخر أو فلاشة USB؟**  
يمكن نقل المجلد وبيانات العوالم، لكن رموز حساب Microsoft المحمية بـWindows DPAPI مرتبطة بحساب Windows والجهاز؛ قد تحتاج إلى تسجيل الدخول مجدداً بعد النقل.

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
