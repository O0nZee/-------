/**
 * Graduation Project Platform - Client Store & API Service
 * Supports both standalone client execution and live backend REST API.
 */

// Stop words dictionary for Arabic & English
const StopWords = new Set([
    "a", "an", "the", "and", "or", "in", "on", "at", "to", "for", "with", "by", "from",
    "is", "are", "was", "were", "using", "used", "system", "project", "app", "application", "platform",
    "في", "من", "على", "إلى", "عن", "مع", "هذا", "هذه", "ذلك", "تلك", "هو", "هي", "مشروع", "نظام", "تطبيق", "منصة", "باستخدام"
]);

// Seed data
const InitialState = {
    currentUser: {
        id: "student_ahmed",
        name: "أحمد منصور (Ahmed Mansour)",
        role: "Student",
        email: "ahmed.mansour@alexu.edu.eg",
        department: "علوم الحاسب (Computer Science)",
        academicId: "ST-2026-101"
    },
    archiveProjects: [
        {
            id: "arch_1",
            title: "Smart Attendance System using Facial Recognition",
            titleAr: "نظام التحضير الذكي للطلاب بالتعرف على الوجه",
            abstract: "Classroom attendance monitoring using deep learning CNNs and live cameras for universities.",
            year: "2023-2024",
            track: "AI & Computer Vision",
            technologies: ["Python", "OpenCV", "TensorFlow", "FastAPI"],
            supervisor: "أ.د. محمد عبد الرحمن",
            grade: "امتياز مع مرتبة الشرف (A+)"
        },
        {
            id: "arch_2",
            title: "Telemedicine Consultation and Electronic Health Records",
            titleAr: "منصة الاستشارات الطبية عن بعد والسجلات الصحية",
            abstract: "Secure HIPAA compliant telehealth platform connecting rural patients with video calls.",
            year: "2022-2023",
            track: "Healthcare Informatics",
            technologies: ["C#", "ASP.NET Core", "SQL Server", "WebRTC"],
            supervisor: "د. رانيا محمود",
            grade: "امتياز (A)"
        },
        {
            id: "arch_3",
            title: "Autonomous Mobile Delivery Robot for Campus",
            titleAr: "روبوت التوصيل الذاتي للتنقل داخل الحرم الجامعي",
            abstract: "Indoor localization and obstacle avoidance robot navigating campus corridors using LiDAR and SLAM.",
            year: "2023-2024",
            track: "Robotics & IoT",
            technologies: ["ROS2", "Python", "C++", "Raspberry Pi"],
            supervisor: "د. خالد سامي",
            grade: "امتياز (A)"
        },
        {
            id: "arch_4",
            title: "Blockchain-Based Academic Degree Verification System",
            titleAr: "نظام توثيق الشهادات الجامعية بتقنية البلوك تشين",
            abstract: "Decentralized ledger for tamper-proof storage and instant verification of university diplomas.",
            year: "2022-2023",
            track: "Cybersecurity & Blockchain",
            technologies: ["Solidity", "Ethereum", "Node.js", "React"],
            supervisor: "أ.د. عصام حمدي",
            grade: "امتياز (A)"
        }
    ],
    ideas: [
        {
            id: "idea_1",
            title: "Smart IoT Microgrid Energy Optimization for Campus",
            titleAr: "تحسين استهلاك الطاقة بالشبكات الذكية وإنترنت الأشياء بالحرم الجامعي",
            description: "Deploying LoRaWAN power sensors and AI predictive models to dynamically balance energy loads across campus buildings.",
            technologies: ["IoT", "Python", "LoRaWAN", "PyTorch", "FastAPI"],
            track: "IoT & Smart Systems",
            studentId: "student_ahmed",
            studentName: "أحمد منصور",
            status: "Approved",
            supervisorFeedback: "فكرة ممتازة ومبتكرة جداً، تلبي معايير ترشيد الطاقة والاستدامة بالحرم الجامعي. معتمدة."
        }
    ],
    teams: [
        {
            id: "team_1",
            name: "فريق رواد الطاقة الذكية (EnergyGrid)",
            leaderId: "student_ahmed",
            leaderName: "أحمد منصور",
            ideaId: "idea_1",
            ideaTitle: "Smart IoT Microgrid Energy Optimization",
            supervisorId: "dr_mostafa",
            supervisorName: "د. مصطفى كامل",
            members: [
                { id: "student_ahmed", name: "أحمد منصور", role: "قائد الفريق (Team Leader)" },
                { id: "student_karim", name: "كريم نبيل", role: "مهندس نظم مدمجة (Embedded & IoT)" },
                { id: "student_mona", name: "منى شوقي", role: "مهندسة ذكاء اصطناعي (AI Engineer)" }
            ],
            maxMembers: 4
        }
    ],
    supervisors: [
        {
            id: "dr_mostafa",
            name: "د. مصطفى كامل (Dr. Mostafa Kamel)",
            title: "أستاذ مساعد (Assoc. Prof.)",
            department: "علوم الحاسب (Computer Science)",
            specialization: "إنترنت الأشياء والأنظمة الذكية",
            maxTeams: 3,
            activeTeams: 1
        },
        {
            id: "dr_rania",
            name: "د. رانيا محمود (Dr. Rania Mahmoud)",
            title: "أستاذ مساعد (Assoc. Prof.)",
            department: "نظم المعلومات (Information Systems)",
            specialization: "المعلوماتية الطبية والحوسبة السحابية",
            maxTeams: 3,
            activeTeams: 2
        },
        {
            id: "prof_hossam",
            name: "أ.د. حسام الشافعي (Prof. Hossam)",
            title: "أستاذ دكتور (Professor)",
            department: "علوم الحاسب (Computer Science)",
            specialization: "الذكاء الاصطناعي وتعلم الآلة",
            maxTeams: 3,
            activeTeams: 3 // Full capacity!
        }
    ],
    tasks: [
        {
            id: "task_1",
            teamId: "team_1",
            title: "إعداد وثيقة توصيف المتطلبات (SRS Document)",
            assignee: "أحمد منصور",
            phase: "المرحلة الأولى: المقترح وتوصيف المتطلبات",
            status: "Completed",
            priority: "High",
            dueDate: "2026-11-01"
        },
        {
            id: "task_2",
            teamId: "team_1",
            title: "مراجعة وحصر الأبحاث السابقة (Literature Review)",
            assignee: "أحمد منصور",
            phase: "المرحلة الأولى: المقترح وتوصيف المتطلبات",
            status: "Completed",
            priority: "Medium",
            dueDate: "2026-11-05"
        },
        {
            id: "task_3",
            teamId: "team_1",
            title: "تصميم دوائر حساسات الطاقة وتوصيل ESP32",
            assignee: "كريم نبيل",
            phase: "المرحلة الثانية: التصميم المعماري وقواعد البيانات",
            status: "InProgress",
            priority: "High",
            dueDate: "2026-11-20"
        },
        {
            id: "task_4",
            teamId: "team_1",
            title: "تدريب نموذج التنبؤ بالأحمال الكهربائية (LSTM Neural Network)",
            assignee: "منى شوقي",
            phase: "المرحلة الثانية: التصميم المعماري وقواعد البيانات",
            status: "ToDo",
            priority: "Urgent",
            dueDate: "2026-11-25"
        }
    ]
};

