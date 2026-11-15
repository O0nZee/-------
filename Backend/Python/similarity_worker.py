"""Local JSON worker for Arabic/English TF-IDF project comparison and AI analysis.

Uses the Python standard library only. Input and output are UTF-8, and no
project text is written to disk or sent over the network.
"""

import json
import math
import re
import sys
import unicodedata
from collections import Counter

PROTOCOL_VERSION = 1
MAX_INPUT_BYTES = 64 * 1024 * 1024
MAX_DOCUMENTS = 10000
MARKS = re.compile(r"[\u0610-\u061a\u0640\u064b-\u065f\u0670\u06d6-\u06ed]")
WORDS = re.compile(r"[^\W_]+", re.UNICODE)
TRANSLATION = str.maketrans(
    "أإآٱةىیک٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹",
    "ااااهييك01234567890123456789",
)
LANGUAGE_NAMES = [
    (re.compile(r"(?<!\w)c\+\+(?!\w)", re.I), "cplusplus"),
    (re.compile(r"(?<!\w)c#(?!\w)", re.I), "csharp"),
    (re.compile(r"(?<!\w)c\s+sharp(?!\w)", re.I), "csharp"),
    (re.compile(r"(?<!\w)\.net\b", re.I), "dotnet"),
]
TECHNOLOGY_ALIASES = {
    "cpp": "cplusplus", "csharp": "csharp", "net": "dotnet",
    "netcore": "dotnet", "dotnetcore": "dotnet", "js": "javascript",
    "ts": "typescript", "reactjs": "react", "vuejs": "vue",
    "node": "nodejs", "postgres": "postgresql", "python3": "python",
}
WEIGHTS = (0.45, 0.35, 0.20)

TRACK_PROFILES = {
    "AI & Machine Learning": {
        "arabic": "الذكاء الاصطناعي وتعلم الآلة",
        "keywords": {
            "ai", "artificial", "intelligence", "machine", "learning", "deep", "neural",
            "network", "cnn", "rnn", "lstm", "transformer", "llm", "nlp", "vision",
            "opencv", "pytorch", "tensorflow", "keras", "scikit", "model", "prediction",
            "classification", "clustering", "detection", "yolo", "dataset", "training",
            "ذكاء", "اصطناعي", "تعلم", "عميق", "اله", "شبكات", "عصبيه", "رؤيه", "حاسوبيه",
            "تصنيف", "تنبؤ", "كشف", "نموذج", "معالجه", "لغات", "طبيعيه"
        },
        "technologies": {"python", "pytorch", "tensorflow", "opencv", "scikit-learn", "keras", "fastapi", "numpy", "pandas"}
    },
    "Cybersecurity & Blockchain": {
        "arabic": "الأمن السيبراني وبلوك تشين",
        "keywords": {
            "security", "cybersecurity", "encryption", "crypto", "cryptography", "blockchain",
            "ethereum", "solidity", "smart", "contract", "penetration", "malware",
            "vulnerability", "web3", "authentication", "authorization", "firewall",
            "hash", "tamper", "ledger", "signature", "audit", "threat",
            "امن", "سيبراني", "تشفير", "حمايه", "بلوك", "تشين", "سلسله", "كتل", "عقود",
            "ذكيه", "اختراق", "ثغره", "تحقق", "هويه", "توقيع", "رقمي"
        },
        "technologies": {"solidity", "ethereum", "web3", "web3.js", "metamask", "truffle", "hardhat", "python", "csharp", "cplusplus"}
    },
    "IoT & Robotics": {
        "arabic": "إنترنت الأشياء والروبوتات",
        "keywords": {
            "iot", "robot", "robotics", "arduino", "raspberry", "sensor", "sensors",
            "lidar", "ros", "ros2", "autonomous", "drone", "embedded", "controller",
            "actuator", "hardware", "esp32", "esp8266", "telemetry", "firmware",
            "انترنت", "اشياء", "روبوت", "روبوتات", "حساس", "حساسات", "اردوينو", "راسبيري",
            "طائره", "مسيره", "تحكم", "مدمج", "عتاد", "ملاحه", "ذاتيه"
        },
        "technologies": {"ros", "ros2", "cplusplus", "python", "arduino", "raspberry pi", "mqtt", "c", "esp32"}
    },
    "Web & Cloud Systems": {
        "arabic": "تطوير الويب والحوسبة السحابية",
        "keywords": {
            "web", "portal", "cloud", "api", "rest", "microservices", "docker",
            "kubernetes", "fullstack", "backend", "frontend", "server", "database",
            "aws", "azure", "graphql", "dashboard", "ecommerce", "saas",
            "ويب", "منصه", "سحابي", "سحابيه", "خدمات", "مصغره", "واجهه", "خادم", "قاعده",
            "بيانات", "متجر", "الكتروني", "بوابه"
        },
        "technologies": {"csharp", "dotnet", "asp.net", "react", "angular", "vue", "nodejs", "postgresql", "sql server", "docker"}
    },
    "Mobile Applications": {
        "arabic": "تطبيقات الهواتف الذكية",
        "keywords": {
            "mobile", "android", "ios", "flutter", "react", "native", "app", "phone",
            "smartphone", "kotlin", "swift", "dart", "touch", "gps", "push", "notification",
            "تطبيق", "جوال", "هاتف", "اندرويد", "ايفون", "فلاتر", "اشعارات", "موقع"
        },
        "technologies": {"flutter", "dart", "kotlin", "swift", "react native", "android", "firebase"}
    },
    "Healthcare Informatics": {
        "arabic": "المعلوماتية الطبية والرعاية الصحية",
        "keywords": {
            "health", "healthcare", "medical", "clinic", "hospital", "patient", "doctor",
            "diagnosis", "telehealth", "telemedicine", "ehr", "prescription", "clinical",
            "laboratory", "vital", "signs", "disease",
            "صحه", "رعايه", "صحيه", "طبي", "طبيه", "مستشفى", "عياده", "مريض", "طبيب",
            "تشخيص", "علاج", "استشاره", "مختبر", "اشعه", "مرض"
        },
        "technologies": {"python", "csharp", "fastapi", "react", "webrtc", "postgresql", "docker"}
    },
    "Data Science & Analytics": {
        "arabic": "علم وتحليل البيانات",
        "keywords": {
            "data", "analytics", "visualization", "bi", "dashboard", "mining", "warehouse",
            "statistics", "pipeline", "etl", "big", "spark", "hadoop", "insights",
            "بيانات", "تحليل", "علم", "لوحه", "مؤشرات", "تنقيب", "احصاء", "مستودع", "استكشاف"
        },
        "technologies": {"python", "pandas", "numpy", "matplotlib", "power bi", "tableau", "spark", "sql"}
    }
}


