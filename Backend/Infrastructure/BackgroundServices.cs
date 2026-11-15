using System.Net;
using System.Net.Mail;
using System.Text;
using GraduationPlatform.Member4_ProjectTracking;

namespace GraduationPlatform.Infrastructure;

public sealed class MailOptions
{
    public string Mode {get;set;}="Local";public string Host {get;set;}="";public int Port {get;set;}=587;
    public bool UseTls {get;set;}=true;public string Username {get;set;}public string Password {get;set;}
    public string FromAddress {get;set;}="graduation@localhost";public string FromName {get;set;}="منصة مشروعات التخرج";
}

public sealed class MailDeliveryService : BackgroundService
{
    private readonly PlatformRuntime _runtime;private readonly MailOptions _options;private readonly ILogger<MailDeliveryService> _logger;
    public MailDeliveryService(PlatformRuntime runtime,MailOptions options,ILogger<MailDeliveryService> logger)
    {
        _runtime=runtime;_options=options;_logger=logger;
        if(options.Mode!="Local" && options.Mode!="Smtp") throw new InvalidOperationException("Mail:Mode must be Local or Smtp.");
        if(options.Mode=="Smtp" && (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.FromAddress))) throw new InvalidOperationException("Configure SMTP host and sender before enabling SMTP mode.");
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.Mutate(null,"mail.recover",null,r=>{foreach(var m in r.Mail.Where(m=>m.Status=="Sending"))m.Status="Pending";return null;});
        while(!stoppingToken.IsCancellationRequested)
        {
            var records=_runtime.Read(r=>r.Mail.Where(m=>m.Status=="Pending" && m.NextAttemptAt<=DateTime.UtcNow).OrderBy(m=>m.CreatedAt).Take(10).ToList());
            foreach(var record in records)
            {
                if(stoppingToken.IsCancellationRequested)break;
                _runtime.Mutate(null,"mail.claim",record.Id,r=>{var m=r.Mail.First(x=>x.Id==record.Id);m.Status="Sending";m.Attempts++;return null;});
                try
                {
                    if(_options.Mode=="Local") WriteLocal(record);
                    else await SendSmtp(record,stoppingToken);
                    _runtime.Mutate(null,"mail.delivered",record.Id,r=>{var m=r.Mail.First(x=>x.Id==record.Id);m.Status="Sent";m.SentAt=DateTime.UtcNow;m.LastError=null;return null;});
                }
                catch(Exception ex) when(ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Mail delivery failed for message {MessageId}: {ErrorType}",record.Id,ex.GetType().Name);
                    _runtime.Mutate(null,"mail.failed",record.Id,r=>
                    {
                        var m=r.Mail.First(x=>x.Id==record.Id);m.Status=m.Attempts>=5?"Failed":"Pending";
                        m.NextAttemptAt=DateTime.UtcNow.AddMinutes(Math.Min(60,Math.Pow(2,m.Attempts)));
                        m.LastError="تعذر تسليم الرسالة. راجع إعدادات البريد أو اتصال الشبكة.";return null;
                    });
                }
            }
            try {await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
    private void WriteLocal(MailRecord record)
    {
        string directory=Path.Combine(_runtime.Store.DataDirectory,"mail");Directory.CreateDirectory(directory);
        string encodedSubject=Convert.ToBase64String(Encoding.UTF8.GetBytes(record.Subject));
        string body=Convert.ToBase64String(Encoding.UTF8.GetBytes(record.Body),Base64FormattingOptions.InsertLineBreaks);
        string contents=$"From: {_options.FromAddress}\r\nTo: {record.To}\r\nSubject: =?UTF-8?B?{encodedSubject}?=\r\nMessage-ID: <{record.Id}@graduation.local>\r\nDate: {record.CreatedAt:R}\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{body}\r\n";
        string path=Path.Combine(directory,record.Id+".eml"),temporary=path+".tmp";
        File.WriteAllText(temporary,contents,new UTF8Encoding(false));File.Move(temporary,path,true);
    }
    private async Task SendSmtp(MailRecord record,CancellationToken token)
    {
        using var client=new SmtpClient(_options.Host,_options.Port){EnableSsl=_options.UseTls,Timeout=15000};
        if(!string.IsNullOrWhiteSpace(_options.Username))client.Credentials=new NetworkCredential(_options.Username,_options.Password);
        using var message=new MailMessage{From=new MailAddress(_options.FromAddress,_options.FromName,Encoding.UTF8),Subject=record.Subject,Body=record.Body,BodyEncoding=Encoding.UTF8,SubjectEncoding=Encoding.UTF8};
        message.To.Add(record.To);await client.SendMailAsync(message,token);
    }
}

public sealed class MaintenanceService : BackgroundService
{
    private readonly PlatformRuntime _runtime;private readonly ILogger<MaintenanceService> _logger;
    public MaintenanceService(PlatformRuntime runtime,ILogger<MaintenanceService> logger){_runtime=runtime;_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _runtime.GenerateDeadlineNotifications();
                string directory=Path.Combine(_runtime.Store.DataDirectory,"backups");
                if(!Directory.Exists(directory) || !Directory.EnumerateFiles(directory,$"graduation-{DateTime.UtcNow:yyyyMMdd}-*.zip").Any()) _runtime.CreateBackup();
            }
            catch(Exception ex){_logger.LogError(ex,"Scheduled maintenance failed.");}
            try {await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}

public sealed partial class PlatformRuntime
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo");

    public int GenerateDeadlineNotifications()
    {
        lock(_gate)
        {
            string before=PlatformJson.Serialize(Capture());int previous=Notifications.Count;
            try
            {
                foreach(var team in Teams.GetAllTeams().Where(t=>!t.IsArchived))
                {
                    foreach(var alert in Tracking.CheckDeadlineAlerts(team.Id))
                    {
                        var task=Tracking.GetTask(alert.TaskId);
                        var recipients=task.Assignments.Select(a=>a.AssignedStudentId).Append(team.LeaderStudentId).ToList();
                        var supervisor=Teams.GetSupervisor(team.AssignedSupervisorId??"");if(supervisor!=null)recipients.Add(supervisor.UserId);
                        string title=alert.IsOverdue?"مهمة تجاوزت موعد التسليم":"موعد تسليم يقترب";
                        var deadline=TimeZoneInfo.ConvertTimeFromUtc(task.DueDate.ToUniversalTime(),CairoTimeZone);
                        string message=$"المهمة «{task.Title}» في فريق «{team.Name}» {(alert.IsOverdue?"متأخرة عن موعد التسليم":"يقترب موعد تسليمها")}.\nالموعد: {deadline:yyyy-MM-dd HH:mm} بتوقيت القاهرة";
                        foreach(var id in recipients.Distinct()) Notify(id,title,message,alert.IsOverdue?"Overdue":"Upcoming",team.Id,$"{task.Id}:{task.DueDate.ToUniversalTime():o}:{alert.IsOverdue}:{id}");
                    }
                }
                if(Notifications.Count!=previous)Store.Save(Capture());
                return Notifications.Count-previous;
            }
            catch{Restore(PlatformJson.Deserialize<PlatformSnapshot>(before));throw;}
        }
    }
}
