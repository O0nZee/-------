using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using Microsoft.AspNetCore.Http;

namespace GraduationPlatform.Tests;

public static class BackendRegressionTests
{
    private static string Root => Directory.GetCurrentDirectory();
    private static string Python => SimilarityService.ResolvePython("", Root)
        ?? throw new Exception("Python 3.10 or newer is required for the hybrid integration tests.");

    public static int RunTests()
    {
        int passed = 0;
        void Test(string name, Action action)
        {
            action(); passed++;
            Console.WriteLine($" [PASS] Backend regression {passed}: {name}");
        }
        Test("Arabic normalization and distinct programming languages", () =>
        {
            Assert(SimilarityEngine.TokenizeAndNormalize("نِظَام مُتَابَعَة الطُّلَّاب")
                .SequenceEqual(SimilarityEngine.TokenizeAndNormalize("متابعه الطلاب")), "Arabic normalization");
            Assert(TextSimilarityAnalysis.NormalizeTechnologies(new[] { "C#", ".NET", "React.js" })
                .SequenceEqual(TextSimilarityAnalysis.NormalizeTechnologies(new[] { "C Sharp", "DotNet", "ReactJS" })), "Technology aliases");
            Assert(!TextSimilarityAnalysis.NormalizeTechnologies(new[] { "C++" })
                .SequenceEqual(TextSimilarityAnalysis.NormalizeTechnologies(new[] { "C#" })), "C++ must remain distinct from C#");
        });
        Test("Missing optional fields do not lower an identical match", () =>
        {
            double score = SimilarityEngine.CalculateWeightedSimilarity("Campus energy telemetry", "Wireless electricity meters", null,
                "Campus energy telemetry", "Wireless electricity meters", null, out _);
            Assert(score == 1, "Identical project without technologies should score 100%");
            score = SimilarityEngine.CalculateWeightedSimilarity("Campus energy telemetry", "", null,
                "Campus energy telemetry", "Wireless electricity meters", null, out _);
            Assert(score == 1, "Title-only identity should use available fields");
        });
        Test("A shared framework alone does not establish a similar project", () =>
        {
            var query = new SimilarityQuery { Title = "Wireless energy meters", Description = "Calibrating electricity sensors", Technologies = new() { "Python" } };
            var document = new SimilarityDocument { Title = "Clinical diagnosis", Description = "Classifying patient medical images", Technologies = new() { "Python" } };
            Assert(TextSimilarityAnalysis.Compare(query, new[] { document }).Count == 0, "Unrelated topics");
        });
        Test("Private current ideas keep their titles and identifiers hidden", () =>
        {
            var work = Work(); work.Documents[0].RevealDetails = false;
            var match = TextSimilarityAnalysis.Compare(work.Query, work.Documents)[0];
            Assert(match.MatchedItemId == null && match.MatchedTitle == "فكرة لفريق آخر" && match.OverlappingKeywords.Count == 0, "Private match projection");
        });
        Test("Registration and password reset both enforce eight characters", () =>
        {
            var auth = new AuthService();
            Assert(!auth.Register("weak", "weak@example.test", "weak12", UserRoleType.Student, "طالب", "علوم", "1").IsSuccess, "Weak registration");
            var user = PlatformRuntime.Require(auth.Register("valid", "valid@example.test", "StrongPassword!123", UserRoleType.Student, "طالب", "علوم", "1"));
            var token = PlatformRuntime.Require(auth.RequestPasswordReset(user.Email));
            Assert(!auth.ResetPassword(token, "weak12").IsSuccess, "Weak reset");
            Assert(auth.ResetPassword(token, "AnotherStrong!123").IsSuccess, "Rejected reset must not consume its token");
        });
        Test("Whitespace cannot bypass description validation and technology lists are copied", () =>
        {
            var ideas = new ProjectIdeaService();
            Assert(!ideas.SubmitIdea("student", "Energy sensors", "tiny" + new string(' ', 30), "", null, "IoT").IsSuccess, "Padded description");
            var technologies = new List<string> { "C#" };
            var idea = PlatformRuntime.Require(ideas.SubmitIdea("student", "Energy sensors", "Wireless sensors measure campus electricity.", "", technologies, "IoT"));
            technologies.Add("Python");
            Assert(idea.Technologies.Count == 1, "Caller-owned list must not mutate stored state");
            Assert(!ideas.UpdateIdea(idea.Id, new string('x', 201), idea.Description, "", null, "IoT").IsSuccess, "Update title limit");
        });
        Test("Arabic academic years normalize and invalid ranges are rejected", () =>
        {
            Assert(Input.AcademicYear("٢٠٢٦–٢٠٢٧") == "2026-2027", "Arabic digits and separator");
            Throws<PlatformException>(() => Input.AcademicYear("٢٠٢٦-٢٠٢٥"));
        });
        Test("API dates require an explicit timezone", () =>
        {
            Throws<PlatformException>(() => Input.UtcDate(new DateTime(2026, 11, 13, 10, 0, 0), "الموعد"));
            var utc = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
            Assert(Input.UtcDate(utc, "الموعد") == utc, "UTC must be preserved");
        });
        Test("Python executes real comparison with C# fallback parity", () => PythonParity().GetAwaiter().GetResult());
        Test("Python track and quality analysis agree with the C# fallback", () => PythonTextParity().GetAwaiter().GetResult());
        Test("Unavailable Python falls back and strict mode reports failure", () => MissingPython().GetAwaiter().GetResult());
        Test("Malformed Python scores and invalid UTF-8 cannot enter an API response", () =>
        {
            foreach (string mode in new[] { "invalid", "invalid-score", "invalid-utf8" })
                FaultyWorker(mode).GetAwaiter().GetResult();
        });
        Test("Malformed track and quality results respect fallback and strict mode", () => FaultyAnalysis().GetAwaiter().GetResult());
        Test("A crashed Python worker falls back", () => FaultyWorker("crash").GetAwaiter().GetResult());
        Test("A stalled Python worker times out and is stopped", () => FaultyWorker("timeout").GetAwaiter().GetResult());
        Test("Request cancellation stops analysis without changing engine health", () => CancelWorker().GetAwaiter().GetResult());
        Test("Invalid similarity configuration fails early", () =>
        {
            Throws<InvalidOperationException>(() => new SimilarityService(new SimilarityOptions { TimeoutSeconds = 0 }, Root));
            Throws<InvalidOperationException>(() => new SimilarityService(new SimilarityOptions { Engine = "Unknown" }, Root));
        });
        Test("Office containers are validated and rejected uploads leave no records", () => OfficeFiles().GetAwaiter().GetResult());
        return passed;
    }

