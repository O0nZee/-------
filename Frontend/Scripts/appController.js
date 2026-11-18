'use strict';

class PlatformApp {
    static async start(role) {
        const app = new PlatformApp(role);
        try { await app.init(); }
        catch (error) {
            document.getElementById('appRoot').innerHTML = `<main class="workspace"><div class="message-box error">${escapeHtml(error.message)}</div><button class="ui-btn" data-action="reload">إعادة المحاولة</button><a class="text-button" href="/">العودة للبداية</a></main>`;
            document.querySelector('[data-action="reload"]')?.addEventListener('click', () => location.reload());
        }
    }
    constructor(role) {
        this.role = role; this.section = 'overview'; this.currentTeamId = null; this.current = null; this.viewEpoch = 0;
        this.studyFilter = { departmentId: '', studyYear: '', semester: '', keyword: '' };
        this.teams = []; this.notifications = []; this.invitations = []; this.supervisors = []; this.users = []; this.archive = [];
        this.sectionInfo = {
            courses: [role === 'Student' ? 'موادّي' : 'المواد الدراسية', '📖'], summaries: ['الملخصات', '📝'], exams: ['الامتحانات السابقة', '📄'],
            overview: ['نظرة عامة', '🏠'], ideas: ['فكرة المشروع', '💡'], team: ['الفريق والإشراف', '👥'],
            tasks: ['لوحة المهام', '📋'], submissions: ['المراحل والتسليمات', '📁'], archive: ['أرشيف المشروعات', '📚'],
            notifications: ['الإشعارات', '🔔'], profile: ['حسابي', '⚙️'], users: ['المستخدمون', '👤'],
            supervisors: ['المشرفون', '👨‍🏫'], reports: ['التقارير', '📊'], operations: ['البريد والنسخ الاحتياطية', '🗂️']
        };
        this.menu = role === 'Student' ? ['overview', 'ideas', 'team', 'tasks', 'submissions', 'archive', 'notifications', 'profile']
            : role === 'Supervisor' ? ['overview', 'ideas', 'team', 'tasks', 'submissions', 'reports', 'archive', 'notifications', 'profile']
            : ['overview', 'ideas', 'team', 'tasks', 'submissions', 'supervisors', 'users', 'reports', 'archive', 'notifications', 'operations', 'profile'];
        this.menu = ['courses', 'summaries', 'exams', ...this.menu];
        this.baseMenu = [...this.menu];
    }
    async init() {
        this.user = await api.init();
        if (!this.user) { location.href = '/?session=expired'; return; }
        if (this.user.role !== this.role) { location.href = portalPath(this.user.role); return; }
        const modules = await Promise.all([import('/Frontend/Scripts/viewComponents.js'), import('/Frontend/Scripts/dialogComponents.js')]);
        this.views = modules[0].Views; this.dialogs = modules[1].Dialogs;
        this.studyCatalog = await api.request('/study/catalog');
        await this.refreshData(); this.renderShell(); this.bindEvents();
        await this.showSection(location.hash.slice(1) || (this.role === 'Student' ? 'courses' : 'overview'));
        this.pollTimer = setInterval(async () => {
            if (document.visibilityState !== 'visible') return;
            try { this.notifications = await api.request('/notifications'); this.updateNotificationBadge(); }
            catch (error) { if (error.status !== 401) console.warn('Notification refresh unavailable'); }
        }, 30000);
        document.addEventListener('session-expired', () => { clearInterval(this.pollTimer); location.href = '/?session=expired'; });
    }
    async refreshData() {
        const values = await Promise.all([api.request('/dashboard'), api.request('/settings'), api.request('/invitations'), api.request('/supervisors'), api.request('/notifications'), api.request('/profile')]);
        [this.dashboard, this.settings, this.invitations, this.supervisors, this.notifications, this.user] = values;
        this.menu = this.baseMenu.filter(section => this.role !== 'Supervisor' ||
            (section === 'reports' ? this.hasPermission('report.view') :
             ['ideas', 'team', 'tasks', 'submissions'].includes(section) ? this.hasPermission('team.view') : true));
        document.querySelectorAll('.nav-item').forEach(button => { button.hidden = !this.menu.includes(button.dataset.section); });
        this.teams = this.dashboard.teams;
        if (!this.teams.some(t => t.id === this.currentTeamId)) this.currentTeamId = this.teams[0]?.id || null;
        this.current = this.currentTeamId ? await api.request(`/teams/${encodeURIComponent(this.currentTeamId)}`) : null;
        this.updatedAt = new Date().toISOString();
        if (document.getElementById('teamToolbar')) this.renderToolbar();
        this.updateNotificationBadge();
    }
    renderShell() {
        document.getElementById('appRoot').innerHTML = `
            <header class="app-header"><div class="header-start"><a href="${portalPath(this.role)}" class="brand"><span class="brand-mark" aria-hidden="true">🎓</span><span>منصة مشروعات التخرج<small>${escapeHtml(roleLabels[this.role])}</small></span></a></div>
            <div class="header-actions"><div class="user-identity"><span class="user-avatar" aria-hidden="true">${escapeHtml(this.user.profile.fullName.slice(0, 2))}</span><span class="user-name" id="headerUserName">${escapeHtml(this.user.profile.fullName)}<small>${escapeHtml(this.user.username)}</small></span></div><button type="button" class="ui-btn ui-btn-outline ui-btn-sm" data-action="logout">خروج</button></div></header>
            <nav class="top-navigation" aria-label="القائمة الرئيسية">${this.baseMenu.map(key => `<button type="button" ${this.menu.includes(key) ? '' : 'hidden'} class="nav-item" data-action="section" data-section="${key}"><span aria-hidden="true">${this.sectionInfo[key][1]}</span><span>${this.sectionInfo[key][0]}</span>${key === 'notifications' ? '<span class="nav-badge" id="notificationCount" hidden></span>' : ''}</button>`).join('')}</nav><div class="workspace-layout">
            <main class="workspace"><div class="page-heading"><div><h1 id="pageTitle"></h1><p class="muted" id="pageSubtitle"></p></div><button class="ui-btn ui-btn-outline ui-btn-sm no-print" type="button" data-action="refresh">↻ تحديث البيانات</button></div><div id="teamToolbar"></div><div id="content" class="content-stack"></div></main></div>`;
        this.renderToolbar(); this.updateNotificationBadge();
    }
    renderToolbar() {
        const toolbar = document.getElementById('teamToolbar'); if (!toolbar) return;
        toolbar.hidden = ['courses', 'summaries', 'exams'].includes(this.section);
        toolbar.innerHTML = this.teams.length ? `<div class="toolbar"><label class="team-picker">الفريق الحالي<select id="teamSelect" class="ui-input" aria-label="الفريق الحالي">${this.teams.map(t => `<option value="${escapeHtml(t.id)}" ${t.id === this.currentTeamId ? 'selected' : ''}>${escapeHtml(t.name)}${t.isArchived ? ' — مؤرشف' : ''}</option>`).join('')}</select></label><span class="muted">${this.current?.team.isArchived ? 'المشروع مكتمل ومؤرشف' : this.current?.team.supervisor ? 'المشرف: ' + escapeHtml(this.current.team.supervisor.name) : 'بانتظار إسناد مشرف'}</span></div>` : '';
        const updated = document.getElementById('updatedAt'); if (updated) updated.textContent = formatDate(this.updatedAt);
    }
    updateNotificationBadge() {
        const badge = document.getElementById('notificationCount'); if (!badge) return;
        const count = this.notifications.filter(n => !n.readAt).length; badge.textContent = formatNumber(count); badge.hidden = count === 0;
    }
    async loadSectionData(section) {
        if (['courses', 'summaries', 'exams'].includes(section)) {
            const query = new URLSearchParams();
            if (this.role !== 'Student') for (const key of ['departmentId', 'studyYear']) if (this.studyFilter[key]) query.set(key, this.studyFilter[key]);
            this.library = await api.request('/study/library?' + query);
        }
        else if (section === 'users') this.users = await api.request('/admin/users');
        else if (section === 'archive') this.archive = await api.request('/archive');
        else if (section === 'reports') this.reports = await api.request('/reports');
        else if (section === 'supervisors') this.permissionCatalog = await api.request('/admin/supervisor-permissions');
        else if (section === 'operations') [this.mail, this.audit, this.backups] = await Promise.all([api.request('/admin/mail'), api.request('/admin/audit'), api.request('/admin/backups')]);
    }
    async showSection(section, reload = true) {
        if (!this.menu.includes(section)) section = 'overview';
        this.section = section; const epoch = ++this.viewEpoch;
        history.replaceState({}, '', portalPath(this.role) + '#' + section);
        document.body.classList.remove('nav-open');
        document.querySelector('.menu-toggle')?.setAttribute('aria-expanded', 'false');
        document.querySelectorAll('.nav-item').forEach(button => { const active = button.dataset.section === section; button.classList.toggle('active', active); button.setAttribute('aria-current', active ? 'page' : 'false'); });
        document.getElementById('pageTitle').textContent = this.sectionInfo[section][0];
        const academic = ['courses', 'summaries', 'exams'].includes(section);
        document.getElementById('pageSubtitle').textContent = academic ? this.role === 'Student'
            ? `${this.user.profile.department || 'اختَر قسمك'} · ${studyYearLabel(this.user.profile.studyYear)}`
            : 'المواد والملخصات والامتحانات لكل قسم وفرقة.'
            : section === 'overview' ? `أهلًا ${this.user.profile.fullName}، تابع آخر مستجدات مشروع التخرج.` : 'بيانات حسابك وفريقك، محدثة من المنصة.';
        const content = document.getElementById('content'); content.innerHTML = '<div class="section-loader">جاري تحميل البيانات…</div>';
        try {
            if (reload) {
                await this.refreshData();
                if (!this.menu.includes(section)) { await this.showSection('overview', false); return; }
                await this.loadSectionData(section);
            }
            if (epoch !== this.viewEpoch) return;
            content.innerHTML = this.views[section](this);
        } catch (error) { if (epoch === this.viewEpoch) content.innerHTML = `<div class="message-box error">${escapeHtml(error.message)}</div><button class="ui-btn" data-action="refresh">إعادة المحاولة</button>`; }
    }
    get team() { return this.current?.team || null; }
    get canLead() { return !!this.team && !this.team.isArchived && (this.role === 'Admin' || this.team.leaderStudentId === this.user.id); }
    hasPermission(permission) { return this.role === 'Admin' || this.role === 'Supervisor' && this.user?.permissions?.includes('team.view') && this.user.permissions.includes(permission); }
    canSupervise(permission) { return !!this.team && !this.team.isArchived && this.hasPermission(permission); }
    get canReview() { return this.canSupervise('idea.review'); }
    get canReviewSubmissions() { return this.canSupervise('submission.review'); }
    get canRemind() { return this.canSupervise('team.reminder'); }
    get canArchive() { return this.canSupervise('project.archive'); }
    get canDownload() { return this.role !== 'Supervisor' || this.hasPermission('submission.download'); }
    get canWork() { return !!this.team && !this.team.isArchived && !!this.team.idea && this.team.idea.status === 'Approved' && this.role !== 'Supervisor'; }
    canChangeTask(task) { return this.canWork && (this.role === 'Admin' || this.team.leaderStudentId === this.user.id || task.assignments.some(a => a.assignedStudentId === this.user.id)); }
    phaseTitle(milestone) { return ({ 1: 'مقترح المشروع ووثيقة المتطلبات', 2: 'التصميم المعماري وقاعدة البيانات', 3: 'التنفيذ والدمج', 4: 'الاختبار والتسليم والمناقشة' }[milestone.sequenceOrder] || milestone.title); }
    empty(message, details = '') { return `<div class="empty-state"><strong>${escapeHtml(message)}</strong><p>${escapeHtml(details)}</p></div>`; }
    toast(message, error = false) {
        const toast = document.getElementById('toast'); clearTimeout(this.toastTimer); toast.textContent = message; toast.className = error ? 'error' : ''; toast.hidden = false;
        this.toastTimer = setTimeout(() => toast.hidden = true, error ? 7000 : 4500);
    }
    openDialog(name, context = {}) {
        const model = this.dialogs[name](this, context), dialog = document.getElementById('actionDialog');
        dialog.innerHTML = `<div class="dialog-heading"><h2 id="dialogTitle">${escapeHtml(model.title)}</h2><button type="button" class="icon-button" data-action="close-dialog" aria-label="إغلاق">×</button></div><div id="dialogMessage" class="message-box error" role="alert" hidden></div>${model.html}`;
        dialog.showModal();
        this.updateUserStudyFields(dialog);
    }
    updateUserStudyFields(container) {
        const form = container.querySelector('[data-form="user-create"], [data-form="user-edit"]');
        if (!form) return;
        const student = form.elements.role.value === 'Student';
        form.querySelectorAll('[data-student-fields], [data-staff-fields]').forEach(group => {
            const enabled = group.hasAttribute('data-student-fields') ? student : !student;
            group.hidden = !enabled;
            group.querySelectorAll('input, select').forEach(field => { field.disabled = !enabled; });
        });
    }
    async afterMutation(message) {
        document.getElementById('actionDialog').close(); const position = scrollY;
        await this.refreshData(); await this.loadSectionData(this.section); await this.showSection(this.section, false);
        scrollTo(0, position); this.toast(message);
    }
    bindEvents() {
        document.addEventListener('click', event => this.handleClick(event));
        document.addEventListener('submit', event => this.handleSubmit(event));
        document.addEventListener('change', async event => {
            if (event.target.name === 'role') this.updateUserStudyFields(document.getElementById('actionDialog'));
            if (event.target.name === 'kind' && event.target.form?.dataset.form === 'study-resource') {
                const group = event.target.form.querySelector('[data-exam-year]'); if (group) group.hidden = event.target.value !== 'Exam';
            }
            if (event.target.id === 'teamSelect') { this.currentTeamId = event.target.value; await this.showSection(this.section); }
            if (event.target.name === 'decision') {
                const form = event.target.form; const feedback = form?.querySelector('[name="feedback"]'); if (feedback) feedback.required = event.target.value !== 'Approved';
                const grade = form?.querySelector('[name="grade"]'); if (grade) grade.required = event.target.value === 'Approved';
            }
        });
        let checkTimer;
        document.addEventListener('input', event => {
            const form = event.target.closest('[data-form="idea"]'); if (!form) return;
            clearTimeout(checkTimer);
            if (form.elements.title.value.trim().length >= 3 && form.elements.description.value.trim().length >= 20) checkTimer = setTimeout(() => this.checkSimilarity(form), 700);
        });
        document.addEventListener('dragstart', event => {
            const card = event.target.closest('[data-task-id]'); if (!card || card.getAttribute('draggable') !== 'true') return;
            event.dataTransfer.setData('text/plain', card.dataset.taskId); event.dataTransfer.effectAllowed = 'move';
        });
        document.addEventListener('dragover', event => { const column = event.target.closest('[data-drop-status]'); if (column) { event.preventDefault(); column.classList.add('drag-over'); } });
        document.addEventListener('dragleave', event => event.target.closest('[data-drop-status]')?.classList.remove('drag-over'));
        document.addEventListener('drop', async event => {
            const column = event.target.closest('[data-drop-status]'); if (!column) return; event.preventDefault(); column.classList.remove('drag-over');
            const id = event.dataTransfer.getData('text/plain'), task = this.current.tasks.find(t => t.id === id);
            if (!task || !this.canChangeTask(task) || task.status === column.dataset.dropStatus) return;
            try { await api.request(`/tasks/${id}/status`, { method: 'POST', body: { status: column.dataset.dropStatus } }); await this.afterMutation('تم تحديث حالة المهمة'); }
            catch (error) { this.toast(error.message, true); }
        });
    }
    async handleClick(event) {
        const button = event.target.closest('[data-action]'); if (!button || button.disabled) return;
        const action = button.dataset.action; event.preventDefault();
        try {
            if (action === 'section') { await this.showSection(button.dataset.section); return; }
            if (action === 'close-dialog') { document.getElementById('actionDialog').close(); return; }
            if (action === 'toggle-menu') { const open = document.body.classList.toggle('nav-open'); button.setAttribute('aria-expanded', String(open)); return; }
            if (action === 'close-menu') { document.body.classList.remove('nav-open'); document.querySelector('.menu-toggle')?.setAttribute('aria-expanded', 'false'); return; }
            if (action === 'logout') { await api.logout(); location.href = '/'; return; }
            if (action === 'refresh') { await this.showSection(this.section); return; }
            if (action === 'study-course-create' || action === 'study-course-edit') { this.openDialog('studyCourse', { course: this.library.courses.find(c => c.id === button.dataset.id) }); return; }
            if (action === 'study-resource-add') { this.openDialog('studyResource', { course: this.library.courses.find(c => c.id === button.dataset.id), kind: button.dataset.kind }); return; }
            if (action === 'team-select') { this.currentTeamId = button.dataset.id; await this.showSection('team'); return; }
            if (action === 'new-task' || action === 'edit-task') { this.openDialog('task', { task: this.current.tasks.find(t => t.id === button.dataset.id) }); return; }
            if (action === 'idea-review') { this.openDialog('ideaReview', { idea: this.current.ideas.find(i => i.id === button.dataset.id), decision: button.dataset.decision }); return; }
            if (action === 'submission-review') { this.openDialog('submissionReview', { submission: this.current.submissions.find(s => s.id === button.dataset.id) }); return; }
            if (action === 'upload') { this.openDialog('upload', { milestoneId: button.dataset.id }); return; }
            if (action === 'create-user') { this.openDialog('user', {}); return; }
            if (action === 'edit-user') { this.openDialog('user', { user: this.users.find(u => u.id === button.dataset.id) }); return; }
            if (action === 'edit-supervisor') { this.openDialog('supervisor', { supervisor: this.supervisors.find(s => s.id === button.dataset.id) }); return; }
            if (action === 'supervisor-permissions' && this.role === 'Admin') {
                const supervisor = this.supervisors.find(s => s.id === button.dataset.id);
                const [catalog, access] = await Promise.all([api.request('/admin/supervisor-permissions'), api.request(`/admin/supervisors/${supervisor.userId}/permissions`)]);
                this.openDialog('supervisorPermissions', { supervisor, catalog, access }); return;
            }
            if (action === 'reminder') { this.openDialog('reminder'); return; }
            if (action === 'archive-final') { this.openDialog('archiveFinal'); return; }
            if (action === 'archive-history') { this.openDialog('archiveHistory'); return; }
            if (action === 'similarity') { await this.checkSimilarity(button.closest('form')); return; }
            if (action === 'print-report') { window.print(); return; }
            if (action === 'view-mail') { this.openDialog('mail', { message: this.mail.messages.find(m => m.id === button.dataset.id) }); return; }
            button.disabled = true;
            if (action === 'study-course-delete') {
                if (!confirm('حذف المادة وكل ملخصاتها وامتحاناتها؟')) return;
                await api.request(`/admin/study/courses/${encodeURIComponent(button.dataset.id)}`, { method: 'DELETE' });
            } else if (action === 'study-resource-delete') {
                if (!confirm('حذف الملف من المادة؟')) return;
                await api.request(`/admin/study/resources/${encodeURIComponent(button.dataset.id)}`, { method: 'DELETE' });
            } else if (action === 'task-status') await api.request(`/tasks/${button.dataset.id}/status`, { method: 'POST', body: { status: button.dataset.status } });
            else if (action === 'delete-task') {
                if (!confirm('حذف المهمة من خطة الفريق؟')) return;
                await api.request(`/tasks/${button.dataset.id}`, { method: 'DELETE' });
            } else if (action === 'remove-member') {
                if (!confirm('إنهاء عضوية هذا الطالب وإلغاء تكليفه بمهام الفريق؟')) return;
                await api.request(`/teams/${this.currentTeamId}/members/${button.dataset.id}`, { method: 'DELETE' });
            } else if (action === 'respond-invitation') await api.request(`/invitations/${button.dataset.id}/respond`, { method: 'POST', body: { accept: button.dataset.accept === 'true' } });
            else if (action === 'cancel-invitation') await api.request(`/invitations/${button.dataset.id}/cancel`, { method: 'POST' });
            else if (action === 'unassign-supervisor') {
                if (!confirm('إنهاء إسناد المشرف الحالي للفريق؟')) return;
                await api.request(`/teams/${this.currentTeamId}/supervisor`, { method: 'DELETE' });
            } else if (action === 'assign-supervisor') await api.request(`/teams/${this.currentTeamId}/supervisor`, { method: 'POST', body: { supervisorId: button.dataset.id } });
            else if (action === 'read-notification') await api.request(`/notifications/${button.dataset.id}/read`, { method: 'POST' });
            else if (action === 'read-all') await api.request('/notifications/read-all', { method: 'POST' });
            else if (action === 'retry-mail') await api.request(`/admin/mail/${button.dataset.id}/retry`, { method: 'POST' });
            else if (action === 'maintenance') await api.request('/admin/maintenance', { method: 'POST' });
            else if (action === 'backup') {
                const backup = await api.request('/admin/backups', { method: 'POST' });
                await this.afterMutation('تم إنشاء النسخة الاحتياطية؛ يمكنك تنزيلها من القائمة'); return;
            } else return;
            await this.afterMutation('تم حفظ العملية بنجاح');
        } catch (error) { this.toast(error.message, true); }
        finally { if (button.isConnected) button.disabled = false; }
    }
    async handleSubmit(event) {
        const form = event.target.closest('[data-form]'); if (!form) return; event.preventDefault();
        const values = formValues(form), submit = form.querySelector('[type="submit"]'), label = submit?.textContent;
        if (submit) { submit.disabled = true; submit.textContent = 'جاري الحفظ…'; }
        form.setAttribute('aria-busy', 'true'); const dialogMessage = document.getElementById('dialogMessage'); if (dialogMessage) dialogMessage.hidden = true;
        try {
            let message = 'تم حفظ التغييرات';
            const kind = form.dataset.form;
            if (kind === 'study-filter') {
                this.studyFilter = { ...this.studyFilter, ...values }; await this.showSection(this.section); return;
            } else if (kind === 'study-course') {
                await api.request(form.dataset.id ? `/admin/study/courses/${encodeURIComponent(form.dataset.id)}` : '/admin/study/courses', {
                    method: form.dataset.id ? 'PUT' : 'POST', body: { ...values, studyYear: Number(values.studyYear), semester: Number(values.semester) }
                }); message = 'تم حفظ المادة';
            } else if (kind === 'study-resource') {
                await api.request('/admin/study/resources', { method: 'POST', body: new FormData(form) }); message = 'تم إضافة الملف للمادة';
            } else if (kind === 'create-team') { const team = await api.request('/teams', { method: 'POST', body: { name: values.name, maxMembers: Number(values.maxMembers) } }); this.currentTeamId = team.id; message = 'تم إنشاء الفريق ومراحل المشروع'; }
            else if (kind === 'team-edit') await api.request(`/teams/${this.currentTeamId}`, { method: 'PATCH', body: values });
            else if (kind === 'invite') { await api.request(`/teams/${this.currentTeamId}/invitations`, { method: 'POST', body: values }); message = 'تم إرسال الدعوة'; }
            else if (kind === 'supervisor-assign') await api.request(`/teams/${this.currentTeamId}/supervisor`, { method: 'POST', body: values });
            else if (kind === 'idea') {
                const body = { ...values, teamId: this.currentTeamId, technologies: values.technologies.split(/[,،]/).map(t => t.trim()).filter(Boolean) };
                await api.request(form.dataset.id ? `/ideas/${form.dataset.id}` : '/ideas', { method: form.dataset.id ? 'PUT' : 'POST', body }); message = 'تم تقديم الفكرة للمراجعة';
            } else if (kind === 'idea-review') { await api.request(`/ideas/${form.dataset.id}/review`, { method: 'POST', body: values }); message = 'تم حفظ قرار المراجعة وإخطار الفريق'; }
            else if (kind === 'task') {
                const body = { ...values, teamId: this.currentTeamId, startDate: dateInputToIso(values.startDate), dueDate: dateInputToIso(values.dueDate), assignedStudentIds: form.dataset.assignments === 'editable' ? new FormData(form).getAll('assignedStudentIds') : null };
                await api.request(form.dataset.id ? `/tasks/${form.dataset.id}` : '/tasks', { method: form.dataset.id ? 'PUT' : 'POST', body }); message = 'تم حفظ المهمة وتكليف الطلاب';
            } else if (kind === 'upload') { const body = new FormData(form); body.set('teamId', this.currentTeamId); await api.request('/submissions', { method: 'POST', body }); message = 'تم رفع الملف وحفظه للمراجعة'; }
            else if (kind === 'submission-review') { await api.request(`/submissions/${form.dataset.id}/review`, { method: 'POST', body: { ...values, grade: values.grade === '' ? null : Number(values.grade) } }); message = 'تم حفظ مراجعة التسليم'; }
            else if (kind === 'user-create') { await api.request('/admin/users', { method: 'POST', body: { ...values, studyYear: values.studyYear ? Number(values.studyYear) : null } }); message = 'تم إنشاء الحساب'; }
            else if (kind === 'user-edit') { await api.request(`/admin/users/${form.dataset.id}`, { method: 'PATCH', body: { ...values, studyYear: values.studyYear ? Number(values.studyYear) : null, role: form.elements.role.value, isActive: form.elements.isActive.checked } }); message = 'تم تحديث الحساب'; }
            else if (kind === 'supervisor-edit') await api.request(`/supervisors/${form.dataset.id}`, { method: 'PATCH', body: { maxTeams: Number(values.maxTeams), specialization: values.specialization } });
            else if (kind === 'supervisor-permissions' && this.role === 'Admin') {
                await api.request(`/admin/supervisors/${form.dataset.id}/permissions`, { method: 'PUT', body: { permissionIds: new FormData(form).getAll('permissionIds') } });
                message = 'تم حفظ صلاحيات المشرف؛ تسري على طلباته فورًا';
            }
            else if (kind === 'reminder') { await api.request(`/teams/${this.currentTeamId}/reminder`, { method: 'POST', body: values }); message = 'تم إخطار أعضاء الفريق'; }
            else if (kind === 'archive-final') { await api.request(`/teams/${this.currentTeamId}/archive`, { method: 'POST', body: { grade: Number(values.grade), academicYear: values.academicYear } }); message = 'تم تقييم المشروع وأرشفته'; }
            else if (kind === 'archive-history') await api.request('/archive', { method: 'POST', body: { ...values, technologies: values.technologies.split(/[,،]/).map(t => t.trim()).filter(Boolean) } });
            else if (kind === 'archive-search') {
                this.archive = await api.request('/archive?' + new URLSearchParams(values));
                document.getElementById('archiveResults').innerHTML = this.views.archiveCards(this); return;
            } else if (kind === 'users-search') {
                this.userFilter = values.keyword.toLowerCase(); document.getElementById('usersTable').innerHTML = this.views.usersTable(this); return;
            } else if (kind === 'profile') {
                await api.request('/profile', { method: 'PATCH', body: { ...values, studyYear: values.studyYear ? Number(values.studyYear) : null } }); this.user = await api.init();
                document.getElementById('headerUserName').innerHTML = `${escapeHtml(this.user.profile.fullName)}<small>${escapeHtml(this.user.username)}</small>`;
                document.querySelector('.user-avatar').textContent = this.user.profile.fullName.slice(0, 2);
            } else if (kind === 'password') {
                if (values.newPassword !== values.confirmPassword) throw new ApiError('كلمتا المرور غير متطابقتين.');
                await api.request('/profile/password', { method: 'POST', body: values }); location.href = '/?changed=1'; return;
            } else return;
            await this.afterMutation(message);
        } catch (error) {
            if (dialogMessage && document.getElementById('actionDialog').open) { dialogMessage.textContent = error.message; dialogMessage.hidden = false; }
            else this.toast(error.message, true);
        } finally { form.removeAttribute('aria-busy'); if (submit?.isConnected) { submit.disabled = false; submit.textContent = label; } }
    }
    async checkSimilarity(form) {
        const output = document.getElementById('similarityOutput'); if (!output || !form?.isConnected) return;
        const values = formValues(form), epoch = this.similarityEpoch = (this.similarityEpoch || 0) + 1;
        output.innerHTML = '<p class="muted">جاري مقارنة الفكرة بالمشروعات الحالية والأرشيف…</p>';
        try {
            const matches = await api.request('/ideas/similarity', { method: 'POST', body: { title: values.title, description: values.description, technologies: values.technologies.split(/[,،]/).map(t => t.trim()).filter(Boolean), excludeIdeaId: form.dataset.id || null } });
            if (epoch === this.similarityEpoch && output.isConnected) output.innerHTML = this.views.similarity(matches);
        } catch (error) { if (epoch === this.similarityEpoch && output.isConnected) output.innerHTML = `<div class="message-box error">${escapeHtml(error.message)}</div>`; }
    }
}
