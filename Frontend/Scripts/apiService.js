'use strict';

class ApiError extends Error {
    constructor(message, status = 0, code = '') { super(message); this.status = status; this.code = code; }
}

class ApiService {
    constructor() { this.baseUrl = '/api'; this.user = null; this.csrfToken = null; }
    async init() {
        const session = await this.request('/auth/session');
        this.user = session.user; this.csrfToken = session.csrfToken;
        return this.user;
    }
    async request(path, { method = 'GET', body, signal } = {}) {
        const headers = { 'Accept': 'application/json' };
        if (method !== 'GET') {
            if (!this.csrfToken) await this.init();
            headers['X-CSRF-TOKEN'] = this.csrfToken;
        }
        let payload = body;
        if (body !== undefined && !(body instanceof FormData)) {
            headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body);
        }
        let response;
        try { response = await fetch(this.baseUrl + path, { method, headers, body: payload, credentials: 'same-origin', signal }); }
        catch (error) {
            if (error.name === 'AbortError') throw error;
            throw new ApiError('تعذّر الاتصال بالمنصة. تأكد من تشغيلها ثم حاول مجددًا.');
        }
        let result;
        try { result = await response.json(); }
        catch { throw new ApiError('تعذّر قراءة استجابة المنصة؛ أعد المحاولة.', response.status); }
        if (!response.ok || !result.isSuccess) {
            if (response.status === 401 && !path.startsWith('/auth/')) document.dispatchEvent(new CustomEvent('session-expired'));
            throw new ApiError(result.message || 'تعذّر تنفيذ العملية.', response.status, result.errorCode);
        }
        return result.data;
    }
    async login(usernameOrEmail, password) {
        await this.request('/auth/login', { method: 'POST', body: { usernameOrEmail, password } });
        return this.init();
    }
    async register(values) {
        await this.request('/auth/register', { method: 'POST', body: values });
        return this.init();
    }
    async logout() { await this.request('/auth/logout', { method: 'POST' }); this.user = null; this.csrfToken = null; }
}

const api = new ApiService();
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const roleLabels = { Student: 'طالب', Supervisor: 'مشرف أكاديمي', Admin: 'إدارة الكلية' };
const portalPath = role => ({ Student: '/student', Supervisor: '/supervisor', Admin: '/admin' }[role] || '/');
const formatDate = value => value ? new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Africa/Cairo' }).format(new Date(value)) : '—';
const formatNumber = value => new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 1 }).format(Number(value) || 0);
const formValues = form => Object.fromEntries(new FormData(form).entries());
const cairoParts = date => Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(date).filter(part => part.type !== 'literal').map(part => [part.type, part.value]));
const dateInputValue = value => { const p = cairoParts(new Date(value)); return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`; };
const dateInputToIso = value => {
    if (!value) return null;
    const [date, time] = value.split('T'), [year, month, day] = date.split('-').map(Number), [hour, minute] = time.split(':').map(Number);
    const wall = Date.UTC(year, month - 1, day, hour, minute); let instant = wall;
    for (let i = 0; i < 3; i++) { const p = cairoParts(new Date(instant)); const represented = Date.UTC(Number(p.year), Number(p.month) - 1, Number(p.day), Number(p.hour), Number(p.minute), Number(p.second)); instant = wall - (represented - instant); }
    const result = new Date(instant);
    if (dateInputValue(result.toISOString()) !== value) throw new ApiError('موعد المهمة غير صالح؛ اختر توقيتًا آخر.');
    return result.toISOString();
};
