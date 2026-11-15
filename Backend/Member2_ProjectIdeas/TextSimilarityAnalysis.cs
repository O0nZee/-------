using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GraduationPlatform.Member2_ProjectIdeas;

public sealed class SimilarityQuery
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Technologies { get; set; } = new();
    public string ExcludeIdeaId { get; set; }
}

public sealed class SimilarityDocument
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public List<string> Technologies { get; set; } = new();
    public string SourceType { get; set; }
    public string TeamId { get; set; }
    public bool RevealDetails { get; set; } = true;
}

public sealed class SimilarityWorkItem
{
    public SimilarityQuery Query { get; set; }
    public List<SimilarityDocument> Documents { get; set; } = new();
}

public sealed class SimilarityScore
{
    public int Index { get; set; }
    public double Score { get; set; }
    public List<string> Keywords { get; set; } = new();
}

// The C# implementation also provides a deterministic fallback for the Python worker.
public static class TextSimilarityAnalysis
{
    private static readonly Regex Marks = new(@"[\u0610-\u061a\u0640\u064b-\u065f\u0670\u06d6-\u06ed]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly (Regex Pattern, string Replacement)[] LanguageNames =
    {
        (new Regex(@"(?<!\w)c\+\+(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "cplusplus"),
        (new Regex(@"(?<!\w)c#(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "csharp"),
        (new Regex(@"(?<!\w)c\s+sharp(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase), "csharp"),
        (new Regex(@"(?<!\w)\.net\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "dotnet")
    };
    private static readonly HashSet<string> StopWords = (
        "a an the and or in on at to for with by from is are was were be been using used " +
        "system project app application platform based graduation study management " +
        "في من على إلى عن مع هذا هذه ذلك تلك هو هي هم أو و التي الذي يتم " +
        "مشروع نظام تطبيق منصة تخرج إدارة دراسة باستخدام خلال بين كل يمكن")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(NormalizeText).ToHashSet(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> TechnologyAliases = new(StringComparer.Ordinal)
    {
        ["cpp"] = "cplusplus", ["csharp"] = "csharp", ["net"] = "dotnet",
        ["netcore"] = "dotnet", ["dotnetcore"] = "dotnet", ["js"] = "javascript",
        ["ts"] = "typescript", ["reactjs"] = "react", ["vuejs"] = "vue",
        ["node"] = "nodejs", ["postgres"] = "postgresql", ["python3"] = "python"
    };
    private static readonly double[] Weights = { .45, .35, .20 };

    public static string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var text = Marks.Replace(input.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "")
            .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا')
            .Replace('ة', 'ه').Replace('ى', 'ي').Replace('ی', 'ي').Replace('ک', 'ك');
        text = string.Concat(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠')
            : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c));
        foreach (var (pattern, replacement) in LanguageNames) text = pattern.Replace(text, replacement);
        return text;
    }

    public static List<string> Tokenize(string input)
    {
        var result = new List<string>();
        foreach (Match match in Words.Matches(NormalizeText(input)))
        {
            string word = match.Value;
            if (StopWords.Contains(word)) continue;
            foreach (string prefix in new[] { "وال", "بال", "كال", "فال", "لل", "ال" })
                if (word.StartsWith(prefix, StringComparison.Ordinal) && word.Length >= prefix.Length + 3)
                { word = word[prefix.Length..]; break; }
            if (word.Length > 4 && word.All(c => c is >= 'a' and <= 'z'))
            {
                if (word.Length > 5 && word.EndsWith("ies", StringComparison.Ordinal)) word = word[..^3] + "y";
                else if (new[] { "ches", "shes", "xes", "zes", "sses" }.Any(s => word.EndsWith(s, StringComparison.Ordinal))) word = word[..^2];
                else if (word.EndsWith('s') && !new[] { "ss", "us", "is" }.Any(s => word.EndsWith(s, StringComparison.Ordinal))) word = word[..^1];
            }
            if (word.Length >= 2 && !StopWords.Contains(word)) result.Add(word);
        }
        return result;
    }

    public static List<string> NormalizeTechnologies(IEnumerable<string> values) =>
        (values ?? Array.Empty<string>()).Select(value =>
        {
            string compact = string.Concat(Words.Matches(NormalizeText(value)).Select(m => m.Value));
            return TechnologyAliases.TryGetValue(compact, out var alias) ? alias : compact;
        }).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private static List<string>[] Fields(string title, string description, IEnumerable<string> technologies) =>
        new[] { Tokenize(title), Tokenize(description), NormalizeTechnologies(technologies) };

    private static Dictionary<string, double> Vector(List<string> tokens, Dictionary<string, int> frequency, int count) =>
        tokens.GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key,
            g => (1 + Math.Log(g.Count())) * (Math.Log((count + 1.0) / (frequency.GetValueOrDefault(g.Key) + 1.0)) + 1),
            StringComparer.Ordinal);

    private static SimilarityScore Score(int index, List<string>[] query, List<string>[] document,
        Dictionary<string, int>[] frequencies, int count)
    {
        // Shared programming languages alone do not establish a similar project.
        if (!Enumerable.Range(0, 2).Any(i => query[i].Count > 0 && document[i].Count > 0))
            return new SimilarityScore { Index = index };
        double score = 0, availableWeight = 0;
        var overlap = new HashSet<string>(StringComparer.Ordinal);
        for (int field = 0; field < 3; field++)
        {
            if (query[field].Count == 0 || document[field].Count == 0) continue;
            var a = Vector(query[field], frequencies[field], count);
            var b = Vector(document[field], frequencies[field], count);
            double dot = 0;
            foreach (var term in a.Keys.Intersect(b.Keys, StringComparer.Ordinal))
            { dot += a[term] * b[term]; overlap.Add(term); }
            double norm = Math.Sqrt(a.Values.Sum(v => v * v) * b.Values.Sum(v => v * v));
            score += Weights[field] * (norm == 0 ? 0 : dot / norm);
            availableWeight += Weights[field];
        }
        return new SimilarityScore
        {
            Index = index, Score = Math.Round(Math.Clamp(score / availableWeight, 0, 1), 4),
            Keywords = overlap.Where(t => t.Length <= 64).OrderBy(t => t, StringComparer.Ordinal).Take(24).ToList()
        };
    }

    public static List<SimilarityScore> ScoreDocuments(SimilarityQuery query, IReadOnlyList<SimilarityDocument> documents)
    {
        var fields = documents.Select(d => Fields(d.Title, d.Description, d.Technologies)).ToList();
        var frequencies = Enumerable.Range(0, 3).Select(field =>
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var document in fields)
                foreach (var term in document[field].Distinct(StringComparer.Ordinal))
                    result[term] = result.GetValueOrDefault(term) + 1;
            return result;
        }).ToArray();
        var source = Fields(query.Title, query.Description, query.Technologies);
        return fields.Select((document, index) => Score(index, source, document, frequencies, documents.Count)).ToList();
    }

    public static List<SimilarityResult> BuildResults(SimilarityQuery query, IReadOnlyList<SimilarityDocument> documents,
        IEnumerable<SimilarityScore> scores, int maxResults = int.MaxValue) =>
        scores.Where(s => s.Score >= .25).OrderByDescending(s => s.Score).ThenBy(s => s.Index).Take(maxResults).Select(s =>
        {
            var document = documents[s.Index];
            bool reveal = document.RevealDetails || document.SourceType == "Archive";
            return ProjectIdeaService.BuildSimilarityResult(query.ExcludeIdeaId, reveal ? document.Id : null,
                reveal ? document.Title : "فكرة لفريق آخر", document.SourceType, s.Score,
                reveal ? s.Keywords : new List<string>());
        }).ToList();

    public static List<SimilarityResult> Compare(SimilarityQuery query, IReadOnlyList<SimilarityDocument> documents, int maxResults = int.MaxValue) =>
        BuildResults(query, documents, ScoreDocuments(query, documents), maxResults);

    public static double ComparePair(SimilarityQuery query, SimilarityDocument document, out List<string> overlapping)
    {
        var result = ScoreDocuments(query, new[] { document })[0];
        overlapping = result.Keywords;
        return result.Score;
    }

    public static TrackClassificationResult ClassifyTrack(string title, string description, List<string> technologies)
    {
        var allTokens = Tokenize(title).Concat(Tokenize(description)).ToHashSet(StringComparer.Ordinal);
        var titleTokens = Tokenize(title).ToHashSet(StringComparer.Ordinal);
        var techs = NormalizeTechnologies(technologies).ToHashSet(StringComparer.Ordinal);

        var trackDefs = new (string Track, string Arabic, string[] Keywords, string[] Techs)[]
        {
            ("AI & Machine Learning", "الذكاء الاصطناعي وتعلم الآلة",
                new[] { "ai", "artificial", "intelligence", "machine", "learning", "deep", "neural", "network", "cnn", "rnn", "lstm", "transformer", "llm", "nlp", "vision", "opencv", "pytorch", "tensorflow", "keras", "scikit", "model", "prediction", "classification", "clustering", "detection", "yolo", "dataset", "training", "ذكاء", "اصطناعي", "تعلم", "عميق", "اله", "شبكات", "عصبيه", "رؤيه", "حاسوبيه", "تصنيف", "تنبؤ", "كشف", "نموذج", "معالجه", "لغات", "طبيعيه" },
                new[] { "python", "pytorch", "tensorflow", "opencv", "scikit-learn", "keras", "fastapi", "numpy", "pandas" }),
            ("Cybersecurity & Blockchain", "الأمن السيبراني وبلوك تشين",
                new[] { "security", "cybersecurity", "encryption", "crypto", "cryptography", "blockchain", "ethereum", "solidity", "smart", "contract", "penetration", "malware", "vulnerability", "web3", "authentication", "authorization", "firewall", "hash", "tamper", "ledger", "signature", "audit", "threat", "امن", "سيبراني", "تشفير", "حمايه", "بلوك", "تشين", "سلسله", "كتل", "عقود", "ذكيه", "اختراق", "ثغره", "تحقق", "هويه", "توقيع", "رقمي" },
                new[] { "solidity", "ethereum", "web3", "web3.js", "metamask", "truffle", "hardhat", "python", "csharp", "cplusplus" }),
            ("IoT & Robotics", "إنترنت الأشياء والروبوتات",
                new[] { "iot", "robot", "robotics", "arduino", "raspberry", "sensor", "sensors", "lidar", "ros", "ros2", "autonomous", "drone", "embedded", "controller", "actuator", "hardware", "esp32", "esp8266", "telemetry", "firmware", "انترنت", "اشياء", "روبوت", "روبوتات", "حساس", "حساسات", "اردوينو", "راسبيري", "طائره", "مسيره", "تحكم", "مدمج", "عتاد", "ملاحه", "ذاتيه" },
                new[] { "ros", "ros2", "cplusplus", "python", "arduino", "raspberry pi", "mqtt", "c", "esp32" }),
            ("Web & Cloud Systems", "تطوير الويب والحوسبة السحابية",
                new[] { "web", "portal", "cloud", "api", "rest", "microservices", "docker", "kubernetes", "fullstack", "backend", "frontend", "server", "database", "aws", "azure", "graphql", "dashboard", "ecommerce", "saas", "ويب", "منصه", "سحابي", "سحابيه", "خدمات", "مصغره", "واجهه", "خادم", "قاعده", "بيانات", "متجر", "الكتروني", "بوابه" },
                new[] { "csharp", "dotnet", "asp.net", "react", "angular", "vue", "nodejs", "postgresql", "sql server", "docker" }),
            ("Mobile Applications", "تطبيقات الهواتف الذكية",
                new[] { "mobile", "android", "ios", "flutter", "react", "native", "app", "phone", "smartphone", "kotlin", "swift", "dart", "touch", "gps", "push", "notification", "تطبيق", "جوال", "هاتف", "اندرويد", "ايفون", "فلاتر", "اشعارات", "موقع" },
                new[] { "flutter", "dart", "kotlin", "swift", "react native", "android", "firebase" }),
            ("Healthcare Informatics", "المعلوماتية الطبية والرعاية الصحية",
                new[] { "health", "healthcare", "medical", "clinic", "hospital", "patient", "doctor", "diagnosis", "telehealth", "telemedicine", "ehr", "prescription", "clinical", "laboratory", "vital", "signs", "disease", "صحه", "رعايه", "صحيه", "طبي", "طبيه", "مستشفى", "عياده", "مريض", "طبيب", "تشخيص", "علاج", "استشاره", "مختبر", "اشعه", "مرض" },
                new[] { "python", "csharp", "fastapi", "react", "webrtc", "postgresql", "docker" }),
            ("Data Science & Analytics", "علم وتحليل البيانات",
                new[] { "data", "analytics", "visualization", "bi", "dashboard", "mining", "warehouse", "statistics", "pipeline", "etl", "big", "spark", "hadoop", "insights", "بيانات", "تحليل", "علم", "لوحه", "مؤشرات", "تنقيب", "احصاء", "مستودع", "استكشاف" },
                new[] { "python", "pandas", "numpy", "matplotlib", "power bi", "tableau", "spark", "sql" })
        };

        var scored = new List<(string Track, string Arabic, double Score, List<string> Keywords)>();
        foreach (var def in trackDefs)
        {
            var kwMatches = allTokens.Intersect(def.Keywords, StringComparer.Ordinal).ToList();
            var titleMatches = titleTokens.Intersect(def.Keywords, StringComparer.Ordinal).ToList();
            var techMatches = techs.Intersect(def.Techs, StringComparer.Ordinal).ToList();

            double s = (titleMatches.Count * 3.0) + (kwMatches.Count * 1.5) + (techMatches.Count * 2.5);
            if (s > 0)
            {
                var combined = kwMatches.Concat(techMatches).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Take(10).ToList();
                scored.Add((def.Track, def.Arabic, s, combined));
            }
        }

        if (scored.Count == 0)
        {
            scored.Add(("General Software Engineering", "هندسة البرمجيات العامة", 1.0, new List<string>()));
        }

        scored = scored.OrderByDescending(x => x.Score).ToList();
        double total = scored.Sum(x => x.Score);
        var recs = scored.Take(3).Select(x => new TrackRecommendation
        {
            Track = x.Track,
            ArabicName = x.Arabic,
            Confidence = Math.Round(Math.Clamp(x.Score / total, 0.20, 0.99), 2),
            MatchedKeywords = x.Keywords
        }).ToList();

        return new TrackClassificationResult
        {
            Engine = "csharp-ai",
            PrimaryTrack = recs[0].ArabicName,
            RecommendedTracks = recs
        };
    }

    public static IdeaQualityEvaluationResult EvaluateQuality(string title, string description, string objectives, List<string> technologies)
    {
        title ??= ""; description ??= ""; objectives ??= "";
        var strengths = new List<string>();
        var recommendations = new List<string>();
        int score = 0;

        int titleWords = Regex.Matches(title, @"\S+").Count;
        if (titleWords >= 3 && titleWords <= 15) { score += 20; strengths.Add("عنوان المشروع محدد وموجز وواضح."); }
        else if (titleWords < 3) { score += 5; recommendations.Add("عنوان المشروع قصير جدًا؛ وضّح مجال التطبيق والهدف الرئيسي في العنوان."); }
        else { score += 12; recommendations.Add("عنوان المشروع طويل بعض الشيء؛ يفضل اختصاره ليكون أكثر تركيزًا."); }

        int descWords = Regex.Matches(description, @"\S+").Count;
        if (descWords >= 50) { score += 35; strengths.Add("وصف الفكرة تفصيلي ويشرح المشكلة والحل المقترح بشكل جيد."); }
        else if (descWords >= 25) { score += 25; recommendations.Add("الوصف الحالي جيد ولكن يفضل إضافة تفاصيل عن نطاق العمل (Scope) والمستخدمين المستهدفين."); }
        else { score += 10; recommendations.Add("وصف الفكرة مختصر؛ يرجى توضيح المشكلة الواقعية، الحل المقترح، والنتائج المتوقعة بالتفصيل."); }

        int objWords = Regex.Matches(objectives, @"\S+").Count;
        if (objWords >= 20) { score += 25; strengths.Add("أهداف المشروع محددة وقابلة للقياس والتقييم الأكاديمي."); }
        else if (objWords >= 5) { score += 15; recommendations.Add("يفضل صياغة أهداف المشروع في نقاط واضحة وقابلة للقياس مرحليًا."); }
        else { score += 5; recommendations.Add("أهداف المشروع غير مفصلة؛ أضف أهدافًا فنية وأكاديمية للمشروع."); }

        var techs = NormalizeTechnologies(technologies);
        if (techs.Count >= 3) { score += 20; strengths.Add("المجموعة التقنية المختارة متكاملة وتغطي طبقات التطوير المختلفة."); }
        else if (techs.Count >= 1) { score += 12; recommendations.Add("حدد التقنيات المستخدمة بدقة لكل من الواجهة والخادم وقاعدة البيانات."); }
        else { recommendations.Add("لم يتم تحديد تقنيات المشروع؛ حدد لغات البرمجة وأطر العمل وقواعد البيانات."); }

        string grade = score >= 85 ? "ممتاز (جاهز للاعتماد)"
            : score >= 70 ? "جيد جدًا (يتطلب تحسينات طفيفة)"
            : score >= 50 ? "مقبول (يحتاج إلى إثراء وتفصيل)"
            : "غير مكتمل (يتطلب مراجعة شاملة)";

        return new IdeaQualityEvaluationResult
        {
            Engine = "csharp-ai",
            Score = score,
            Grade = grade,
            Strengths = strengths,
            Recommendations = recommendations
        };
    }
}
