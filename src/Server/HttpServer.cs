using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;

namespace GraduationPlatform.Server
{
    public class PlatformWebServer
    {
        private readonly HttpListener _listener;
        private readonly string _webRoot;
        private readonly int _port;
        private bool _isRunning;

        // Core business services from Members 1, 2, 3, 4
        private readonly AuthService _authService;
        private readonly ProjectIdeaService _ideaService;
        private readonly TeamService _teamService;
        private readonly TrackingService _trackingService;

        public PlatformWebServer(string webRoot, int port = 5000)
        {
            _webRoot = Path.GetFullPath(webRoot);
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
            _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", _port));

            // Initialize services
            _authService = new AuthService();
            _ideaService = new ProjectIdeaService();
            _teamService = new TeamService();
            _trackingService = new TrackingService();

            SeedInitialData();
        }

        private void SeedInitialData()
        {
            // Seed Dr. Mostafa
            var docUser = _authService.Register("dr_mostafa", "mostafa@alexu.edu.eg", "ProfDoc123", UserRoleType.Supervisor, 
                "Dr. Mostafa Kamel", "Computer Science", "FAC-014").Data;
            _teamService.RegisterSupervisor(docUser.Id, docUser.Profile.FullName, "Assoc. Prof.", "Computer Science", "IoT & Smart Systems", 3);

            // Seed Student Ahmed
            var student = _authService.Register("ahmed_mansour", "ahmed@alexu.edu.eg", "Pass12345", UserRoleType.Student, 
                "Ahmed Mansour", "Computer Science", "ST-2026-101").Data;

            // Seed Team
            var team = _teamService.CreateTeam(student.Id, student.Profile.FullName, "EnergyGrid Pioneers", 4).Data;

            // Seed Default Milestones & Tasks
            _trackingService.InitializeDefaultMilestones(team.Id);
            _trackingService.CreateTask(team.Id, null, "Complete SRS Spec", "Phase 1 requirements", TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _isRunning = true;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("================================================================================");
                Console.WriteLine(string.Format(">>> GRADUATION WEB SERVER RUNNING ON: http://localhost:{0}/ <<<", _port));
                Console.WriteLine("================================================================================");
                Console.ResetColor();
                Console.WriteLine("Available Web Routes:");
                Console.WriteLine(string.Format(" - Public Portal & Role Selector:  http://localhost:{0}/", _port));
                Console.WriteLine(string.Format(" - Student Web Portal:             http://localhost:{0}/student", _port));
                Console.WriteLine(string.Format(" - Supervisor Web Portal:          http://localhost:{0}/supervisor", _port));
                Console.WriteLine(string.Format(" - Admin Dashboard (الداش بورد):   http://localhost:{0}/admin", _port));
                Console.WriteLine("--------------------------------------------------------------------------------");

                ThreadPool.QueueUserWorkItem(ListenLoop);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("Failed to start web server on port {0}: {1}", _port, ex.Message));
                Console.ResetColor();
            }
        }

        public void Stop()
        {
            _isRunning = false;
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }

        private void ListenLoop(object state)
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(ctx => ProcessRequest((HttpListenerContext)ctx), context);
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            try
            {
                string rawUrl = req.RawUrl.Split('?')[0];

                // Route aliases
                if (rawUrl == "/" || rawUrl == "/index" || rawUrl == "/index.html")
                {
                    ServeFile(res, Path.Combine(_webRoot, "index.html"), "text/html; charset=utf-8");
                    return;
                }
                if (rawUrl == "/student" || rawUrl == "/student.html")
                {
                    ServeFile(res, Path.Combine(_webRoot, "student.html"), "text/html; charset=utf-8");
                    return;
                }
                if (rawUrl == "/supervisor" || rawUrl == "/supervisor.html")
                {
                    ServeFile(res, Path.Combine(_webRoot, "supervisor.html"), "text/html; charset=utf-8");
                    return;
                }
                if (rawUrl == "/admin" || rawUrl == "/admin.html")
                {
                    ServeFile(res, Path.Combine(_webRoot, "admin.html"), "text/html; charset=utf-8");
                    return;
                }

                // REST API: System Health Check
                if (rawUrl == "/api/health")
                {
                    string json = "{\"status\":\"healthy\",\"platform\":\"Graduation Project Platform\",\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
                    ServeJson(res, json);
                    return;
                }

                // Static files (.css, .js, .png, etc.)
                string relativePath = rawUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string filePath = Path.Combine(_webRoot, relativePath);

                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    string mime = "text/plain";
                    if (ext == ".html") mime = "text/html; charset=utf-8";
                    else if (ext == ".css") mime = "text/css; charset=utf-8";
                    else if (ext == ".js") mime = "application/javascript; charset=utf-8";
                    else if (ext == ".json") mime = "application/json; charset=utf-8";
                    else if (ext == ".png") mime = "image/png";

                    ServeFile(res, filePath, mime);
                    return;
                }

                // 404 Not Found
                res.StatusCode = 404;
                byte[] errorBytes = Encoding.UTF8.GetBytes("<h1>404 - Page Not Found</h1>");
                res.ContentType = "text/html; charset=utf-8";
                res.OutputStream.Write(errorBytes, 0, errorBytes.Length);
                res.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    byte[] err = Encoding.UTF8.GetBytes(string.Format("<h1>500 Internal Error</h1><p>{0}</p>", ex.Message));
                    res.OutputStream.Write(err, 0, err.Length);
                    res.Close();
                }
                catch { }
            }
        }

        private static void ServeFile(HttpListenerResponse res, string filePath, string contentType)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            res.ContentType = contentType;
            res.ContentLength64 = bytes.Length;
            res.StatusCode = 200;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        private static void ServeJson(HttpListenerResponse res, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.StatusCode = 200;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        public static void Main(string[] args)
        {
            string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src", "WebUI");
            if (!Directory.Exists(webRoot))
            {
                webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "WebUI");
            }
            if (!Directory.Exists(webRoot))
            {
                webRoot = Path.Combine(Environment.CurrentDirectory, "src", "WebUI");
            }

            int port = 5000;
            var server = new PlatformWebServer(webRoot, port);
            server.Start();

            Console.WriteLine("Press [ENTER] to stop server...");
            Console.ReadLine();
            server.Stop();
        }
    }
}
