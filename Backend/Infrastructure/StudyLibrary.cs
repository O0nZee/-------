using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Member1_UserManagement;

namespace GraduationPlatform.Infrastructure;

public sealed class StudyDepartment
{
    public string Id { get; set; }
    public string Name { get; set; }
}

public sealed class StudyCourse
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DepartmentId { get; set; }
    public int StudyYear { get; set; }
    public int Semester { get; set; }
    public string Name { get; set; }
    public string Description { get; set; } = "";
}

public sealed class StudyResource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; }
    public string Kind { get; set; }
    public string Title { get; set; }
    public string ExamYear { get; set; } = "";
    public string OriginalFileName { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; }
    public string UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed partial class PlatformRuntime
{
    public List<StudyDepartment> StudyDepartments { get; private set; } = new();
    public List<StudyCourse> StudyCourses { get; private set; } = new();
    public List<StudyResource> StudyResources { get; private set; } = new();

    public static int ValidateStudyYear(int? year)
    {
        if (year is not (>= 1 and <= 4)) throw new PlatformException("يرجى اختيار الفرقة من الأولى إلى الرابعة");
        return year.Value;
    }

    public string ValidateStudyDepartment(string name)
    {
        name = Input.Text(name, "القسم", 100);
        return StudyDepartments.FirstOrDefault(d => d.Name == name)?.Name
            ?? throw new PlatformException("يرجى اختيار قسم من الأقسام المتاحة");
    }

    public object StudyCatalog() => new
    {
        departments = StudyDepartments,
        years = new[] { new { value = 1, name = "الفرقة الأولى" }, new { value = 2, name = "الفرقة الثانية" },
            new { value = 3, name = "الفرقة الثالثة" }, new { value = 4, name = "الفرقة الرابعة" } }
    };

    public object StudyLibrary(User actor, string departmentId = null, int? studyYear = null)
    {
        // Students always read the scope saved in their profile, regardless of query parameters.
        if (actor.Role == UserRoleType.Student)
        {
            departmentId = StudyDepartments.FirstOrDefault(d => d.Name == actor.Profile.Department)?.Id;
            studyYear = actor.Profile.StudyYear;
        }
        else
        {
            if (!string.IsNullOrEmpty(departmentId) && !StudyDepartments.Any(d => d.Id == departmentId))
                throw new PlatformException("القسم غير موجود");
            if (studyYear != null) ValidateStudyYear(studyYear);
        }
        bool needsProfile = actor.Role == UserRoleType.Student && (departmentId == null || studyYear is not (>= 1 and <= 4));
        var courses = needsProfile ? new List<StudyCourse>() : StudyCourses.Where(c =>
            (string.IsNullOrEmpty(departmentId) || c.DepartmentId == departmentId) && (studyYear == null || c.StudyYear == studyYear))
            .OrderBy(c => c.StudyYear).ThenBy(c => c.Semester).ThenBy(c => c.Name).ToList();
        return new
        {
            department = StudyDepartments.FirstOrDefault(d => d.Id == departmentId), studyYear, needsProfile,
            courses = courses.Select(c => new { c.Id, c.Name, c.DepartmentId, c.StudyYear, c.Semester, c.Description,
                departmentName = StudyDepartments.FirstOrDefault(d => d.Id == c.DepartmentId)?.Name,
                resources = StudyResources.Where(s => s.CourseId == c.Id).OrderByDescending(s => s.CreatedAt).Select(StudyResourceView) })
        };
    }

    public static object StudyResourceView(StudyResource item) => new
    {
        item.Id, item.CourseId, item.Kind, item.Title, item.ExamYear, item.OriginalFileName, item.SizeBytes, item.CreatedAt,
        downloadUrl = "/api/study/resources/" + item.Id + "/download"
    };

    public StudyCourse SaveStudyCourse(User actor, StudyCourseRequest request, string id = null)
    {
        RequireCollegeAdmin(actor);
        if (!StudyDepartments.Any(d => d.Id == request.DepartmentId)) throw new PlatformException("يرجى اختيار القسم");
        int year = ValidateStudyYear(request.StudyYear);
        if (request.Semester is not (1 or 2)) throw new PlatformException("يرجى اختيار الفصل الدراسي الأول أو الثاني");
        string name = Input.Text(request.Name, "اسم المادة", 150);
        string description = Input.Text(request.Description, "وصف المادة", 2000, false);
        if (StudyCourses.Any(c => c.Id != id && c.DepartmentId == request.DepartmentId && c.StudyYear == year && c.Semester == request.Semester && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new PlatformException("المادة مضافة بالفعل لهذا القسم والفرقة والفصل الدراسي", 409, "duplicate_course");
        var course = id == null ? new StudyCourse() : StudyCourses.FirstOrDefault(c => c.Id == id)
            ?? throw new PlatformException("المادة غير موجودة", 404, "not_found");
        course.Name = name; course.Description = description; course.DepartmentId = request.DepartmentId;
        course.StudyYear = year; course.Semester = request.Semester;
        if (id == null) StudyCourses.Add(course);
        return course;
    }

    public StudyCourse AccessStudyCourse(User actor, string id)
    {
        var course = StudyCourses.FirstOrDefault(c => c.Id == id) ?? throw new PlatformException("المادة غير موجودة", 404, "not_found");
        if (actor.Role == UserRoleType.Student && (course.StudyYear != actor.Profile.StudyYear ||
            StudyDepartments.FirstOrDefault(d => d.Id == course.DepartmentId)?.Name != actor.Profile.Department))
            throw new PlatformException("هذه المادة لا تخص قسمك وفرقتك", 403, "forbidden");
        return course;
    }
}