def normalize_text(value):
    text = MARKS.sub("", unicodedata.normalize("NFKC", value or "").lower())
    text = text.translate(TRANSLATION)
    for pattern, replacement in LANGUAGE_NAMES:
        text = pattern.sub(replacement, text)
    return text


STOP_WORDS = {
    normalize_text(word)
    for word in (
        "a an the and or in on at to for with by from is are was were be been using used "
        "system project app application platform based graduation study management "
        "في من على إلى عن مع هذا هذه ذلك تلك هو هي هم أو و التي الذي يتم "
        "مشروع نظام تطبيق منصة تخرج إدارة دراسة باستخدام خلال بين كل يمكن"
    ).split()
}


def tokenize(value):
    tokens = []
    for word in WORDS.findall(normalize_text(value)):
        if word in STOP_WORDS:
            continue
        for prefix in ("وال", "بال", "كال", "فال", "لل", "ال"):
            if word.startswith(prefix) and len(word) >= len(prefix) + 3:
                word = word[len(prefix):]
                break
        if len(word) > 4 and word.isascii() and word.isalpha():
            if len(word) > 5 and word.endswith("ies"):
                word = word[:-3] + "y"
            elif word.endswith(("ches", "shes", "xes", "zes", "sses")):
                word = word[:-2]
            elif word.endswith("s") and not word.endswith(("ss", "us", "is")):
                word = word[:-1]
        if len(word) >= 2 and word not in STOP_WORDS:
            tokens.append(word)
    return tokens


def normalize_technologies(values):
    result = []
    for value in values or []:
        compact = "".join(WORDS.findall(normalize_text(value)))
        canonical = TECHNOLOGY_ALIASES.get(compact, compact)
        if canonical and canonical not in result:
            result.append(canonical)
    return result


def fields(document):
    return (
        tokenize(document.get("title", "")),
        tokenize(document.get("description", "")),
        normalize_technologies(document.get("technologies", [])),
    )


def vector(tokens, frequency, count):
    return {
        term: (1 + math.log(amount)) * (math.log((count + 1) / (frequency.get(term, 0) + 1)) + 1)
        for term, amount in Counter(tokens).items()
    }


