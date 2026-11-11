using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;

namespace GraduationPlatform.Backend.ApiControllers
{
    public class ApiServer
    {
        private readonly HttpListener _listener;
        private readonly string _rootDir;
        private readonly int _port;
        private bool _isRunning;

        // Core business services from Backend modules
        public AuthService AuthService { get; private set; }
        public ProjectIdeaService IdeaService { get; private set; }
        public TeamService TeamService { get; private set; }
        public TrackingService TrackingService { get; private set; }

        public ApiServer(string rootDir, int port = 5000)
        {
            _rootDir = Path.GetFullPath(rootDir);
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
            _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", _port));

            AuthService = new AuthService();
            IdeaService = new ProjectIdeaService();
            TeamService = new TeamService();
            TrackingService = new TrackingService();

            SeedInitialData();
        }

        private void SeedInitialData()
        {
            var doc = AuthService.Register("dr_mostafa", "mostafa@alexu.edu.eg", "DocPass123", UserRoleType.Supervisor, "Dr. Mostafa Kamel", "Computer Science", "FAC-014").Data;
            TeamService.RegisterSupervisor(doc.Id, doc.Profile.FullName, "Assoc. Prof.", "Computer Science", "IoT & Smart Systems", 3);

            var student = AuthService.Register("ahmed_mansour", "ahmed@alexu.edu.eg", "AhmedPass1", UserRoleType.Student, "Ahmed Mansour", "Computer Science", "ST-2026-101").Data;
            var team = TeamService.CreateTeam(student.Id, student.Profile.FullName, "EnergyGrid Pioneers", 4).Data;
            TrackingService.InitializeDefaultMilestones(team.Id);
        }

        public void Start()
        {
            _listener.Start();
            _isRunning = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("================================================================================");
            Console.WriteLine(string.Format(">>> GRADUATION BACKEND & WEB SERVER RUNNING ON: http://localhost:{0}/ <<<", _port));
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine("Available Clean Routes:");
            Console.WriteLine(string.Format(" - Landing Portal:        http://localhost:{0}/", _port));
            Console.WriteLine(string.Format(" - Student Web App:       http://localhost:{0}/student", _port));
            Console.WriteLine(string.Format(" - Supervisor Web App:    http://localhost:{0}/supervisor", _port));
            Console.WriteLine(string.Format(" - Admin Dashboard:       http://localhost:{0}/admin", _port));
            Console.WriteLine("--------------------------------------------------------------------------------");

            ThreadPool.QueueUserWorkItem(ListenLoop);
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
                string path = req.RawUrl.Split('?')[0];

                // Route pages to Frontend/Pages
                if (path == "/" || path == "/index" || path == "/index.html")
                {
                    ServeFile(res, Path.Combine(_rootDir, "Frontend", "Pages", "index.html"), "text/html; charset=utf-8");
                    return;
                }
                if (path == "/student" || path == "/student.html")
                {
                    ServeFile(res, Path.Combine(_rootDir, "Frontend", "Pages", "student.html"), "text/html; charset=utf-8");
                    return;
                }
                if (path == "/supervisor" || path == "/supervisor.html")
                {
                    ServeFile(res, Path.Combine(_rootDir, "Frontend", "Pages", "supervisor.html"), "text/html; charset=utf-8");
                    return;
                }
                if (path == "/admin" || path == "/admin.html")
                {
                    ServeFile(res, Path.Combine(_rootDir, "Frontend", "Pages", "admin.html"), "text/html; charset=utf-8");
                    return;
                }

                // REST API health check
                if (path == "/api/health")
                {
                    ServeJson(res, "{\"status\":\"online\",\"architecture\":\"Separated Frontend/Backend/UI_UX\",\"time\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
                    return;
                }

                // Static asset file serving (UI_UX styles, Frontend scripts)
                string relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string filePath = Path.Combine(_rootDir, relative);

                // If not found directly, check inside Frontend folder
                if (!File.Exists(filePath))
                {
                    string frontendPath = Path.Combine(_rootDir, "Frontend", relative);
                    if (File.Exists(frontendPath)) filePath = frontendPath;
                }

                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    string mime = "text/plain";
                    if (ext == ".html") mime = "text/html; charset=utf-8";
                    else if (ext == ".css") mime = "text/css; charset=utf-8";
                    else if (ext == ".js") mime = "application/javascript; charset=utf-8";
                    else if (ext == ".json") mime = "application/json; charset=utf-8";

                    ServeFile(res, filePath, mime);
                    return;
                }

                // Try finding file in Frontend/Pages/
                string pagePath = Path.Combine(_rootDir, "Frontend", "Pages", relative);
                if (File.Exists(pagePath))
                {
                    ServeFile(res, pagePath, "text/html; charset=utf-8");
                    return;
                }

                res.StatusCode = 404;
                byte[] notFound = Encoding.UTF8.GetBytes("<h1>404 - Not Found</h1>");
                res.ContentType = "text/html; charset=utf-8";
                res.OutputStream.Write(notFound, 0, notFound.Length);
                res.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    byte[] err = Encoding.UTF8.GetBytes("500 Error: " + ex.Message);
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
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            if (!Directory.Exists(Path.Combine(root, "Frontend")))
            {
                root = Environment.CurrentDirectory;
            }

            var server = new ApiServer(root, 5000);
            server.Start();

            Console.WriteLine("Press [ENTER] to stop server...");
            Console.ReadLine();
            server.Stop();
        }
    }
}