    private static SimilarityWorkItem Work() => new()
    {
        Query = new SimilarityQuery { Title = "متابعة استهلاك الطاقة", Description = "حساسات لاسلكية لقياس الطاقة وتحليل الاستهلاك داخل الحرم الجامعي", Technologies = new() { "C#", ".NET" } },
        Documents = new()
        {
            new() { Id = "duplicate", Title = "مُتَابَعَة اِسْتِهْلَاك الطَّاقَة", Description = "حساسات لاسلكية لقياس الطاقة وتحليل الاستهلاك داخل الحرم الجامعي", Technologies = new() { "C Sharp", "DotNet" }, SourceType = "CurrentIdea" },
            new() { Id = "medicine", Title = "رعاية المرضى", Description = "تصنيف صور الأشعة وتشخيص أمراض القلب", Technologies = new() { "Python" }, SourceType = "Archive" },
            new() { Id = "related", Title = "Campus energy telemetry", Description = "حساسات لقياس الطاقة داخل الحرم الجامعي", Technologies = new() { "Python" }, SourceType = "Archive" }
        }
    };

    private static async Task PythonParity()
    {
        using var analysis = new SimilarityService(new SimilarityOptions { PythonExecutable = Python, AllowFallback = false }, Root);
        await analysis.InitializeAsync();
        Assert(analysis.ActiveEngine == "python-tfidf", "Python must actually run");
        var work = Work();
        var expected = TextSimilarityAnalysis.Compare(work.Query, work.Documents, 10);
        var actual = await analysis.AnalyzeAsync(work);
        Assert(actual.Count == expected.Count && actual.Count > 0, "Same result count");
        for (int index = 0; index < actual.Count; index++)
        {
            Assert(actual[index].MatchedItemId == expected[index].MatchedItemId, "Same ranking");
            Assert(Math.Abs(actual[index].SimilarityScore - expected[index].SimilarityScore) < .0001, "Same scores");
            Assert(actual[index].OverlappingKeywords.SequenceEqual(expected[index].OverlappingKeywords), "Same keywords");
        }
    }

