# 🎓 منصة مشروعات التخرج الموحدة (Graduation Project Platform)
### هيكلية برمجية مفصولة بالكامل (Backend / Frontend / UI_UX Separation)

---

## 🏗️ أولاً: الفصل التام بين الطبقات الثلاث مع استمرار ترابطها

تمت إعادة هيكلة المشروع وفصل مكوناته في 3 مجلدات رئيسية ومستقلة تماماً، بحيث يمكنك تعديل أي طبقة بمفردها دون التأثير على الطبقات الأخرى:

```text
e:\المشروع\
│
├── 📦 Backend\                   <-- الباك إند (C# / .NET / Logic & DB Models)
│   ├── Common\                   --> نمط Result Pattern الموحد
│   ├── Member1_UserManagement\   --> إدارة المستخدمين والتشفير والصلاحيات (RBAC)
│   ├── Member2_ProjectIdeas\     --> أفكار المشروعات ومحرك فحص التشابه بالذكاء الاصطناعي
│   ├── Member3_TeamManagement\   --> إدارة الفرق وتوزيع المشرفين وكوتة الإشراف (Max 3)
│   ├── Member4_ProjectTracking\  --> متابعة المراحل والمهام ولوحة الـ Kanban والإنذارات
│   └── ApiControllers\           --> خادم الـ REST API وموجه الطلبات
│
├── 💻 Frontend\                  <-- الفرونت إند (Pages & Scripts & State)
│   ├── Pages\
│   │   ├── index.html            --> بوابة الدخول الرئيسية (Landing)
│   │   ├── student.html          --> بوابة تطبيق ويب الطلاب (Student Web App)
│   │   ├── supervisor.html       --> بوابة تطبيق ويب المشرفين (Supervisor Web App)
│   │   └── admin.html            --> الداش بورد (Admin Dashboard)
│   └── Scripts\
│       ├── apiService.js         --> طبقة الاتصال البرمجي وإدارة الحالة (API Layer)
│       ├── studentController.js  --> منطق تحكم صفحات الطلاب والتفاعل
│       ├── supervisorController.js -> منطق تحكم صفحات المشرفين والاعتماد
│       └── adminController.js    --> منطق تحكم الداش بورد وكوتة المشرفين
│
├── 🎨 UI_UX\                     <-- التصميم وواجهة وتجربة المستخدم (Design System)
│   ├── Styles\
│   │   ├── design-system.css     --> متغيرات الألوان، الخطوط، المسافات، والـ Reset
│   │   ├── components.css        --> الأزرار، الكروت، الحقول، الشارات، ولوحة Kanban
│   │   └── dashboard.css         --> القائمة الجانبية، كروت الـ KPI، ومؤشرات الأداء
│   ├── Assets\                   --> الأيقونات والصور والعناصر الرسومية
│   └── ui-tokens.json            --> ملف إعدادات ورموز التصميم لمصممي الـ UI/UX
│
├── 🧪 tests\                     <-- الاختبارات المنفصلة والتكاملية
│   ├── Member1_Tests.cs & .exe   --> اختبارات العضو الأول (9/9)
│   ├── Member2_Tests.cs & .exe   --> اختبارات العضو الثاني (8/8)
│   ├── Member3_Tests.cs & .exe   --> اختبارات العضو الثالث (9/9)
│   ├── Member4_Tests.cs & .exe   --> اختبارات العضو الرابع (8/8)
│   ├── Integration_Tests.cs      --> الاختبار التكاملي الشامل (1/1)
│   ├── TestRunner.cs             --> مشغل الاختبارات المجمع
│   └── RunAllTests.exe           --> تشغيل كل الاختبارات دفعة واحدة (35/35 نجاح)
│
├── WebServer.exe                 <-- خادم الويب الشامل
├── GraduationPlatform.sln        <-- ملف الحل لـ Visual Studio
└── README.md
```

---

## 🔗 كيف ترتبط الطبقات ببعضها مع إمكانية تعديل كل منها بمفردها؟

1. **إذا أردت تعديل التصميم والشكل (UI / UX):**
   - افتح مجلد `UI_UX\Styles\`:
     - لتعديل الألوان أو الخطوط أو الهوامش: عدل `design-system.css`.
     - لتعديل شكل الأزرار أو الكروت أو لوحة الـ Kanban: عدل `components.css`.
     - لتعديل شكل القائمة الجانبية أو كروت الإحصائيات بالداش بورد: عدل `dashboard.css`.
   - **النتيجة:** ستنعكس تعديلاتك فوراً على كافة صفحات الويب دون لمس سطر كود واحد في الـ JavaScript أو الـ C#.

2. **إذا أردت تعديل وظائف الواجهات الأمامية (Frontend Logic):**
   - افتح مجلد `Frontend\Scripts\`:
     - لتعديل سلوك صفحات الطلاب: عدل `studentController.js`.
     - لتعديل سلوك صفحات المشرفين: عدل `supervisorController.js`.
     - لتعديل سلوك الداش بورد: عدل `adminController.js`.
   - أو افتح `Frontend\Pages\` لتعديل بنية الصفحات (HTML).

3. **إذا أردت تعديل الباك إند وقواعد البيانات (Backend Logic & Models):**
   - افتح مجلد `Backend\`:
     - لتعديل المستخدمين والصلاحيات: `Backend\Member1_UserManagement\`.
     - لتعديل خوارزمية الذكاء الاصطناعي لفحص الأفكار: `Backend\Member2_ProjectIdeas\SimilarityEngine.cs`.
     - لتعديل كوتة المشرفين أو الفرق: `Backend\Member3_TeamManagement\TeamService.cs`.
     - لتعديل حساب المهام والمراحل: `Backend\Member4_ProjectTracking\TrackingService.cs`.

---

## 🚀 تشغيل النظام والاختبارات

### 1. تشغيل صفحات الويب مباشرة في المتصفح:
- الرئيسية: `Frontend\Pages\index.html`
- بوابة الطلاب: `Frontend\Pages\student.html`
- بوابة المشرفين: `Frontend\Pages\supervisor.html`
- الداش بورد: `Frontend\Pages\admin.html`

### 2. تشغيل خادم الويب والباك إند المتكامل:
```powershell
.\WebServer.exe
```
سيقوم بتشغيل السيرفر وربط الـ Frontend والـ UI_UX بالـ Backend عبر:
`http://localhost:5000/`

### 3. تشغيل كافة اختبارات الـ Backend (35/35 PASSED):
```powershell
.\tests\RunAllTests.exe
```
