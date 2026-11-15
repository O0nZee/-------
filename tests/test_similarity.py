import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

WORKER = Path(__file__).resolve().parents[1] / "Backend" / "Python" / "similarity_worker.py"
SPEC = importlib.util.spec_from_file_location("project_similarity_worker", WORKER)
worker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(worker)


def request(query, documents, limit=10):
    return {"protocolVersion": 1, "query": query, "documents": documents, "maxResults": limit}


class SimilarityTests(unittest.TestCase):
    def test_arabic_diacritics_and_letters(self):
        self.assertEqual(worker.tokenize("نِظَام مُتَابَعَة الطُّلَّاب"), worker.tokenize("متابعه الطلاب"))
        self.assertEqual(worker.normalize_text("أإآٱىةیک"), "اااايهيك")

    def test_unicode_digits_and_presentation_forms(self):
        self.assertEqual(worker.normalize_text("٢٠٢٦ ۲۰۲۷"), "2026 2027")
        self.assertEqual(worker.tokenize("ﻣﺘﺎﺑﻌﺔ"), worker.tokenize("متابعة"))

    def test_english_plural_forms(self):
        self.assertEqual(worker.tokenize("students universities sensors"), worker.tokenize("student university sensor"))
        self.assertIn("analysis", worker.tokenize("analysis"))

    def test_technology_aliases_preserve_distinct_languages(self):
        self.assertEqual(worker.normalize_technologies(["C#", ".NET", "React.js"]), ["csharp", "dotnet", "react"])
        self.assertEqual(worker.normalize_technologies(["C Sharp", "DotNet", "ReactJS"]), ["csharp", "dotnet", "react"])
        self.assertNotEqual(worker.normalize_technologies(["C++"]), worker.normalize_technologies(["C#"]))

    def test_empty_and_stop_words(self):
        self.assertEqual(worker.tokenize(None), [])
        self.assertEqual(worker.tokenize("في من مشروع نظام application management"), [])
        self.assertEqual(worker.analyze(request({"title": "طاقة"}, []))["matches"], [])

    def test_identical_project_without_technologies(self):
        project = {"title": "متابعة استهلاك الطاقة", "description": "حساسات لاسلكية لقياس الاستهلاك داخل الحرم الجامعي"}
        self.assertEqual(worker.analyze(request(project, [project]))["matches"][0]["score"], 1.0)

    def test_title_only_does_not_lose_weight(self):
        query = {"title": "Campus Energy Telemetry"}
        document = {"title": "Campus Energy Telemetry", "description": "Wireless sensors measure electricity."}
        self.assertEqual(worker.analyze(request(query, [document]))["matches"][0]["score"], 1.0)

    def test_shared_technology_does_not_match_unrelated_projects(self):
        query = {"title": "Energy sensors", "description": "Calibrating wireless electricity meters", "technologies": ["Python"]}
        unrelated = {"title": "Clinical diagnosis", "description": "Doctors classify patient medical scans", "technologies": ["Python"]}
        self.assertEqual(worker.analyze(request(query, [unrelated]))["matches"], [])
        self.assertEqual(worker.analyze(request({"title": "project", "technologies": ["Python"]}, [unrelated]))["matches"], [])

    def test_ranking_limit_and_stable_ties(self):
        query = {"title": "Wireless energy sensors", "description": "Measurement calibration electricity"}
        documents = [dict(query) for _ in range(4)]
        self.assertEqual([m["index"] for m in worker.analyze(request(query, documents, 2))["matches"]], [0, 1])

    def test_invalid_request_limits_and_types(self):
        for invalid in (
            {"protocolVersion": 9}, request({"title": 123}, []),
            request({"title": "valid"}, None), request({"title": "valid"}, [], False),
            request({"title": "valid", "technologies": [None]}, []),
            request({"title": "valid"}, [{}] * 10001),
        ):
            with self.subTest(invalid_type=type(invalid)):
                with self.assertRaises(ValueError):
                    worker.analyze(invalid)

    def test_real_utf8_json_protocol_and_health(self):
        project = {"title": "متابعة الطاقة", "description": "حساسات لقياس استهلاك الكهرباء", "technologies": ["C#"]}
        result = subprocess.run(
            [sys.executable, "-I", "-B", "-X", "utf8", str(WORKER)],
            input=json.dumps(request(project, [project]), ensure_ascii=False).encode("utf-8"),
            capture_output=True, timeout=5, check=True,
        )
        self.assertEqual(json.loads(result.stdout)["matches"][0]["score"], 1.0)
        health = subprocess.run([sys.executable, "-I", "-B", "-X", "utf8", str(WORKER), "--health"],
                                capture_output=True, timeout=5, check=True)
        self.assertEqual(json.loads(health.stdout)["engine"], "python-tfidf")

    def test_bad_json_returns_a_controlled_error(self):
        result = subprocess.run([sys.executable, "-I", "-B", "-X", "utf8", str(WORKER)],
                                input=b'{"invalid"', capture_output=True, timeout=5)
        self.assertEqual(result.returncode, 2)
        self.assertEqual(json.loads(result.stdout)["error"], "invalid_request")
        self.assertEqual(result.stderr, b"")

    def test_classification_and_quality_analysis(self):
        query = {"title": "رؤية حاسوبية للتعرف على الوجوه", "description": "شبكات عصبية وتعلم عميق لتصنيف الصور", "technologies": ["Python", "PyTorch", "OpenCV"]}
        classification = worker.classify_track({"protocolVersion": 1, "query": query})
        self.assertEqual(classification["recommendedTracks"][0]["track"], "AI & Machine Learning")
        self.assertEqual(classification["engine"], "python-ai")
        complete = {"title": "متابعة استهلاك الطاقة", "description": "\n".join(["قياس"] * 50), "objectives": "\t".join(["تحسين"] * 20), "technologies": ["C#", ".NET", "IoT"]}
        evaluation = worker.evaluate_quality({"protocolVersion": 1, "query": complete})
        self.assertEqual(evaluation["score"], 100)
        self.assertEqual(evaluation["recommendations"], [])

    def test_all_analysis_actions_validate_protocol_and_query(self):
        for action in (worker.classify_track, worker.evaluate_quality, worker.extract_key_terms):
            for invalid in ([1], {"protocolVersion": True, "query": {}}, {"protocolVersion": 9, "query": {}}, {"protocolVersion": 1, "query": []}):
                with self.subTest(action=action.__name__, request=invalid):
                    with self.assertRaises(ValueError):
                        action(invalid)
        result = subprocess.run([sys.executable, "-I", "-B", "-X", "utf8", str(WORKER)],
                                input=b'[]', capture_output=True, timeout=5)
        self.assertEqual(result.returncode, 2)
        self.assertEqual(json.loads(result.stdout)["error"], "invalid_request")
        self.assertEqual(result.stderr, b"")


if __name__ == "__main__":
    unittest.main()