    private static async Task MissingPython()
    {
        var options = new SimilarityOptions { PythonExecutable = Path.Combine(Root, "tests", ".runtime", "missing-python.exe") };
        using var fallback = new SimilarityService(options, Root);
        await fallback.InitializeAsync();
        Assert((await fallback.AnalyzeAsync(Work())).Count > 0 && fallback.ActiveEngine == "csharp-tfidf", "Working fallback");
        using var strict = new SimilarityService(new SimilarityOptions { PythonExecutable = options.PythonExecutable, AllowFallback = false }, Root);
        await ExpectUnavailable(() => strict.InitializeAsync());
        await ExpectUnavailable(() => strict.ClassifyTrackAsync("Energy sensors", "Wireless meters", null));
        await ExpectUnavailable(() => strict.EvaluateQualityAsync("Energy sensors", "Wireless meters", "", null));
    }

    private static async Task PythonTextParity()
    {
        using var service = new SimilarityService(new SimilarityOptions { PythonExecutable = Python, AllowFallback = false }, Root);
        await service.InitializeAsync();
        const string title = "رؤية حاسوبية للتعرف على الوجوه", description = "شبكات عصبية وتعلم عميق لتصنيف الصور";
        var technologies = new List<string> { "Python", "PyTorch", "OpenCV" };
        var expected = TextSimilarityAnalysis.ClassifyTrack(title, description, technologies);
        var actual = await service.ClassifyTrackAsync(title, description, technologies);
        Assert(actual.Engine == "python-ai" && actual.PrimaryTrack == expected.PrimaryTrack, "Python track analysis");
        Assert(actual.RecommendedTracks.Count == expected.RecommendedTracks.Count, "Same track count");
        for (int index = 0; index < actual.RecommendedTracks.Count; index++)
        {
            var a = actual.RecommendedTracks[index]; var e = expected.RecommendedTracks[index];
            Assert(a.Track == e.Track && Math.Abs(a.Confidence - e.Confidence) < .0001, "Same tracks and confidence");
            Assert(a.MatchedKeywords.SequenceEqual(e.MatchedKeywords), "Same track keywords");
        }
        string detailed = string.Join('\n', Enumerable.Repeat("قياس", 50));
        string objectives = string.Join('\t', Enumerable.Repeat("تحسين", 20));
        technologies = new() { "C#", ".NET", "IoT" };
        var expectedQuality = TextSimilarityAnalysis.EvaluateQuality("متابعة استهلاك الطاقة", detailed, objectives, technologies);
        var quality = await service.EvaluateQualityAsync("متابعة استهلاك الطاقة", detailed, objectives, technologies);
        Assert(quality.Engine == "python-ai" && quality.Score == 100 && quality.Score == expectedQuality.Score, "Whitespace-aware quality parity");
        Assert(quality.Grade == expectedQuality.Grade && quality.Strengths.SequenceEqual(expectedQuality.Strengths)
            && quality.Recommendations.SequenceEqual(expectedQuality.Recommendations), "Same quality feedback");
    }

