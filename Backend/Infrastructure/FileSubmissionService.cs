using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Member1_UserManagement;

namespace GraduationPlatform.Infrastructure;

public sealed class FileSubmissionService
{
    private readonly PlatformRuntime _runtime;
    private static readonly Dictionary<string,string> Types=new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]="application/pdf",[".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"]="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",[".pptx"]="application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"]="application/zip",[".txt"]="text/plain",[".md"]="text/markdown",[".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg"
    };
    public FileSubmissionService(PlatformRuntime runtime) { _runtime=runtime; }

    public async Task<string> SaveAsync(string actorId,string teamId,string milestoneId,string title,IFormFile file,CancellationToken cancellationToken)
    {
        title=Input.Text(title,"عنوان التسليم",200);
        if(file==null || file.Length==0) throw new PlatformException("يرجى اختيار ملف غير فارغ");
        long limit=(long)_runtime.Options.MaxUploadMegabytes*1024*1024;
        if(file.Length>limit) throw new PlatformException($"الحد الأقصى للملف {_runtime.Options.MaxUploadMegabytes} ميجابايت",413,"file_too_large");
        string name=Input.Text(file.FileName.Replace('\\','/').Split('/').Last(),"اسم الملف",150);
        string extension=Path.GetExtension(name);
        if(!Types.TryGetValue(extension,out var contentType)) throw new PlatformException("صيغة الملف غير مدعومة. استخدم PDF أو ملفات Office أو ZIP أو نصًا أو صورة");
        _runtime.Read(r=>{Validate(r,r.Actor(actorId),teamId,milestoneId);return true;});
        var submission=new ProjectSubmission{TeamId=teamId,MilestoneId=milestoneId,UploadedBy=actorId,Title=title,OriginalFileName=name,ContentType=contentType};
        submission.StoredFileName=submission.Id+".bin";
        string path=Path.Combine(_runtime.UploadDirectory,submission.StoredFileName);
        try
        {
            using(var source=file.OpenReadStream())
            using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer=new byte[65536];int read;
                while((read=await source.ReadAsync(buffer,cancellationToken))>0)
                {
                    submission.SizeBytes+=read;
                    if(submission.SizeBytes>limit) throw new PlatformException("الملف أكبر من الحد المسموح",413,"file_too_large");
                    hash.AppendData(buffer,0,read);await target.WriteAsync(buffer.AsMemory(0,read),cancellationToken);
                }
                await target.FlushAsync(cancellationToken);target.Flush(true);
                submission.Sha256=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if(submission.SizeBytes==0) throw new PlatformException("يرجى اختيار ملف غير فارغ");
            ValidateSignature(path,extension);
            return _runtime.Mutate(actorId,"submission.upload",submission.Id,r=>
            {
                var actor=r.Actor(actorId);var team=Validate(r,actor,teamId,milestoneId);
                r.Submissions.Add(submission);
                var supervisor=r.Teams.GetSupervisor(team.AssignedSupervisorId??"");
                if(supervisor!=null) r.Notify(supervisor.UserId,"تسليم مرحلة جديد",$"رفع فريق «{team.Name}» تسليم «{title}» للمراجعة.","SubmissionUploaded",team.Id);
                return r.SubmissionView(submission);
            },"تم رفع الملف وحفظ التسليم للمراجعة");
        }
        catch { if(File.Exists(path)) File.Delete(path);throw; }
    }

    private static GraduationPlatform.Member3_TeamManagement.Team Validate(PlatformRuntime r,User actor,string teamId,string milestoneId)
    {
        var team=r.AccessTeam(actor,teamId,true);
        if(actor.Role==UserRoleType.Supervisor) throw new PlatformException("رفع التسليمات متاح للفريق والإدارة",403,"forbidden");
        if(team.ProjectIdeaId==null || r.Ideas.GetIdeaById(team.ProjectIdeaId)?.Status!=GraduationPlatform.Member2_ProjectIdeas.IdeaStatus.Approved)
            throw new PlatformException("يجب اعتماد فكرة المشروع قبل تسليم الملفات",409,"idea_not_approved");
        if(r.Tracking.GetMilestone(milestoneId??"")?.TeamId!=team.Id) throw new PlatformException("المرحلة لا تخص هذا الفريق");
        return team;
    }

    private static void ValidateSignature(string path,string extension)
    {
        var head=new byte[8];int count;using(var source=File.OpenRead(path))count=source.ReadAtLeast(head,head.Length,false);
        bool valid=extension.ToLowerInvariant() switch
        {
            ".pdf"=>count>=5 && Encoding.ASCII.GetString(head,0,5)=="%PDF-",
            ".docx" or ".xlsx" or ".pptx" or ".zip"=>count>=4 && ValidZip(path,extension),
            ".png"=>count==8 && head.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
            ".jpg" or ".jpeg"=>count>=3 && head[0]==0xff && head[1]==0xd8 && head[2]==0xff,
            ".txt" or ".md"=>ValidText(path),
            _=>false
        };
        if(!valid) throw new PlatformException("محتوى الملف لا يطابق صيغته أو الملف غير صالح");
    }

    private static bool ValidZip(string path,string extension)
    {
        try
        {
            using var archive=ZipFile.OpenRead(path);
            if(archive.Entries.Count>10000)return false;
            if(extension.Equals(".zip",StringComparison.OrdinalIgnoreCase))return true;
            var expected=extension.ToLowerInvariant() switch
            {
                ".docx"=>("word/document.xml","application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                ".xlsx"=>("xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                ".pptx"=>("ppt/presentation.xml","application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"),
                _=>("","")
            };
            var types=archive.GetEntry("[Content_Types].xml");
            var main=archive.GetEntry(expected.Item1);
            if(types==null || main==null || types.Length>512*1024 || main.Length==0
                || archive.Entries.Count(e=>e.FullName=="[Content_Types].xml" || e.FullName==expected.Item1)!=2)return false;
            using var stream=types.Open();
            using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=512*1024});
            var document=XDocument.Load(reader);
            XNamespace ns="http://schemas.openxmlformats.org/package/2006/content-types";
            return document.Root?.Name==ns+"Types"
                && document.Root.Elements(ns+"Override").Any(e=>(string)e.Attribute("PartName")=="/"+expected.Item1 && (string)e.Attribute("ContentType")==expected.Item2);
        }
        catch(Exception ex) when(ex is InvalidDataException or XmlException or ArgumentException){return false;}
    }
    private static bool ValidText(string path)
    {
        try { var text=new UTF8Encoding(false,true).GetString(File.ReadAllBytes(path));return !text.Contains('\0'); }
        catch(DecoderFallbackException) {return false;}
    }
}
