/**
 * ==========================================================================
 * FRONTEND - API SERVICE & STATE LAYER
 * Connects Frontend views to the Backend REST API (with standalone mock fallback).
 * Modify this file when changing API endpoints or client state logic.
 * ==========================================================================
 */

class ApiService {
    constructor() {
        this.baseUrl = "http://localhost:5000/api";
        this.state = this.loadState();
    }

    loadState() {
        const cached = localStorage.getItem("GRAD_CORE_STATE");
        if (cached) {
            try { return JSON.parse(cached); } catch (e) { }
        }
        return this.getDefaultState();
    }

    saveState() {
        localStorage.setItem("GRAD_CORE_STATE", JSON.stringify(this.state));
    }

    getDefaultState() {
        return {
            archive: [
                {
                    id: "arch_1",
                    title: "Smart Attendance System using Facial Recognition",
                    abstract: "Automated classroom attendance using CNNs and IP cameras for universities.",
                    year: "2023-2024",
                    track: "AI & Computer Vision",
                    technologies: ["Python", "OpenCV", "TensorFlow", "React"],
                    supervisor: "أ.د. محمد عبد الرحمن",
                    grade: "A+"
                },
                {
                    id: "arch_2",
                    title: "Telemedicine Consultation and Electronic Health Records",
                    abstract: "Secure HIPAA compliant telehealth platform connecting doctors with rural patients.",
                    year: "2022-2023",
                    track: "Healthcare Informatics",
                    technologies: ["C#", "ASP.NET Core", "SQL Server", "WebRTC"],
                    supervisor: "د. رانيا محمود",
                    grade: "A"
                },
                {
                    id: "arch_3",
                    title: "Autonomous Mobile Delivery Robot for Campus",
                    abstract: "Indoor navigation robot delivering mail across campus using LiDAR and SLAM.",
                    year: "2023-2024",
                    track: "Robotics & IoT",
                    technologies: ["ROS2", "Python", "C++", "Raspberry Pi"],
                    supervisor: "د. خالد سامي",
                    grade: "A"
                }
            ],
            supervisors: [
                { id: "dr_mostafa", name: "د. مصطفى كامل", title: "أستاذ مساعد", dept: "علوم الحاسب", max: 3, current: 1 },
                { id: "dr_rania", name: "د. رانيا محمود", title: "أستاذ مساعد", dept: "نظم المعلومات", max: 3, current: 2 },
                { id: "prof_hossam", name: "أ.د. حسام الشافعي", title: "أستاذ دكتور", dept: "علوم الحاسب", max: 3, current: 3 }
            ]
        };
    }

    // AI Cosine Similarity Engine (Member 2 algorithm running on frontend)
    calculateSimilarity(targetTitle, targetDesc, targetTech) {
        const fullInput = `${targetTitle} ${targetTitle} ${targetDesc} ${targetTech}`.toLowerCase();
        const results = [];

        this.state.archive.forEach(item => {
            const archiveText = `${item.title} ${item.title} ${item.abstract} ${item.technologies.join(" ")}`.toLowerCase();
            const tokensA = this.tokenize(fullInput);
            const tokensB = this.tokenize(archiveText);

            const score = this.cosineScore(tokensA, tokensB);
            if (score >= 0.25) {
                const overlap = tokensA.filter(t => tokensB.includes(t));
                results.push({
                    title: item.title,
                    year: item.year,
                    score: Math.min(1.0, score),
                    risk: score >= 0.70 ? "High" : (score >= 0.40 ? "Moderate" : "Low"),
                    overlap: Array.from(new Set(overlap))
                });
            }
        });

        return results.sort((a, b) => b.score - a.score);
    }

    tokenize(text) {
        const stopWords = new Set(["a", "an", "the", "in", "on", "for", "with", "system", "project", "app", "من", "في", "على", "مشروع", "نظام", "باستخدام"]);
        return text.replace(/[^\w\s\u0600-\u06FF]/gi, " ")
            .split(/\s+/)
            .map(w => w.trim())
            .filter(w => w.length >= 2 && !stopWords.has(w));
    }

    cosineScore(tokensA, tokensB) {
        if (!tokensA.length || !tokensB.length) return 0;
        const freqA = {}, freqB = {};
        tokensA.forEach(t => freqA[t] = (freqA[t] || 0) + 1);
        tokensB.forEach(t => freqB[t] = (freqB[t] || 0) + 1);

        const all = new Set([...Object.keys(freqA), ...Object.keys(freqB)]);
        let dot = 0, normA = 0, normB = 0;

        all.forEach(k => {
            const a = freqA[k] || 0;
            const b = freqB[k] || 0;
            dot += a * b;
            normA += a * a;
            normB += b * b;
        });

        if (normA === 0 || normB === 0) return 0;
        return dot / (Math.sqrt(normA) * Math.sqrt(normB));
    }
}

const api = new ApiService();