    private static async Task ExpectUnavailable(Func<Task> action)
    {
        try { await action(); throw new Exception("Strict mode must reject unavailable Python"); }
        catch (PlatformException error) { Assert(error.Status == 503 && error.Code == "similarity_unavailable", "Controlled unavailable response"); }
    }

    private static string WorkerRoot(string mode)
    {
        string root = Path.Combine(Root, "tests", ".runtime", "worker-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "Backend", "Python", "similarity_worker.py");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string body = mode switch
        {
            "invalid" => "print(json.dumps({'protocolVersion':1,'engine':'python-tfidf','matches':[{'index':9999,'score':1,'keywords':[]}]}))",
            "invalid-score" => "print(json.dumps({'protocolVersion':1,'engine':'python-tfidf','matches':[{'index':0,'score':2,'keywords':[]}]}))",
            "invalid-utf8" => "sys.stdout.buffer.write(b'\\xff')",
            "invalid-classification" => "print(json.dumps({'protocolVersion':1,'engine':'python-ai','action':'classify','primaryTrack':'Invalid','recommendedTracks':[{'track':'Invalid','arabicName':'Invalid','confidence':5,'matchedKeywords':[]}]}))",
            "invalid-evaluation" => "print(json.dumps({'protocolVersion':1,'engine':'python-ai','action':'evaluate','score':999,'grade':'Invalid','strengths':[],'recommendations':[]}))",
            "crash" => "raise RuntimeError('worker_fixture_error')",
            _ => "time.sleep(30)"
        };
        File.WriteAllText(path, "import json,sys,time\nif '--health' in sys.argv:\n print(json.dumps({'protocolVersion':1,'engine':'python-tfidf'}))\nelse:\n sys.stdin.buffer.read()\n " + body + "\n", new UTF8Encoding(false));
        return root;
    }

    private static async Task FaultyWorker(string mode)
    {
        using var service = new SimilarityService(new SimilarityOptions { PythonExecutable = Python, TimeoutSeconds = mode == "timeout" ? 1 : 5 }, WorkerRoot(mode));
        await service.InitializeAsync();
        Assert(service.ActiveEngine == "python-tfidf", "Fixture health check");
        var watch = Stopwatch.StartNew();
        var result = await service.AnalyzeAsync(Work());
        Assert(result.Count > 0 && service.ActiveEngine == "csharp-tfidf", "Safe fallback");
        if (mode == "timeout") Assert(watch.Elapsed < TimeSpan.FromSeconds(5), "Worker timeout must be enforced");
    }

    private static async Task FaultyAnalysis()
    {
        foreach (string mode in new[] { "invalid-classification", "invalid-evaluation" })
        {
            string root = WorkerRoot(mode);
            using var service = new SimilarityService(new SimilarityOptions { PythonExecutable = Python }, root);
            await service.InitializeAsync();
            string engine = mode == "invalid-classification"
                ? (await service.ClassifyTrackAsync("Energy sensors", "Wireless meters", null)).Engine
                : (await service.EvaluateQualityAsync("Energy sensors", "Wireless meters", "", null)).Engine;
            Assert(engine == "csharp-ai" && service.ActiveEngine == "csharp-tfidf", "Malformed analysis fallback");
            using var strict = new SimilarityService(new SimilarityOptions { PythonExecutable = Python, AllowFallback = false }, root);
            await strict.InitializeAsync();
            if (mode == "invalid-classification") await ExpectUnavailable(() => strict.ClassifyTrackAsync("Energy sensors", "Wireless meters", null));
            else await ExpectUnavailable(() => strict.EvaluateQualityAsync("Energy sensors", "Wireless meters", "", null));
        }
    }

    private static async Task CancelWorker()
    {
        using var service = new SimilarityService(new SimilarityOptions { PythonExecutable = Python, TimeoutSeconds = 5 }, WorkerRoot("timeout"));
        await service.InitializeAsync();
        using var cancellation = new CancellationTokenSource(200);
        var watch = Stopwatch.StartNew();
        try { await service.AnalyzeAsync(Work(), cancellation.Token); throw new Exception("Cancellation must propagate"); }
        catch (OperationCanceledException) { Assert(cancellation.IsCancellationRequested, "Caller cancellation"); }
        Assert(watch.Elapsed < TimeSpan.FromSeconds(3), "Cancellation must stop the worker promptly");
        Assert(service.ActiveEngine == "python-tfidf", "User cancellation must not degrade engine health");
    }

    private static byte[] OfficeContainer(string part, string contentType)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var xml = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open(), new UTF8Encoding(false)))
                xml.Write($"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{part}\" ContentType=\"{contentType}\"/></Types>");
            using (var xml = new StreamWriter(archive.CreateEntry(part).Open(), new UTF8Encoding(false)))
                xml.Write("<document xmlns=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><body/></document>");
        }
        return buffer.ToArray();
    }

    private static async Task OfficeFiles()
    {
        string data = Path.Combine(Root, "tests", ".runtime", "office-" + Guid.NewGuid().ToString("N"));
        using var runtime = new PlatformRuntime(new PlatformOptions { DataDirectory = data, DemoData = false }, Root, "RegressionAdmin!123");
        var admin = runtime.Auth.GetUsersByRole(UserRoleType.Admin).Single();
        var student = PlatformRuntime.Require(runtime.Auth.Register("office_student", "office@example.test", "StrongPassword!123", UserRoleType.Student, "طالب ملفات", "علوم", "1"));
        var team = PlatformRuntime.Require(runtime.Teams.CreateTeam(student.Id, student.Profile.FullName, "فريق الملفات", 5));
        var idea = PlatformRuntime.Require(runtime.Ideas.SubmitIdea(student.Id, "تحليل استهلاك الطاقة", "حساسات لقياس استهلاك الطاقة في الحرم الجامعي", "", null, "IoT", team.Id));
        PlatformRuntime.Require(runtime.Ideas.ReviewIdea(admin.Id, idea.Id, IdeaStatus.Approved, ""));
        PlatformRuntime.Require(runtime.Teams.LinkProjectIdea(team.Id, idea.Id));
        var milestone = PlatformRuntime.Require(runtime.Tracking.InitializeDefaultMilestones(team.Id))[0];
        var files = new FileSubmissionService(runtime);
        async Task Upload(byte[] bytes, string name)
        {
            using var stream = new MemoryStream(bytes);
            var form = new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary() };
            await files.SaveAsync(student.Id, team.Id, milestone.Id, "تقرير اختبار", form, CancellationToken.None);
        }
        foreach (var invalid in new[]
        {
            (Encoding.UTF8.GetBytes("PK-not-a-real-archive"), "fake.zip"),
            (Encoding.UTF8.GetBytes("PK-not-a-real-document"), "fake.docx"),
            (OfficeContainer("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"), "wrong-kind.docx")
        })
        {
            try { await Upload(invalid.Item1, invalid.Item2); throw new Exception("Invalid archive must be rejected"); }
            catch (PlatformException) { }
            Assert(runtime.Submissions.Count == 0 && !Directory.EnumerateFiles(runtime.UploadDirectory).Any(), "Rejected upload cleanup");
        }
        await Upload(OfficeContainer("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"), "report.docx");
        Assert(runtime.Submissions.Count == 1, "Office document container accepted");
        runtime.Mutate(admin.Id, "submission.review.test", null, r => r.ReviewSubmission(admin, r.Submissions[0].Id, new ReviewRequest { Decision = "approved", Grade = 95, Feedback = "" }));
        Assert(runtime.Submissions[0].Decision == "Approved", "Canonical case-insensitive review decision");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Backend regression failed: " + message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
