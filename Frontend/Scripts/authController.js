'use strict';

const authMessage = document.getElementById('authMessage');
const query = new URLSearchParams(location.search);
const openedFromFile = location.protocol === 'file:';
let resetToken = query.get('reset');
if (resetToken && !openedFromFile) history.replaceState({}, '', '/');

function showAuthMessage(message, error = false) {
    authMessage.textContent = message; authMessage.className = `message-box ${error ? 'error' : 'success'}`; authMessage.hidden = false;
}
function selectAuthTab(name) {
    document.querySelectorAll('[data-auth-form]').forEach(form => form.hidden = form.dataset.authForm !== name);
    document.getElementById('signedInPanel').hidden = true;
    document.getElementById('authForms').hidden = false;
    document.querySelectorAll('.auth-tabs [data-auth-tab]').forEach(button => {
        const active = button.dataset.authTab === name; button.classList.toggle('active', active); button.setAttribute('aria-selected', String(active));
    });
    document.querySelector(`[data-auth-form="${name}"] input`)?.focus();
}
document.addEventListener('click', async event => {
    const tab = event.target.closest('[data-auth-tab]');
    if (tab) { authMessage.hidden = true; selectAuthTab(tab.dataset.authTab); }
    if (event.target.closest('#switchAccount')) {
        try { await api.logout(); location.href = '/'; } catch (error) { showAuthMessage(error.message, true); }
    }
});
document.addEventListener('submit', async event => {
    const form = event.target.closest('[data-auth-form]'); if (!form) return;
    event.preventDefault(); const values = formValues(form), button = form.querySelector('button[type="submit"]');
    const label = button.textContent; button.disabled = true; button.textContent = 'جاري التنفيذ…'; authMessage.hidden = true;
    try {
        if (form.dataset.authForm === 'login') { const user = await api.login(values.usernameOrEmail.trim(), values.password); location.replace(portalPath(user.role)); }
        else if (form.dataset.authForm === 'register') {
            if (values.password !== values.confirmPassword) throw new ApiError('كلمتا المرور غير متطابقتين.');
            delete values.confirmPassword; values.studyYear = Number(values.studyYear);
            const user = await api.register(values); location.href = portalPath(user.role);
        } else if (form.dataset.authForm === 'forgot') {
            await api.request('/auth/forgot-password', { method: 'POST', body: { email: values.email } });
            showAuthMessage('إذا كان البريد مسجلًا ونشطًا، سيتم إرسال رابط استعادة كلمة المرور.');
        } else if (form.dataset.authForm === 'reset') {
            if (values.newPassword !== values.confirmPassword) throw new ApiError('كلمتا المرور غير متطابقتين.');
            await api.request('/auth/reset-password', { method: 'POST', body: { token: resetToken, newPassword: values.newPassword } });
            resetToken = null; await api.init(); await api.logout(); selectAuthTab('login'); showAuthMessage('تم تغيير كلمة المرور. سجّل الدخول بكلمة المرور الجديدة.');
        }
    } catch (error) { showAuthMessage(error.message, true); }
    finally { button.disabled = false; button.textContent = label; }
});
(async () => {
    if (openedFromFile) {
        document.getElementById('authForms').hidden = true;
        document.getElementById('localEntryPanel').hidden = false;
        return;
    }
    try {
        const catalog = await api.request('/study/catalog');
        const department = document.getElementById('registerDepartment'), year = document.getElementById('registerStudyYear');
        if (department) department.innerHTML = studyDepartmentOptions(catalog, department.value, false);
        if (year) year.innerHTML = studyYearOptions(catalog, year.value);
    } catch (error) {
        // The rendered registration options remain usable if the catalog refresh fails.
        console.warn('Registration catalog refresh unavailable');
    }
    try {
        const user = await api.init();
        if (resetToken) { document.getElementById('authForms').hidden = true; document.getElementById('resetForm').hidden = false; return; }
        if (user) {
            if (document.body.dataset.loginFirst === 'true') { location.replace(portalPath(user.role)); return; }
            document.getElementById('authForms').hidden = true; document.getElementById('signedInPanel').hidden = false;
            document.getElementById('signedInName').textContent = user.profile.fullName;
            document.getElementById('continueLink').href = portalPath(user.role);
        }
        if (query.has('session')) showAuthMessage('انتهت الجلسة؛ سجّل الدخول مجددًا.', true);
        else if (query.has('changed')) showAuthMessage('تم تغيير كلمة المرور؛ سجّل الدخول مجددًا.');
        else if (query.has('denied')) showAuthMessage('هذه البوابة متاحة لدور آخر. انتقل إلى بوابتك من حسابك.', true);
    } catch (error) { showAuthMessage(error.message, true); }
})();
