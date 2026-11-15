namespace GraduationPlatform.Infrastructure;

public static class ErrorMessages
{
    private static readonly Dictionary<string,string> Messages=new()
    {
        ["Username is already taken"]="اسم المستخدم مستخدم بالفعل",["Email is already registered"]="البريد الإلكتروني مسجل بالفعل",
        ["A valid email address is required"]="يرجى إدخال بريد إلكتروني صالح",["Student is already registered in a graduation team"]="الطالب عضو في فريق بالفعل",
        ["Invited student is already enrolled in a team"]="الطالب المدعو عضو في فريق بالفعل",["Team has reached its maximum member limit"]="الفريق وصل إلى الحد الأقصى للأعضاء",
        ["A pending invitation has already been sent to this student"]="توجد دعوة معلقة لهذا الطالب بالفعل",["You cannot invite yourself to your team"]="لا يمكنك دعوة نفسك",
        ["Invitation is no longer pending"]="تم الرد على الدعوة أو إلغاؤها بالفعل",["You are not authorized to respond to this invitation"]="الدعوة تخص طالبًا آخر",
        ["You are already enrolled in another graduation team"]="أنت عضو في فريق آخر بالفعل",["Due date cannot be earlier than start date"]="موعد التسليم يجب أن يكون بعد بداية المهمة",
        ["Milestone belongs to another team"]="المرحلة تخص فريقًا آخر",["Invalid milestone for this team"]="المرحلة لا تخص هذا الفريق",
        ["Invalid or expired reset token"]="رابط استعادة كلمة المرور غير صالح أو تم استخدامه",["Password reset token has expired"]="انتهت صلاحية رابط استعادة كلمة المرور",
        ["Current password is incorrect"]="كلمة المرور الحالية غير صحيحة",["Transfer leadership before removing the team leader"]="انقل قيادة الفريق قبل إزالة قائده",
        ["The last active administrator cannot be removed"]="يجب الاحتفاظ بحساب إدارة نشط واحد على الأقل",["Quota cannot be below the current workload"]="سعة الإشراف يجب ألا تقل عن عدد الفرق الحالية",
        ["An approved idea must be reopened by the supervisor before editing"]="يجب أن يطلب المشرف تعديلات قبل تغيير فكرة معتمدة",["Invalid task details"]="بيانات المهمة غير صالحة"
    };
    public static string Localize(string message)
    {
        if(Messages.TryGetValue(message,out var localized))return localized;
        if(message.StartsWith("Supervisor quota exceeded!"))return "المشرف وصل إلى الحد الأقصى للفرق؛ اختر مشرفًا آخر أو عدّل سعته";
        return message;
    }
}