def score_document(index, query, document, frequencies, count):
    if not any(query[i] and document[i] for i in range(2)):
        return {"index": index, "score": 0.0, "keywords": []}
    total, available_weight, overlap = 0.0, 0.0, set()
    for field, weight in enumerate(WEIGHTS):
        if not query[field] or not document[field]:
            continue
        a = vector(query[field], frequencies[field], count)
        b = vector(document[field], frequencies[field], count)
        common = set(a).intersection(b)
        overlap.update(common)
        dot = sum(a[term] * b[term] for term in sorted(common))
        norm = math.sqrt(sum(v * v for v in a.values()) * sum(v * v for v in b.values()))
        total += weight * (dot / norm if norm else 0.0)
        available_weight += weight
    return {
        "index": index,
        "score": round(min(1.0, max(0.0, total / available_weight)), 4),
        "keywords": sorted(term for term in overlap if len(term) <= 64)[:24],
    }


def validate_document(document):
    if not isinstance(document, dict):
        raise ValueError("invalid_document")
    for field in ("title", "description", "objectives"):
        value = document.get(field, "")
        if value is not None and not isinstance(value, str):
            raise ValueError("invalid_text")
        if len(value or "") > 20000:
            raise ValueError("text_too_long")
    technologies = document.get("technologies", [])
    if technologies is None:
        technologies = []
    if not isinstance(technologies, list) or len(technologies) > 20:
        raise ValueError("invalid_technologies")
    if any(not isinstance(value, str) or len(value) > 60 for value in technologies):
        raise ValueError("invalid_technology")


def validate_request(request):
    if (not isinstance(request, dict) or type(request.get("protocolVersion")) is not int
            or request.get("protocolVersion") != PROTOCOL_VERSION):
        raise ValueError("unsupported_protocol")


def analyze(request):
    validate_request(request)
    query = request.get("query")
    documents = request.get("documents")
    validate_document(query)
    if not isinstance(documents, list) or len(documents) > MAX_DOCUMENTS:
        raise ValueError("invalid_corpus")
    max_results = request.get("maxResults", 10)
    if isinstance(max_results, bool) or not isinstance(max_results, int) or not 1 <= max_results <= 100:
        raise ValueError("invalid_limit")
    for document in documents:
        validate_document(document)
    corpus = [fields(document) for document in documents]
    frequencies = [
        Counter(term for document in corpus for term in set(document[field]))
        for field in range(3)
    ]
    source = fields(query)
    matches = [
        score_document(index, source, document, frequencies, len(corpus))
        for index, document in enumerate(corpus)
    ]
    matches = [match for match in matches if match["score"] >= 0.25]
    matches.sort(key=lambda match: (-match["score"], match["index"]))
    return {"protocolVersion": PROTOCOL_VERSION, "engine": "python-tfidf", "matches": matches[:max_results]}


def classify_track(request):
    validate_request(request)
    query = request.get("query")
    validate_document(query)
    
    title_tokens = set(tokenize(query.get("title", "")))
    desc_tokens = set(tokenize(query.get("description", "")))
    techs = set(normalize_technologies(query.get("technologies", [])))
    
    all_tokens = title_tokens | desc_tokens
    
    scored_tracks = []
    for track_name, profile in TRACK_PROFILES.items():
        kw_matches = all_tokens & profile["keywords"]
        title_matches = title_tokens & profile["keywords"]
        tech_matches = techs & profile["technologies"]
        
        score = (len(title_matches) * 3.0) + (len(kw_matches) * 1.5) + (len(tech_matches) * 2.5)
        if score > 0:
            scored_tracks.append({
                "track": track_name,
                "arabicName": profile["arabic"],
                "rawScore": score,
                "matchedKeywords": sorted(list(kw_matches | tech_matches))[:10]
            })
            
    scored_tracks.sort(key=lambda x: -x["rawScore"])
    if not scored_tracks:
        scored_tracks.append({
            "track": "General Software Engineering",
            "arabicName": "هندسة البرمجيات العامة",
            "rawScore": 1.0,
            "matchedKeywords": []
        })
        
    total_raw = sum(t["rawScore"] for t in scored_tracks) or 1.0
    results = []
    for t in scored_tracks[:3]:
        confidence = round(min(0.99, max(0.20, t["rawScore"] / total_raw)), 2)
        results.append({
            "track": t["track"],
            "arabicName": t["arabicName"],
            "confidence": confidence,
            "matchedKeywords": t["matchedKeywords"]
        })
        
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "engine": "python-ai",
        "action": "classify",
        "recommendedTracks": results,
        "primaryTrack": results[0]["arabicName"]
    }