// State Manager
class PlatformStore {
    constructor() {
        const stored = localStorage.getItem("GRAD_PLATFORM_STATE");
        if (stored) {
            try {
                this.state = JSON.parse(stored);
            } catch (e) {
                this.state = InitialState;
            }
        } else {
            this.state = InitialState;
            this.save();
        }
    }

    save() {
        localStorage.setItem("GRAD_PLATFORM_STATE", JSON.stringify(this.state));
    }

    reset() {
        this.state = InitialState;
        this.save();
    }
}

const store = new PlatformStore();

// Text Normalizer & Cosine Similarity for Arabic & English
function tokenize(text) {
    if (!text) return [];
    let norm = text.toLowerCase()
        .replace(/[أإآ]/g, "ا")
        .replace(/ة/g, "ه")
        .replace(/ى/g, "ي")
        .replace(/[^\w\s]/gi, " ");
    return norm.split(/\s+/)
        .map(w => w.trim())
        .filter(w => w.length >= 2 && !StopWords.has(w));
}

function calculateCosineSimilarity(textA, textB) {
    const tokensA = tokenize(textA);
    const tokensB = tokenize(textB);
    if (!tokensA.length || !tokensB.length) return { score: 0, overlap: [] };

    const freqA = {}, freqB = {};
    tokensA.forEach(t => freqA[t] = (freqA[t] || 0) + 1);
    tokensB.forEach(t => freqB[t] = (freqB[t] || 0) + 1);

    const allTerms = new Set([...Object.keys(freqA), ...Object.keys(freqB)]);
    let dot = 0, normA = 0, normB = 0;
    const overlap = [];

    allTerms.forEach(term => {
        const a = freqA[term] || 0;
        const b = freqB[term] || 0;
        if (a > 0 && b > 0) overlap.push(term);
        dot += a * b;
        normA += a * a;
        normB += b * b;
    });

    if (normA === 0 || normB === 0) return { score: 0, overlap: [] };
    const score = dot / (Math.sqrt(normA) * Math.sqrt(normB));
    return { score: Math.min(1.0, score), overlap };
}

function checkPlagiarismAndSimilarity(title, desc, techString) {
    const results = [];
    const fullInput = `${title} ${title} ${desc} ${techString}`;

    store.state.archiveProjects.forEach(arch => {
        const archText = `${arch.title} ${arch.title} ${arch.abstract} ${arch.technologies.join(" ")}`;
        const res = calculateCosineSimilarity(fullInput, archText);
        if (res.score >= 0.25) {
            let risk = "Low";
            let rec = "فكرة مبتكرة وجديدة، نسبة التشابه طفيفة وطبيعية.";
            if (res.score >= 0.70) {
                risk = "High";
                rec = "تحذير سرقة أدبية أو تكرار مباشر! الفكرة متطابقة بنسبة عالية مع مشروع سابق، ويجب تغيير النطاق لتجنب الرفض.";
            } else if (res.score >= 0.40) {
                risk = "Moderate";
                rec = "يوجد تشابه جزئي في بعض التقنيات أو الأهداف. يجب توضيح القيمة المضافة للفكرة بوضوح.";
            }

            results.push({
                matchedTitle: arch.title,
                matchedYear: arch.year,
                score: res.score,
                risk: risk,
                overlap: res.overlap,
                recommendation: rec
            });
        }
    });

    return results.sort((a, b) => b.score - a.score);
}
