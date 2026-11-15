using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GraduationPlatform.Member2_ProjectIdeas;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraduationPlatform.Infrastructure;

public sealed class SimilarityOptions
{
    public string Engine { get; set; } = "Python";
    public string PythonExecutable { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxConcurrentWorkers { get; set; } = 2;
    public int MaxCorpusDocuments { get; set; } = 5000;
    public int MaxResults { get; set; } = 10;
    public bool AllowFallback { get; set; } = true;
}

public sealed class SimilarityService : IDisposable
{
    private const int ProtocolVersion = 1;
    private const int MaxPayloadBytes = 64 * 1024 * 1024;
    private readonly SimilarityOptions _options;
    private readonly string _contentRoot;
    private readonly string _script;
    private readonly ILogger<SimilarityService> _logger;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private volatile bool _ready;
    private volatile string _activeEngine = "starting";
    private volatile string _failureReason;
    private long _retryAfter;
    private string _executable;

    public string ActiveEngine => _activeEngine;
    public object Health => new
    {
        configuredEngine = _options.Engine, activeEngine = _activeEngine,
        status = _failureReason == null ? "healthy" : "degraded",
        protocolVersion = ProtocolVersion, reason = _failureReason
    };

    public SimilarityService(SimilarityOptions options, string contentRoot, ILogger<SimilarityService> logger = null)
    {
        _options = options;
        _contentRoot = Path.GetFullPath(contentRoot);
        _script = Path.Combine(_contentRoot, "Backend", "Python", "similarity_worker.py");
        _logger = logger ?? NullLogger<SimilarityService>.Instance;
        if ((!string.Equals(options.Engine, "Python", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(options.Engine, "CSharp", StringComparison.OrdinalIgnoreCase))
            || options.TimeoutSeconds is < 1 or > 30 || options.MaxConcurrentWorkers is < 1 or > 4
            || options.MaxCorpusDocuments is < 1 or > 10000 || options.MaxResults is < 1 or > 100)
            throw new InvalidOperationException("Invalid Similarity configuration.");
        _slots = new SemaphoreSlim(options.MaxConcurrentWorkers, options.MaxConcurrentWorkers);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_options.Engine, "CSharp", StringComparison.OrdinalIgnoreCase))
            { _activeEngine = "csharp-tfidf"; return; }
            if (_ready) return;
            _executable = ResolvePython(_options.PythonExecutable, _contentRoot);
            string output = await RunWorkerAsync(null, cancellationToken);
            using var health = JsonDocument.Parse(output);
            if (health.RootElement.GetProperty("protocolVersion").GetInt32() != ProtocolVersion
                || health.RootElement.GetProperty("engine").GetString() != "python-tfidf")
                throw new InvalidDataException("Unsupported Python worker protocol.");
            MarkReady();
            _logger.LogInformation("Python text similarity worker is ready.");
        }
        catch (Exception ex) when (ExpectedFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            MarkFailure(ex);
            if (!_options.AllowFallback) throw Unavailable();
        }
        finally { _initialization.Release(); }
    }

    public async Task<List<SimilarityResult>> AnalyzeAsync(SimilarityWorkItem work, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (work.Documents.Count > _options.MaxCorpusDocuments
            || work.Documents.Sum(d => (long)(d.Title?.Length ?? 0) + (d.Description?.Length ?? 0)) > 10000000)
            throw new PlatformException("عدد مشروعات المقارنة أو حجم نصوصها تجاوز حد الفحص؛ راجع إعدادات الخدمة.", 503, "similarity_capacity");
        if (work.Documents.Count == 0) return new();
        if (!_ready && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow.Ticks >= Interlocked.Read(ref _retryAfter))
            await InitializeAsync(cancellationToken);
        if (!_ready)
        {
            if (!_options.AllowFallback && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)) throw Unavailable();
            return await FallbackAsync(work, cancellationToken);
        }
        string payload = JsonSerializer.Serialize(new
        {
            protocolVersion = ProtocolVersion, work.Query, work.Documents, maxResults = _options.MaxResults
        }, PlatformJson.Options);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            throw new PlatformException("حجم نصوص المقارنة أكبر من الحد المسموح.", 503, "similarity_capacity");
        try
        {
            string output = await RunWorkerAsync(payload, cancellationToken);
            var response = JsonSerializer.Deserialize<WorkerResponse>(output, PlatformJson.Options);
            ValidateResponse(response, work.Documents.Count);
            MarkReady();
            return TextSimilarityAnalysis.BuildResults(work.Query, work.Documents, response.Matches, _options.MaxResults);
        }
        catch (Exception ex) when (ExpectedFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            MarkFailure(ex);
            if (!_options.AllowFallback) throw Unavailable();
            return await FallbackAsync(work, cancellationToken);
        }
    }

    public async Task<TrackClassificationResult> ClassifyTrackAsync(string title, string description, List<string> technologies, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_ready && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow.Ticks >= Interlocked.Read(ref _retryAfter))
            await InitializeAsync(cancellationToken);

        if (!_ready)
        {
            if (!_options.AllowFallback && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)) throw Unavailable();
            return TextSimilarityAnalysis.ClassifyTrack(title, description, technologies);
        }

        string payload = JsonSerializer.Serialize(new
        {
            protocolVersion = ProtocolVersion,
            action = "classify",
            query = new { title = title ?? "", description = description ?? "", technologies = technologies ?? new List<string>() }
        }, PlatformJson.Options);

        try
        {
            string output = await RunWorkerAsync(payload, cancellationToken);
            var response = JsonSerializer.Deserialize<ClassificationResponse>(output, PlatformJson.Options);
            ValidateClassification(response);
            MarkReady();
            return new TrackClassificationResult
            {
                Engine = "python-ai",
                PrimaryTrack = response.PrimaryTrack,
                RecommendedTracks = response.RecommendedTracks
            };
        }
        catch (Exception ex) when (ExpectedFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            MarkFailure(ex);
            if (!_options.AllowFallback) throw Unavailable();
            return TextSimilarityAnalysis.ClassifyTrack(title, description, technologies);
        }
    }

    public async Task<IdeaQualityEvaluationResult> EvaluateQualityAsync(string title, string description, string objectives, List<string> technologies, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_ready && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow.Ticks >= Interlocked.Read(ref _retryAfter))
            await InitializeAsync(cancellationToken);

        if (!_ready)
        {
            if (!_options.AllowFallback && string.Equals(_options.Engine, "Python", StringComparison.OrdinalIgnoreCase)) throw Unavailable();
            return TextSimilarityAnalysis.EvaluateQuality(title, description, objectives, technologies);
        }

        string payload = JsonSerializer.Serialize(new
        {
            protocolVersion = ProtocolVersion,
            action = "evaluate",
            query = new
            {
                title = title ?? "",
                description = description ?? "",
                objectives = objectives ?? "",
                technologies = technologies ?? new List<string>()
            }
        }, PlatformJson.Options);

        try
        {
            string output = await RunWorkerAsync(payload, cancellationToken);
            var response = JsonSerializer.Deserialize<EvaluationResponse>(output, PlatformJson.Options);
            ValidateEvaluation(response);
            MarkReady();
            return new IdeaQualityEvaluationResult
            {
                Engine = "python-ai",
                Score = response.Score.Value,
                Grade = response.Grade,
                Strengths = response.Strengths,
                Recommendations = response.Recommendations
            };
        }
        catch (Exception ex) when (ExpectedFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            MarkFailure(ex);
            if (!_options.AllowFallback) throw Unavailable();
            return TextSimilarityAnalysis.EvaluateQuality(title, description, objectives, technologies);
        }
    }

    public async Task<string> RunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Python & AI Engine Diagnostics ===");
        sb.AppendLine($"Configured Engine: {_options.Engine}");
        sb.AppendLine($"Active Engine: {_activeEngine}");
        sb.AppendLine($"Python Executable: {(_executable ?? "Not Resolved")}");
        sb.AppendLine($"Worker Script: {_script} (Exists: {File.Exists(_script)})");

        try
        {
            await InitializeAsync(cancellationToken);
            sb.AppendLine($"Initialization Status: {(_ready ? "SUCCESS (Python AI is active)" : "DEGRADED (Falling back to C#)")}");
            if (_failureReason != null)
                sb.AppendLine($"Failure Reason: {_failureReason}");

            if (_ready)
            {
                var testQuery = new SimilarityQuery
                {
                    Title = "نظام الحضور الذكي بالتعرف على الوجوه",
                    Description = "تطبيق ذكاء اصطناعي لتسجيل حضور الطلاب عبر الكاميرات والتعلم العميق",
                    Technologies = new List<string> { "Python", "OpenCV" }
                };
                var testDoc = new SimilarityDocument
                {
                    Id = "diag-1",
                    Title = "نظام الحضور الذكي بالتعرف على الوجوه",
                    Description = "تطبيق ذكاء اصطناعي لتسجيل حضور الطلاب عبر الكاميرات والتعلم العميق",
                    Technologies = new List<string> { "Python", "OpenCV" },
                    SourceType = "Archive"
                };
                var sw = Stopwatch.StartNew();
                var results = await AnalyzeAsync(new SimilarityWorkItem { Query = testQuery, Documents = new List<SimilarityDocument> { testDoc } }, cancellationToken);
                sw.Stop();
                sb.AppendLine($"Test Similarity Check: Passed in {sw.ElapsedMilliseconds} ms (Matches: {results.Count}, Top Score: {(results.Count > 0 ? results[0].SimilarityScore.ToString("P1") : "N/A")})");

                var trackResult = await ClassifyTrackAsync(testQuery.Title, testQuery.Description, testQuery.Technologies, cancellationToken);
                sb.AppendLine($"Test Track Classification: Passed ({trackResult.PrimaryTrack}, Confidence: {(trackResult.RecommendedTracks.Count > 0 ? trackResult.RecommendedTracks[0].Confidence.ToString("P0") : "N/A")}, Engine: {trackResult.Engine})");

                var evalResult = await EvaluateQualityAsync(testQuery.Title, testQuery.Description, "أهداف المشروع", testQuery.Technologies, cancellationToken);
                sb.AppendLine($"Test Quality Evaluation: Passed (Score: {evalResult.Score}/100, Grade: {evalResult.Grade}, Engine: {evalResult.Engine})");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Diagnostics Error: {ex.Message}");
        }

        return sb.ToString();
    }

    private async Task<List<SimilarityResult>> FallbackAsync(SimilarityWorkItem work, CancellationToken cancellationToken)
    {
        await AcquireSlotAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = TextSimilarityAnalysis.Compare(work.Query, work.Documents, _options.MaxResults);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { _slots.Release(); }
    }

    private async Task AcquireSlotAsync(CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(TimeSpan.FromSeconds(_options.TimeoutSeconds), cancellationToken))
            throw new PlatformException("خدمة فحص التشابه مشغولة؛ حاول مجددًا بعد قليل.", 503, "similarity_busy");
    }

    private async Task<string> RunWorkerAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_executable) || !File.Exists(_script))
            throw new FileNotFoundException("Python runtime or similarity worker is unavailable.");
        await AcquireSlotAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var process = new Process();
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _executable, WorkingDirectory = _contentRoot,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            if (Path.GetFileNameWithoutExtension(_executable).Equals("py", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add("-3");
            foreach (string argument in new[] { "-I", "-B", "-X", "utf8", _script }) start.ArgumentList.Add(argument);
            if (payload == null) start.ArgumentList.Add("--health");
            process.StartInfo = start;
            process.Start();
            var stdout = ReadBoundedAsync(process.StandardOutput, 128 * 1024, timeout.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 8 * 1024, timeout.Token);
            if (payload != null) await process.StandardInput.WriteAsync(payload.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("Python worker returned an error.");
            return await stdout;
        }
        finally
        {
            try
            {
                if (process.Id != 0 && !process.HasExited)
                { process.Kill(entireProcessTree: true); process.WaitForExit(1000); }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            _slots.Release();
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[2048];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length + read > limit) throw new InvalidDataException("Python worker output exceeded its limit.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private void ValidateResponse(WorkerResponse response, int documentCount)
    {
        if (response?.ProtocolVersion != ProtocolVersion || response.Engine != "python-tfidf"
            || response.Matches == null || response.Matches.Count > _options.MaxResults)
            throw new InvalidDataException("Invalid Python worker response.");
        var indices = new HashSet<int>();
        foreach (var match in response.Matches)
            if (match == null || match.Index < 0 || match.Index >= documentCount || !indices.Add(match.Index)
                || !double.IsFinite(match.Score) || match.Score < .25 || match.Score > 1
                || match.Keywords == null || match.Keywords.Count > 24
                || match.Keywords.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 64))
                throw new InvalidDataException("Invalid Python similarity score.");
    }

    private void MarkReady()
    {
        _ready = true; _activeEngine = "python-tfidf"; _failureReason = null;
        Interlocked.Exchange(ref _retryAfter, 0);
    }

    private void MarkFailure(Exception exception)
    {
        _ready = false; _activeEngine = "csharp-tfidf";
        _failureReason = exception is OperationCanceledException ? "timeout"
            : exception is Win32Exception or FileNotFoundException ? "python_unavailable" : "worker_failed";
        Interlocked.Exchange(ref _retryAfter, DateTime.UtcNow.AddSeconds(30).Ticks);
        _logger.LogWarning("Python similarity worker unavailable ({Reason}); C# fallback configured: {Fallback}.",
            _failureReason, _options.AllowFallback);
    }

    private static bool ExpectedFailure(Exception exception) =>
        exception is IOException or InvalidDataException or Win32Exception or JsonException or OperationCanceledException
            or DecoderFallbackException or FormatException or KeyNotFoundException or InvalidOperationException;
    private static PlatformException Unavailable() =>
        new("تعذّر تشغيل خدمة فحص التشابه؛ راجع إعدادات بايثون أو حاول مجددًا.", 503, "similarity_unavailable");

    public static string ResolvePython(string configured, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured)) return configured;
            if (configured.Contains(Path.DirectorySeparatorChar) || configured.Contains(Path.AltDirectorySeparatorChar))
                return Path.GetFullPath(Path.Combine(contentRoot, configured));
            return FindOnPath(configured) ?? configured;
        }
        foreach (string local in new[] { Path.Combine(contentRoot, ".tools", "python", "python.exe"), Path.Combine(contentRoot, ".tools", "python", "bin", "python3") })
            if (File.Exists(local)) return local;
        foreach (string name in new[] { "python3", "python", "py" })
        {
            string found = FindOnPath(name);
            if (found != null) return found;
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile)) profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(profile))
        {
            string bundled = Path.Combine(profile, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "python", "python.exe");
            if (File.Exists(bundled)) return bundled;
        }
        string node = FindOnPath("node");
        if (node != null)
        {
            string dependencies = new FileInfo(node).Directory?.Parent?.Parent?.FullName;
            if (dependencies != null)
            {
                string bundled = Path.Combine(dependencies, "python", "python.exe");
                if (File.Exists(bundled)) return bundled;
            }
        }
        return null;
    }

    private static string FindOnPath(string name)
    {
        string filename = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory) || directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                string path = Path.Combine(directory.Trim('"'), filename);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private sealed class WorkerResponse
    {
        public int ProtocolVersion { get; set; }
        public string Engine { get; set; }
        public List<SimilarityScore> Matches { get; set; }
    }

    private sealed class ClassificationResponse
    {
        public int ProtocolVersion { get; set; }
        public string Engine { get; set; }
        public string Action { get; set; }
        public string PrimaryTrack { get; set; }
        public List<TrackRecommendation> RecommendedTracks { get; set; }
    }

    private sealed class EvaluationResponse
    {
        public int ProtocolVersion { get; set; }
        public string Engine { get; set; }
        public string Action { get; set; }
        public int? Score { get; set; }
        public string Grade { get; set; }
        public List<string> Strengths { get; set; }
        public List<string> Recommendations { get; set; }
    }

    private static bool ValidText(string value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Contains('\0');

    private static bool ValidTextList(List<string> values, int count, int length) =>
        values != null && values.Count <= count && values.All(value => ValidText(value, length));

    private static void ValidateClassification(ClassificationResponse response)
    {
        if (response?.ProtocolVersion != ProtocolVersion || response.Engine != "python-ai" || response.Action != "classify"
            || !ValidText(response.PrimaryTrack, 100) || response.RecommendedTracks == null
            || response.RecommendedTracks.Count is < 1 or > 3
            || response.RecommendedTracks.Any(track => track == null || !ValidText(track.Track, 100)
                || !ValidText(track.ArabicName, 100) || !double.IsFinite(track.Confidence)
                || track.Confidence < 0 || track.Confidence > 1 || !ValidTextList(track.MatchedKeywords, 10, 64))
            || response.RecommendedTracks.Select(track => track.Track).Distinct(StringComparer.Ordinal).Count() != response.RecommendedTracks.Count
            || response.PrimaryTrack != response.RecommendedTracks[0].ArabicName)
            throw new InvalidDataException("Invalid Python track classification.");
    }

    private static void ValidateEvaluation(EvaluationResponse response)
    {
        if (response?.ProtocolVersion != ProtocolVersion || response.Engine != "python-ai" || response.Action != "evaluate"
            || response.Score == null || response.Score < 0 || response.Score > 100 || !ValidText(response.Grade, 100)
            || !ValidTextList(response.Strengths, 10, 1000) || !ValidTextList(response.Recommendations, 10, 1000))
            throw new InvalidDataException("Invalid Python idea evaluation.");
    }

    public void Dispose() { _slots.Dispose(); _initialization.Dispose(); }
}