def evaluate_quality(request):
    validate_request(request)
    query = request.get("query")
    validate_document(query)
    
    title = (query.get("title") or "").strip()
    description = (query.get("description") or "").strip()
    objectives = (query.get("objectives") or "").strip()
    technologies = query.get("technologies") or []
    
    strengths = []
    recommendations = []
    score = 0
    
    # Title analysis (max 20)
    title_words = len(title.split())
    if 3 <= title_words <= 15:
        score += 20
        strengths.append("عنوان المشروع محدد وموجز وواضح.")
    elif title_words < 3:
        score += 5
        recommendations.append("عنوان المشروع قصير جدًا؛ وضّح مجال التطبيق والهدف الرئيسي في العنوان.")
    else:
        score += 12
        recommendations.append("عنوان المشروع طويل بعض الشيء؛ يفضل اختصاره ليكون أكثر تركيزًا.")
        
    # Description analysis (max 35)
    desc_words = len(description.split())
    if desc_words >= 50:
        score += 35
        strengths.append("وصف الفكرة تفصيلي ويشرح المشكلة والحل المقترح بشكل جيد.")
    elif desc_words >= 25:
        score += 25
        recommendations.append("الوصف الحالي جيد ولكن يفضل إضافة تفاصيل عن نطاق العمل (Scope) والمستخدمين المستهدفين.")
    else:
        score += 10
        recommendations.append("وصف الفكرة مختصر؛ يرجى توضيح المشكلة الواقعية، الحل المقترح، والنتائج المتوقعة بالتفصيل.")
        
    # Objectives analysis (max 25)
    obj_words = len(objectives.split())
    if obj_words >= 20:
        score += 25
        strengths.append("أهداف المشروع محددة وقابلة للقياس والتقييم الأكاديمي.")
    elif obj_words >= 5:
        score += 15
        recommendations.append("يفضل صياغة أهداف المشروع في نقاط واضحة وقابلة للقياس مرحليًا.")
    else:
        score += 5
        recommendations.append("أهداف المشروع غير مفصلة؛ أضف أهدافًا فنية وأكاديمية للمشروع.")
        
    # Technologies analysis (max 20)
    norm_techs = normalize_technologies(technologies)
    if len(norm_techs) >= 3:
        score += 20
        strengths.append("المجموعة التقنية المختارة متكاملة وتغطي طبقات التطوير المختلفة.")
    elif len(norm_techs) >= 1:
        score += 12
        recommendations.append("حدد التقنيات المستخدمة بدقة لكل من الواجهة والخادم وقاعدة البيانات.")
    else:
        score += 0
        recommendations.append("لم يتم تحديد تقنيات المشروع؛ حدد لغات البرمجة وأطر العمل وقواعد البيانات.")
        
    # Quality Grade
    if score >= 85:
        grade = "ممتاز (جاهز للاعتماد)"
    elif score >= 70:
        grade = "جيد جدًا (يتطلب تحسينات طفيفة)"
    elif score >= 50:
        grade = "مقبول (يحتاج إلى إثراء وتفصيل)"
    else:
        grade = "غير مكتمل (يتطلب مراجعة شاملة)"
        
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "engine": "python-ai",
        "action": "evaluate",
        "score": score,
        "grade": grade,
        "strengths": strengths,
        "recommendations": recommendations,
        "wordCount": {
            "title": title_words,
            "description": desc_words,
            "objectives": obj_words,
            "technologiesCount": len(norm_techs)
        }
    }


def extract_key_terms(request):
    validate_request(request)
    query = request.get("query")
    validate_document(query)
    
    tokens = tokenize(query.get("title", "")) + tokenize(query.get("description", ""))
    counts = Counter(tokens)
    keywords = [term for term, _ in counts.most_common(15)]
    technologies = normalize_technologies(query.get("technologies", []))
    
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "engine": "python-ai",
        "action": "keywords",
        "keywords": keywords,
        "technologies": technologies
    }


def main():
    if sys.version_info < (3, 10):
        raise RuntimeError("python_3_10_required")
    if sys.argv[1:] == ["--health"]:
        print(json.dumps({
            "protocolVersion": PROTOCOL_VERSION,
            "engine": "python-tfidf",
            "pythonVersion": list(sys.version_info[:3]),
            "capabilities": ["similarity", "classify", "evaluate", "keywords"]
        }))
        return
    raw = sys.stdin.buffer.read(MAX_INPUT_BYTES + 1)
    if len(raw) > MAX_INPUT_BYTES:
        raise ValueError("request_too_large")
    request = json.loads(raw.decode("utf-8"))
    validate_request(request)

    action = request.get("action", "similarity")
    if action == "similarity":
        result = analyze(request)
    elif action == "classify":
        result = classify_track(request)
    elif action == "evaluate":
        result = evaluate_quality(request)
    elif action == "keywords":
        result = extract_key_terms(request)
    else:
        raise ValueError("unsupported_action")
        
    print(json.dumps(result, ensure_ascii=False, allow_nan=False, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, TypeError, UnicodeError, RuntimeError, RecursionError):
        print(json.dumps({"protocolVersion": PROTOCOL_VERSION, "error": "invalid_request"}))
        sys.exit(2)
