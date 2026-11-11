/**
 * ==========================================================================
 * FRONTEND - ADMIN DASHBOARD CONTROLLER
 * Handles interactive behaviors for the College Admin Dashboard.
 * ==========================================================================
 */

function switchDashboardSection(sectionId, btnElement) {
    document.querySelectorAll('.sidebar-action-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.admin-view').forEach(view => view.style.display = 'none');
    btnElement.classList.add('active');
    document.getElementById(sectionId).style.display = 'block';
}

function processSupervisorAssignment() {
    const supervisorId = document.getElementById('selectedSupervisor').value;
    const teamId = document.getElementById('selectedTeam').value;

    if (supervisorId === 'prof_hossam') {
        alert('⛔ خطأ في إسناد المشرف (Supervisor Capacity Exceeded):\n\nأ.د. حسام الشافعي وصل للحد الأقصى القانوني للإشراف (3/3 فرق).\nيرفض النظام هذا الإجراء تلقائياً لضمان عدالة توزيع الأعباء الأكاديمية.');
    } else {
        alert('✅ تم إسناد المشرف الأكاديمي بنجاح للفريق وتحديث مصفوفة الأعباء في الداش بورد!');
    }
}
