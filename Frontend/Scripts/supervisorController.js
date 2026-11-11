/**
 * ==========================================================================
 * FRONTEND - SUPERVISOR PORTAL CONTROLLER
 * Handles interactive behaviors for the Supervisor Web App.
 * ==========================================================================
 */

function handleSupervisorDecision(decision) {
    const feedback = document.getElementById('supervisorNotes').value;
    const statusBadge = document.getElementById('ideaDecisionBadge');

    if (decision === 'Approved') {
        statusBadge.className = 'ui-badge ui-badge-approved';
        statusBadge.innerText = 'معتمد رسمياً ✓';
        alert('تم اعتماد فكرة المشروع رسمياً وإخطار الفريق للبدء في خطة العمل!');
    } else if (decision === 'ModificationRequested') {
        statusBadge.className = 'ui-badge ui-badge-supervisor';
        statusBadge.innerText = 'مطلوب تعديلات';
        alert('تم إرسال طلب التعديل مع الملاحظات إلى قائد الفريق.');
    } else {
        statusBadge.className = 'ui-badge ui-badge-admin';
        statusBadge.innerText = 'مرفوض';
        alert('تم رفض الفكرة وإتاحة المجال للطلاب لتقديم مقترح بديل.');
    }
}

function broadcastTeamAlert() {
    alert('تم إرسال تنبيه رسمي عبر البريد الجامعي والمنصة إلى جميع أعضاء الفريق المعنيين بنجاح!');
}
