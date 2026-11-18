# قاعدة بيانات الطلبة — SQLite

`schema.sql` هو تعريف جداول المنصة. التطبيق يضمّنه وقت البناء وينفّذه عند التشغيل، لذلك لا يعتمد على مكان تشغيل الملف التنفيذي. تشغيله مرة أخرى يحافظ على السجلات الموجودة.

البيانات الفعلية تُحفظ في `../data/graduation.db` تلقائيًا عند التسجيل أو تعديل الملف الشخصي، وتبقى بعد إغلاق التطبيق وإعادة تشغيله. إعداد `Platform:DataDirectory` يمكنه تغيير هذا المسار.

## عرض بيانات الطلبة

افتح قاعدة البيانات ببرنامج يدعم SQLite، مثل DB Browser for SQLite، ثم نفّذ:

```sql
SELECT id, username, full_name, email, department, study_year,
       academic_identifier, phone, is_active, created_at, updated_at
FROM students
ORDER BY full_name;
```

`students` عرض SQL مرتبط بجدول `users`؛ يعرض الطلبة فقط ويتحدث من بيانات الحساب المحفوظة، بدون نسخة ثانية قد تختلف عنها. لا يعرض كلمات المرور أو بيانات التحقق منها. `academic_identifier` هو الرقم الجامعي، و`is_active` يساوي 1 للحساب النشط و0 للحساب المعطّل.

`study_year` هو رقم الفرقة من 1 إلى 4. الحسابات القديمة قد تحتوي على NULL حتى يُحدد الطالب فرقته من «حسابي».

## المواد والملخصات والامتحانات السابقة

الجداول الجديدة:

| الجدول | المحتوى |
| --- | --- |
| study_departments | الأقسام الثمانية ومعرفاتها |
| study_courses | المادة والقسم والفرقة والفصل الدراسي والوصف |
| study_resources | المحاضرات والملخصات والامتحانات، مع معلومات الملف والعام الدراسي |

```sql
SELECT id, name FROM study_departments;
SELECT c.id, c.name, d.name AS department, c.study_year, c.semester
FROM study_courses c JOIN study_departments d ON d.id = c.department_id;
SELECT course_id, kind, title, exam_year, original_file_name FROM study_resources;
```

يمكن إضافة المحتوى من حساب الإدارة عبر «المواد الدراسية ← إضافة مادة»، ثم «إضافة ملف» داخل المادة. نوع الملف في قاعدة البيانات هو `Material` للمحاضرات، و`Summary` للملخصات، و`Exam` للامتحانات السابقة. التطبيق يحفظ الملف الفعلي باسم معرفه مع امتداد `.bin` داخل `data/uploads`، ويتولى التحقق من الصيغة والحجم وربطه بالمادة.

لإدخال مواد مباشرة باستخدام SQL: أوقف الخادم بـ`Stop.cmd`، افتح `data/graduation.db`، وأدخل المادة بالقسم والفرقة المناسبين. المثال التالي يُدخل مادة للحاسب الآلي في الفرقة الثانية والفصل الأول؛ غيّر القيم حسب بيانات الكلية:

```sql
INSERT INTO study_courses(id, department_id, study_year, semester, name, description)
VALUES(lower(hex(randomblob(16))), 'computer-ar', 2, 1, 'قواعد البيانات', '');
```

بعد تعديل SQL شغّل `Start.cmd` لقراءة البيانات الجديدة. أضف ملفات المواد من واجهة الإدارة حتى تُحفظ الملفات الفعلية ومعلوماتها معًا. النسخ الاحتياطية للمنصة تشمل الجداول والملفات.

## تصدير بيانات الطلبة إلى SQL

من فولدر المشروع، بعد تشغيل النسخة المبنية بالتعديل:

```powershell
python "data base/export_students.py"
```

النتيجة `students-data.sql`: نسخة محلية من ملفات الطلبة وقت التصدير، في جدول مستقل باسم `student_records`. التصدير لا يغيّر حسابات التطبيق. لتحديث النسخة، شغّل الأمر مرة أخرى. إذا كان مسار البيانات مختلفًا:

```powershell
python "data base/export_students.py" --database "D:/platform-data/graduation.db"
```

استورد `students-data.sql` في قاعدة SQLite منفصلة، ثم استخدم:

```sql
SELECT * FROM student_records;
```

هذا تصدير لبيانات الملف الشخصي، وليس نسخة لاستعادة الدخول إلى الحسابات. لاستعادة المنصة بالكامل، استخدم النسخ الاحتياطية الموجودة في التطبيق. تعريف الجداول وسكربت التصدير يمكن مشاركتهما مع التيم؛ ملف بيانات الطلبة وقاعدة التشغيل مستبعدان من Git.
