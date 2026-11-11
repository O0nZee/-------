/**
 * ==========================================================================
 * FRONTEND - STUDENT PORTAL CONTROLLER
 * Handles interactive behaviors for the Student Web App.
 * ==========================================================================
 */

function switchStudentTab(tabId, element) {
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.tab-view').forEach(view => view.style.display = 'none');
    element.classList.add('active');
    document.getElementById(tabId).style.display = 'block';
}

function handleLiveAiCheck() {
    const title = document.getElementById('ideaTitleInput').value;
    const desc = document.getElementById('ideaDescInput').value;
    const tech = document.getElementById('ideaTechInput').value;
    const results = api.calculateSimilarity(title, desc, tech);
    const box = document.getElementById('aiOutputBox');
    const badge = document.getElementById('aiStatusBadge');

    if (results.length > 0) {
        const top = results[0];
        const pct = (top.score * 100).toFixed(1);

        if (top.risk === "High") {
            badge.className = "ui-badge ui-badge-admin";
            badge.innerText = `تحذير سرقة أدبية (${pct}%)!`;
            box.innerHTML = `
                <div class="ui-alert ui-alert-danger" style="flex-direction: column; align-items: flex-start;">
                    <div>⚠️ <strong>تطابق مرتفع جداً مع مشروع بأرشيف الكلية:</strong></div>
                    <div style="font-size: 0.88rem; margin-top: 0.4rem;">
                        <strong>المشروع المطابق:</strong> ${top.title} (${top.year})<br>
                        <strong>نسبة التشابه:</strong> <span style="font-size: 1.1rem; color: #dc2626;">${pct}%</span><br>
                        <strong>الكلمات المشتركة:</strong> ${top.overlap.join(", ")}<br>
                        <strong>توجيه النظام:</strong> يجب تغيير نطاق الفكرة أو تقديم إضافة جديدة واضحة لتجنب رفض الفكرة.
                    </div>
                </div>
            `;
        } else if (top.risk === "Moderate") {
            badge.className = "ui-badge ui-badge-supervisor";
            badge.innerText = `تشابه جزئي (${pct}%)`;
            box.innerHTML = `
                <div class="ui-alert ui-alert-warning" style="flex-direction: column; align-items: flex-start;">
                    <div>⚠️ <strong>تشابه جزئي مع مشروع (${top.title})</strong></div>
                    <div style="font-size: 0.85rem; margin-top: 0.3rem;">نسبة التشابه: ${pct}%. يرجى إبراز الميزة التنافسية لفكرتكم.</div>
                </div>
            `;
        } else {
            renderOriginalIdea(box, badge);
        }
    } else {
        renderOriginalIdea(box, badge);
    }
}

function renderOriginalIdea(box, badge) {
    badge.className = "ui-badge ui-badge-approved";
    badge.innerText = "فكرة مبتكرة (خالية من التكرار)";
    box.innerHTML = `
        <div class="ui-alert ui-alert-success">
            ✅ <strong>فكرة أصلية ومبتكرة!</strong> لم يتم رصد أي تكرار غير مقصود مع مشروعات الأرشيف السابقة.
        </div>
    `;
}

function submitNewIdea(e) {
    e.preventDefault();
    alert("تم تقديم فكرة المشروع بنجاح إلى المشرف الأكاديمي للاعتماد!");
}

function inviteMember() {
    const email = document.getElementById('memberEmailInput').value;
    if (!email) { alert("يرجى إدخال البريد الإلكتروني الجامعي للطالب"); return; }
    alert(`تم إرسال دعوة الانضمام بنجاح إلى: ${email}`);
    document.getElementById('memberEmailInput').value = "";
}

function advanceKanbanTask(taskId, nextStatus) {
    alert(`تم تحديث حالة المهمة بنجاح إلى: ${nextStatus}`);
    const bar = document.getElementById('mainProgressBar');
    const badge = document.getElementById('progressTextBadge');
    if (bar && badge) {
        bar.style.width = "75%";
        badge.innerText = "75.0% منجز";
    }
}
