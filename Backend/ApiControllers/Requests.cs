using System.Text.RegularExpressions;
using System.Globalization;
using GraduationPlatform.Infrastructure;

namespace GraduationPlatform.Backend.ApiControllers;

public sealed class RegisterRequest
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string FullName { get; set; }
    public string Department { get; set; }
    public int? StudyYear { get; set; }
    public string AcademicId { get; set; }
    public string Role { get; set; }
}
public sealed class LoginRequest { public string UsernameOrEmail { get; set; } public string Password { get; set; } }
public sealed class ForgotRequest { public string Email { get; set; } }
public sealed class ResetRequest { public string Token { get; set; } public string NewPassword { get; set; } }
public sealed class ProfileRequest { public string FullName { get; set; } public string Department { get; set; } public int? StudyYear { get; set; } public string Phone { get; set; } public string Bio { get; set; } }
public sealed class PasswordRequest { public string CurrentPassword { get; set; } public string NewPassword { get; set; } }
public sealed class TeamRequest { public string Name { get; set; } public int? MaxMembers { get; set; } public string LeaderStudentId { get; set; } }
public sealed class InvitationRequest { public string Email { get; set; } public string ProposedRole { get; set; } }
public sealed class InvitationResponseRequest { public bool? Accept { get; set; } }
public sealed class AssignmentRequest { public string SupervisorId { get; set; } }
public sealed class SupervisorRequest { public int MaxTeams { get; set; } public string Specialization { get; set; } }
public sealed class SupervisorPermissionsRequest { public List<string> PermissionIds { get; set; } }
public sealed class IdeaRequest
{
    public string TeamId { get; set; } public string Title { get; set; } public string Description { get; set; }
    public string Objectives { get; set; } public List<string> Technologies { get; set; } public string Track { get; set; }
    public string ExcludeIdeaId { get; set; }
}
public sealed class ReviewRequest { public string Decision { get; set; } public string Feedback { get; set; } public decimal? Grade { get; set; } }
public sealed class TaskRequest
{
    public string TeamId { get; set; } public string MilestoneId { get; set; } public string Title { get; set; } public string Description { get; set; }
    public string Priority { get; set; } public DateTime? StartDate { get; set; } public DateTime? DueDate { get; set; }
    public List<string> AssignedStudentIds { get; set; }
}
public sealed class TaskStatusRequest { public string Status { get; set; } }
public sealed class UserUpdateRequest { public bool? IsActive { get; set; } public string Role { get; set; } public string FullName { get; set; } public string Department { get; set; } public int? StudyYear { get; set; } }
public sealed class StudyCourseRequest
{
    public string Name { get; set; }
    public string DepartmentId { get; set; }
    public int StudyYear { get; set; }
    public int Semester { get; set; }
    public string Description { get; set; }
}
public sealed class ReminderRequest { public string Message { get; set; } }
public sealed class ArchiveRequest { public decimal? Grade { get; set; } public string AcademicYear { get; set; } }
public sealed class HistoricalProjectRequest
{
    public string Title { get; set; } public string Abstract { get; set; } public string AcademicYear { get; set; } public string Department { get; set; }
    public string Track { get; set; } public List<string> Technologies { get; set; } public string SupervisorName { get; set; } public string Grade { get; set; }
}

public static class Input
{
    public static string Text(string value,string label,int max=200,bool required=true)
    {
        if(required && string.IsNullOrWhiteSpace(value)) throw new PlatformException("يرجى إدخال "+label);
        value=(value??"").Trim();
        if(value.Length>max || value.Contains('\0')) throw new PlatformException(label+" أطول من الحد المسموح أو يحتوي على قيمة غير صالحة");
        return value;
    }
    public static string Password(string value)
    {
        if(string.IsNullOrWhiteSpace(value) || value.Length<8 || value.Length>128) throw new PlatformException("كلمة المرور يجب أن تكون بين ٨ و١٢٨ حرفًا");
        return value;
    }
    public static string Username(string value)
    {
        value=Text(value,"اسم المستخدم",50);
        if(!Regex.IsMatch(value,@"^[\p{L}\p{N}_.-]{3,50}$")) throw new PlatformException("اسم المستخدم: ٣ أحرف على الأقل؛ حروف وأرقام ونقطة أو شرطة فقط");
        return value;
    }
    public static T EnumValue<T>(string value,string label) where T:struct,Enum
    {
        if(!Enum.TryParse<T>(value,true,out var result) || !Enum.IsDefined(result) || !string.Equals(Enum.GetName(result),value,StringComparison.OrdinalIgnoreCase))
            throw new PlatformException(label+" غير صالح");
        return result;
    }
    public static List<string> Technologies(List<string> values)
    {
        if(values==null) return new();
        if(values.Count>20) throw new PlatformException("الحد الأقصى ٢٠ تقنية");
        return values.Select(v=>Text(v,"اسم التقنية",60)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string AcademicYear(string value)
    {
        value=Text(value,"العام الدراسي",9).Replace('–','-').Replace('—','-');
        value=string.Concat(value.Select(c=>c is >= '٠' and <= '٩'?(char)('0'+c-'٠')
            :c is >= '۰' and <= '۹'?(char)('0'+c-'۰'):c));
        if(!Regex.IsMatch(value,@"^[0-9]{4}-[0-9]{4}$")
            || !int.TryParse(value[..4],NumberStyles.None,CultureInfo.InvariantCulture,out var first)
            || !int.TryParse(value[5..],NumberStyles.None,CultureInfo.InvariantCulture,out var last)
            || last-first!=1)
            throw new PlatformException("العام الدراسي يجب أن يكون عامين متتاليين، مثل 2026-2027");
        return value;
    }

    public static DateTime UtcDate(DateTime value,string label)
    {
        if(value.Kind==DateTimeKind.Unspecified)
            throw new PlatformException(label+": أرسل الموعد مع فرق التوقيت أو بتوقيت UTC متبوعًا بـ Z");
        return value.ToUniversalTime();
    }
}
