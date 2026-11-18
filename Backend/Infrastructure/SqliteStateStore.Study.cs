using Microsoft.Data.Sqlite;

namespace GraduationPlatform.Infrastructure;

public sealed partial class SqliteStateStore
{
    public List<StudyDepartment> LoadStudyDepartments()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,name FROM study_departments ORDER BY rowid";
        using var reader = cmd.ExecuteReader(); var result = new List<StudyDepartment>();
        while (reader.Read()) result.Add(new() { Id = reader.GetString(0), Name = reader.GetString(1) });
        return result;
    }

    private static List<StudyCourse> LoadStudyCourses(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,department_id,study_year,semester,name,description FROM study_courses ORDER BY rowid";
        using var reader = cmd.ExecuteReader(); var result = new List<StudyCourse>();
        while (reader.Read()) result.Add(new() { Id = reader.GetString(0), DepartmentId = reader.GetString(1),
            StudyYear = reader.GetInt32(2), Semester = reader.GetInt32(3), Name = reader.GetString(4), Description = reader.GetString(5) });
        return result;
    }

    private static List<StudyResource> LoadStudyResources(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,course_id,kind,title,exam_year,original_file_name,content_type,size_bytes,sha256,uploaded_by,created_at FROM study_resources ORDER BY rowid";
        using var reader = cmd.ExecuteReader(); var result = new List<StudyResource>();
        while (reader.Read()) result.Add(new() { Id = reader.GetString(0), CourseId = reader.GetString(1), Kind = reader.GetString(2),
            Title = reader.GetString(3), ExamYear = reader.GetString(4), OriginalFileName = reader.GetString(5), ContentType = reader.GetString(6),
            SizeBytes = reader.GetInt64(7), Sha256 = reader.GetString(8), UploadedBy = reader.GetString(9),
            CreatedAt = DateTime.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) });
        return result;
    }

    private static void WriteStudyLibrary(SqliteConnection db, SqliteTransaction tx, PlatformSnapshot state)
    {
        foreach (var course in state.StudyCourses) InsertStudyRow(db, tx, "study_courses", ("id", course.Id),
            ("department_id", course.DepartmentId), ("study_year", course.StudyYear), ("semester", course.Semester),
            ("name", course.Name), ("description", course.Description));
        foreach (var item in state.StudyResources) InsertStudyRow(db, tx, "study_resources", ("id", item.Id), ("course_id", item.CourseId),
            ("kind", item.Kind), ("title", item.Title), ("exam_year", item.ExamYear), ("original_file_name", item.OriginalFileName),
            ("content_type", item.ContentType), ("size_bytes", item.SizeBytes), ("sha256", item.Sha256), ("uploaded_by", item.UploadedBy),
            ("created_at", item.CreatedAt.ToString("o")));
    }

    private static void InsertStudyRow(SqliteConnection db, SqliteTransaction tx, string table, params (string Column, object Value)[] fields)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        var parameters = fields.Select((f, i) => "$p" + i).ToArray();
        for (int i = 0; i < fields.Length; i++) cmd.Parameters.AddWithValue(parameters[i], fields[i].Value ?? DBNull.Value);
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(',', fields.Select(f => f.Column))}) VALUES ({string.Join(',', parameters)})";
        cmd.ExecuteNonQuery();
    }
}
