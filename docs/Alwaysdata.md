# رفع المشروع على Alwaysdata

المشروع يعمل على .NET 8 وPython وSQLite. هذا الدليل يستخدم نسخة Linux منشورة من جهازك، ثم موقعًا من النوع `.NET` على Alwaysdata. النسخة المنشورة تحتوي على الكود والواجهات، وتبدأ بقاعدة بيانات جديدة عند أول تشغيل.

`Start.cmd` يشغّل المشروع على جهازك ويفتح `localhost:5000`. فتح `index.html` من جهازك يعرض مدخل التشغيل المحلي. لفتح نسخة الإنترنت استخدم رابط حساب الاستضافة بعد رفع الملفات وإعداد الموقع؛ ملف `Open-Alwaysdata.cmd` في المشروع يفتح رابط حساب o0nzee فقط، ولا يرفع الملفات.

## 1. إنشاء ملف النشر على جهازك

من PowerShell داخل مجلد المشروع:

```powershell
./scripts/Publish-Alwaysdata.ps1 -PublicUrl https://ACCOUNT.alwaysdata.net
```

على هذا الجهاز الحزم موجودة محليًا؛ يمكن استخدام `-Offline`. السكربت ينشئ مجلد إصدار جديدًا داخل `artifacts/alwaysdata` وفيه `graduation-alwaysdata.zip` ومجلد `site` ودليل الإعداد. لا يحتاج رفع مجلد المشروع كاملًا أو تثبيت .NET داخل حساب الاستضافة.

معامل `-PublicUrl` يضبط روابط الواجهة وإعدادات حزمة النشر على رابط حسابك، ويضيفه كقيمة افتراضية في ملف تشغيل الاستضافة. إعداد `Platform__PublicUrl` من لوحة Alwaysdata يتجاوز هذه القيمة إذا غيّرت الدومين لاحقًا. إعدادات التشغيل المحلي في المشروع تبقى كما هي.

## 2. إعداد الحساب

افتح https://admin.alwaysdata.com ثم اختر حساب الاستضافة. **ACCOUNT** في الأوامر التالية هو اسم حساب الاستضافة الظاهر في اللوحة، وليس البريد الإلكتروني المستخدم للدخول.

- من **Environment → .NET** اختر **8.0**.
- من **Environment → Python** اختر إصدارًا من **3.10** أو أحدث؛ 3.12 مناسب.
- من **Remote access → SSH** استخدم بيانات مستخدم SSH الظاهرة في حسابك.

## 3. رفع الملفات

في FileZilla أو عميل SFTP استخدم:

| الحقل | القيمة |
| --- | --- |
| Protocol | SFTP |
| Host | ssh-ACCOUNT.alwaysdata.net |
| Port | 22 |
| Username | مستخدم SSH الظاهر في لوحة الحساب |
| Password | كلمة مرور مستخدم SSH، تُدخل في البرنامج فقط |

ارفع **محتويات مجلد site** إلى `/home/ACCOUNT/www/graduation`، بحيث يكون `GraduationPlatform.dll` داخل هذا المجلد مباشرة. يمكن بدلًا من ذلك رفع ملف ZIP إلى مجلد الحساب، ثم فكّه من SSH:

```sh
mkdir -p /home/ACCOUNT/www/graduation
unzip /home/ACCOUNT/graduation-alwaysdata.zip -d /home/ACCOUNT/www/graduation
```

ملف التشغيل يعمل عبر `/bin/sh`، فلا يحتاج تغيير صلاحية التنفيذ. تأكد من نقل الملفات بوضع Binary أو SFTP حتى لا تُعدّل نهايات السطور.

## 4. إنشاء الموقع

من **Web → Sites** افتح الموقع الذي يستخدم عنوان حسابك وعدّله، أو استخدم **Add a site** إذا لم يوجد موقع لهذا العنوان. أدخل:

| الحقل | القيمة |
| --- | --- |
| Name | منصة الكلية |
| Addresses | ACCOUNT.alwaysdata.net، أو الدومين الذي أضفته لحسابك |
| Type | .NET |
| Working directory | /home/ACCOUNT/www/graduation |
| Command | /bin/sh /home/ACCOUNT/www/graduation/start-alwaysdata.sh |

في **Environment variables** أضف، بعد استبدال ACCOUNT:

```text
Platform__PublicUrl=https://ACCOUNT.alwaysdata.net
Platform__DataDirectory=/home/ACCOUNT/graduation-data
Similarity__PythonExecutable=python3
Bootstrap__AdminPassword=REPLACE_WITH_YOUR_OWN_STRONG_PASSWORD
```

اختر كلمة مرور إدارة خاصة بك، من 12 حرفًا على الأقل، وأدخلها في لوحة Alwaysdata. لا تضع كلمة المرور الحقيقية في GitHub أو هذا الدليل. تُستخدم عند أول تشغيل لقاعدة جديدة، وبعد إنشاء الإدارة يمكنك إزالة `Bootstrap__AdminPassword` من بيئة الموقع.

ملف التشغيل يضبط وضع Production، ويوقف الحسابات التجريبية، ويستمع على `IP` و`PORT` اللذين توفرهما Alwaysdata، مع دعم عناوين IPv6. كما يضبط اسم الموقع المقبول وتهيئة HTTPS خلف البروكسي.

من إعدادات الموقع فعّل **إعادة توجيه HTTP إلى HTTPS**. هذا مطلوب لأن ملف التشغيل يستخدم `Platform__UseHttpsProxy=true`، حيث تنتهي وصلة HTTPS عند بروكسي Alwaysdata ويصل الطلب إلى التطبيق داخليًا عبر HTTP.

## 5. فتح الموقع

احفظ الإعدادات ثم أعد تشغيل الموقع من **Web → Sites**. افتح:

```text
https://ACCOUNT.alwaysdata.net/
https://ACCOUNT.alwaysdata.net/api/health
```

تسجيل الدخول الأول يكون باسم المستخدم **admin** وكلمة المرور التي أدخلتها. أنشئ المواد وحسابات المشرفين من حساب الإدارة. `api/health` يعرض `database: SQLite`، ومحرك التحليل الطبيعي هو `python-tfidf`.

لا تحتاج إنشاء قاعدة MySQL في Alwaysdata. التطبيق ينشئ SQLite داخل `/home/ACCOUNT/graduation-data`، مع ملفات المواد والتسليمات ومفاتيح الجلسات والنسخ الاحتياطية. تأكد من وجود مساحة كافية لملفات المواد والنسخ مع مرور الوقت.

## 6. تحديث الموقع لاحقًا

شغّل سكربت النشر مرة أخرى، أوقف الموقع مؤقتًا من اللوحة، وارفع محتويات الإصدار الجديد إلى مجلد البرنامج، ثم أعد تشغيله. احتفظ بمجلد `graduation-data` خارج مجلد البرنامج؛ لا تستبدله أثناء رفع تحديث الكود. رفع التعديل إلى GitHub وحده لا يحدّث هذا الموقع تلقائيًا بهذه الطريقة.

## نقل البيانات الحالية

إذا أردت نقل الحسابات والمواد الحالية، أنشئ نسخة احتياطية من **البريد والنسخ الاحتياطية** في حساب الإدارة المحلي، ثم استعدها في مجلد البيانات على الاستضافة والموقع متوقف. انقل قاعدة البيانات وملفات uploads ومفاتيح keys معًا. تغيير `DemoData` إلى false لا يغيّر كلمات مرور الحسابات التجريبية الموجودة في قاعدة قديمة؛ غيّرها أو عطّلها قبل إتاحة هذه البيانات على الإنترنت.

## لو ظهر خطأ

- **502 أو Connection to upstream**: راجع سجل الموقع داخل `/home/ACCOUNT/admin/logs/sites`، وتأكد من اختيار .NET 8 ومن المسار الصحيح. ملف التشغيل يستخدم المنفذ المخصص من Alwaysdata بدل 5000.
- **Invalid Hostname / 400**: اضبط `Platform__PublicUrl` على رابط الموقع الصحيح. عند استخدام أكثر من دومين، أضف `AllowedHosts` بأسماء الدومينات مفصولة بفاصلة منقوطة.
- **تعذر إنشاء الإدارة**: أدخل `Bootstrap__AdminPassword` من 12 حرفًا على الأقل قبل أول تشغيل لقاعدة جديدة.
- **محرك التحليل البديل**: راجع `Similarity__PythonExecutable` وتأكد أن `python3 --version` يعمل في SSH بإصدار 3.10 أو أحدث.
- **database already used**: أوقف أي موقع آخر يستخدم مجلد البيانات نفسه؛ يجب تشغيل نسخة واحدة على SQLite.

## المراجع الرسمية

- [.NET على Alwaysdata](https://help.alwaysdata.com/en/docs/web-hosting/languages/dotnet/configuration/)
- [بيانات SFTP وSSH](https://help.alwaysdata.com/en/docs/technical-specifications/login-details/)
- [Python على Alwaysdata](https://help.alwaysdata.com/en/docs/web-hosting/languages/python/configuration/)
- [إعادة توجيه HTTPS](https://help.alwaysdata.com/en/docs/web-hosting/sites/ssl-tls/redirect-http-to-https/)
