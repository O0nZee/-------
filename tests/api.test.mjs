import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import net from 'node:net';
import { randomUUID } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const localSdk = path.join(root, '.tools', 'dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
const sdk = process.env.GRADUATION_DOTNET || (await fs.access(localSdk).then(() => localSdk, () => 'dotnet'));
const app = process.env.GRADUATION_APP_DLL || path.join(root, 'bin', 'Release', 'net8.0', 'GraduationPlatform.dll');
const run = randomUUID().replaceAll('-', '').slice(0, 10);
const data = path.join('tests', '.runtime', 'api-' + run);
const reservation = net.createServer(); reservation.listen(0, '127.0.0.1'); await once(reservation, 'listening');
const port = reservation.address().port; await new Promise(resolve => reservation.close(resolve));
const base = `http://localhost:${port}`;
const adminPassword = 'TestAdministrator!123';
let child, logs = '', passed = 0;
async function start(directory = data) {
    child = spawn(sdk, [app, '--contentRoot', root, '--environment', 'Development', '--urls', base, '--Platform:PublicUrl', base, '--Platform:DataDirectory', directory, '--Platform:DemoData', 'false', '--Bootstrap:AdminPassword', adminPassword], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.on('data', chunk => logs += chunk); child.stderr.on('data', chunk => logs += chunk);
    for (let i = 0; i < 100; i++) {
        if (child.exitCode !== null) throw new Error('Server exited before readiness:\n' + logs.slice(-4000));
        try { if ((await fetch(base + '/api/health')).ok) return; } catch { }
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error('Server did not become ready:\n' + logs.slice(-4000));
}
async function stop() {
    if (!child || child.exitCode !== null) return;
    const exited = once(child, 'exit'); child.kill(); await exited;
}
class Client {
    constructor() { this.cookies = new Map(); this.csrf = null; this.user = null; }
    async request(method, route, body, expected = 200, raw = false, csrf = true) {
        const headers = { Cookie: [...this.cookies].map(([key, value]) => key + '=' + value).join('; ') };
        if (method !== 'GET' && csrf && this.csrf) headers['X-CSRF-TOKEN'] = this.csrf;
        if (body !== undefined && !(body instanceof FormData)) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(body); }
        const response = await fetch(base + '/api' + route, { method, headers, body, redirect: 'manual' });
        for (const cookie of response.headers.getSetCookie()) {
            const [key, ...value] = cookie.split(';')[0].split('=');
            if (!value.join('=')) this.cookies.delete(key); else this.cookies.set(key, value.join('='));
        }
        if (raw) { assert.equal(response.status, expected, `${method} ${route}`); return response; }
        const result = await response.json();
        assert.equal(response.status, expected, `${method} ${route}: ${result.message || ''}`);
        if (expected < 400) assert.equal(result.isSuccess, true, `${method} ${route}`);
        return result;
    }
    async session() { const result = await this.request('GET', '/auth/session'); this.csrf = result.data.csrfToken; this.user = result.data.user; return this.user; }
    async login(username, password) { await this.session(); await this.request('POST', '/auth/login', { usernameOrEmail: username, password }); return this.session(); }
    async register(prefix, role = 'Student', department = 'حاسب آلي', studyYear = 2) {
        await this.session(); const username = prefix + '_' + run, email = `${prefix}-${run}@tests.local`;
        await this.request('POST', '/auth/register', { username, email, password: 'StudentPassword!123', fullName: prefix + ' طالب اختبار', department, studyYear, academicId: prefix + '-01', role });
        await this.session(); return this.user;
    }
}
async function test(name, action) { await action(); passed++; process.stdout.write(`PASS ${passed}: ${name}\n`); }
async function restoreBackup(destination, expected = 0) {
    const restoration = spawn(process.platform === 'win32' ? 'powershell.exe' : 'pwsh', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(root, 'scripts', 'Restore-Backup.ps1'), '-Archive', path.join(root, data, 'backups', backupName), '-Destination', destination], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = ''; restoration.stdout.on('data', chunk => output += chunk); restoration.stderr.on('data', chunk => output += chunk);
    const [code] = await once(restoration, 'exit');
    if (expected === 0) assert.equal(code, 0, output); else assert.notEqual(code, 0, 'An existing directory must not be overwritten');
}
let studyCourse, studyFiles = [], owner, member, outsider, doctorUser, secondDoctorUser, team, otherTeam, milestones, invitation, idea, task, submissions = [], backupName;
const admin = new Client(), leader = new Client(), colleague = new Client(), unrelated = new Client(), doctor = new Client(), secondDoctor = new Client(), publicClient = new Client();
try {
    await start();
    await test('Health confirms that the real Python text-analysis worker is active', async () => {
        const health = (await publicClient.request('GET', '/health')).data;
        assert.equal(health.status, 'healthy');
        assert.equal(health.similarity.configuredEngine, 'Python');
        assert.equal(health.similarity.activeEngine, 'python-tfidf');
        assert.equal(health.similarity.status, 'healthy');
        assert.equal(health.similarity.protocolVersion, 1);
    });
    await test('Anonymous API access and requests without CSRF are blocked', async () => {
        await publicClient.session(); await publicClient.request('GET', '/teams', undefined, 401);
        await publicClient.request('POST', '/auth/login', { usernameOrEmail: 'admin', password: adminPassword }, 403, false, false);
        await publicClient.request('POST', '/auth/login', { usernameOrEmail: 'admin', password: 'WrongPassword!' }, 401);
    });
    await test('Administrator login does not expose password or reset fields', async () => {
        const user = await admin.login('admin', adminPassword); assert.equal(user.role, 'Admin');
        assert(!('passwordHash' in user)); assert(!('passwordSalt' in user)); assert(!('securityStamp' in user)); assert(!('passwordResetToken' in user));
        assert.equal((await admin.request('GET', '/archive')).data.length, 0, 'A non-demo database must not contain sample archive projects');
    });
    await test('Public registration cannot choose administrator or supervisor privileges', async () => {
        owner = await leader.register('leader', 'Admin'); member = await colleague.register('member', 'Supervisor'); outsider = await unrelated.register('outsider', 'Student', 'إعلام', 2);
        assert.equal(owner.role, 'Student'); assert.equal(member.role, 'Student'); await leader.request('GET', '/admin/users', undefined, 403);
    });
    await test('Registration requires a supported department and a study year from one to four', async () => {
        const catalog = (await publicClient.request('GET', '/study/catalog')).data;
        assert.deepEqual(catalog.departments.map(d => d.name), ['حاسب آلي إنجليزي', 'حاسب آلي', 'تكنولوجيا ذوي الاحتياجات الخاصة', 'تكنولوجيا', 'مسرح', 'إعلام', 'فنية', 'موسيقى']);
        assert.deepEqual(catalog.years.map(y => y.value), [1, 2, 3, 4]);
        const values = { username: 'invalid_' + run, email: `invalid-${run}@tests.local`, password: 'StrongPassword!123', fullName: 'طالب', department: 'حاسب آلي', studyYear: 2 };
        for (const invalid of [{ studyYear: 5 }, { studyYear: null }, { department: 'قسم غير موجود' }])
            await publicClient.request('POST', '/auth/register', { ...values, ...invalid }, 400);
        assert.equal((await leader.request('GET', '/profile')).data.profile.studyYear, 2);
    });
    await test('Study courses are scoped to the saved department and year and only administrators edit them', async () => {
        const values = { name: 'قواعد البيانات', departmentId: 'computer-ar', studyYear: 2, semester: 1, description: 'مادة اختبار' };
        studyCourse = (await admin.request('POST', '/admin/study/courses', values)).data;
        await admin.request('POST', '/admin/study/courses', values, 409);
        await admin.request('POST', '/admin/study/courses', { ...values, studyYear: 5 }, 400);
        await admin.request('POST', '/admin/study/courses', { ...values, departmentId: 'media' });
        await admin.request('POST', '/admin/study/courses', { ...values, studyYear: 3 });
        await leader.request('POST', '/admin/study/courses', { ...values, name: 'ممنوعة' }, 403);
        const library = (await leader.request('GET', '/study/library?departmentId=media&studyYear=3')).data;
        assert.equal(library.department.id, 'computer-ar'); assert.equal(library.studyYear, 2);
        assert.deepEqual(library.courses.map(c => c.id), [studyCourse.id]);
        assert.equal((await unrelated.request('GET', '/study/library')).data.courses[0].departmentId, 'media');
    });
    await test('Materials, summaries and previous exams upload and download inside the correct student scope', async () => {
        for (const kind of ['Material', 'Summary', 'Exam']) {
            const form = new FormData(); form.set('courseId', studyCourse.id); form.set('kind', kind); form.set('title', 'ملف ' + kind);
            form.set('examYear', kind === 'Exam' ? '٢٠٢٥-٢٠٢٦' : '');
            form.set('file', new Blob(['Study file ' + kind], { type: 'text/plain' }), kind + '.txt');
            const item = (await admin.request('POST', '/admin/study/resources', form)).data; studyFiles.push(item);
            assert.equal(await (await leader.request('GET', item.downloadUrl.slice(4), undefined, 200, true)).text(), 'Study file ' + kind);
            await unrelated.request('GET', item.downloadUrl.slice(4), undefined, 403);
            await publicClient.request('GET', item.downloadUrl.slice(4), undefined, 401);
        }
        const library = (await leader.request('GET', '/study/library')).data;
        assert.equal(library.courses[0].resources.length, 3);
        assert.equal(library.courses[0].resources.find(r => r.kind === 'Exam').examYear, '2025-2026');
        const invalid = new FormData(); invalid.set('courseId', studyCourse.id); invalid.set('kind', 'Summary'); invalid.set('title', 'ملف مزيف');
        invalid.set('file', new Blob(['fake PDF']), 'fake.pdf');
        await admin.request('POST', '/admin/study/resources', invalid, 400);
        assert.equal((await leader.request('GET', '/study/library')).data.courses[0].resources.length, 3);
    });
    await test('Changing the profile updates the academic page and rejects invalid years', async () => {
        const profile = (await leader.request('GET', '/profile')).data.profile;
        const change = { fullName: profile.fullName, department: 'إعلام', studyYear: 2, phone: '', bio: '' };
        await leader.request('PATCH', '/profile', { ...change, studyYear: 0 }, 400);
        await leader.request('PATCH', '/profile', change);
        assert.equal((await leader.request('GET', '/study/library')).data.courses[0].departmentId, 'media');
        await leader.request('GET', studyFiles[0].downloadUrl.slice(4), undefined, 403);
        await leader.request('PATCH', '/profile', { ...change, department: 'حاسب آلي' });
    });
    await test('Course edits and resource removal persist and remain administration-only', async () => {
        const course = (await admin.request('POST', '/admin/study/courses', { name: 'مادة مؤقتة', departmentId: 'art', studyYear: 4, semester: 2 })).data;
        await leader.request('DELETE', '/admin/study/courses/' + course.id, undefined, 403);
        await admin.request('PUT', '/admin/study/courses/' + course.id, { name: 'مادة بعد التعديل', departmentId: 'art', studyYear: 4, semester: 1 });
        assert.equal((await admin.request('GET', '/study/library?departmentId=art&studyYear=4')).data.courses[0].semester, 1);
        const form = new FormData(); form.set('courseId', course.id); form.set('kind', 'Summary'); form.set('title', 'ملخص مؤقت');
        form.set('file', new Blob(['Temporary study summary']), 'temporary.txt');
        const item = (await admin.request('POST', '/admin/study/resources', form)).data;
        await admin.request('DELETE', '/admin/study/resources/' + item.id);
        await admin.request('GET', item.downloadUrl.slice(4), undefined, 404);
        await admin.request('DELETE', '/admin/study/courses/' + course.id);
        assert.equal((await admin.request('GET', '/study/library?departmentId=art&studyYear=4')).data.courses.length, 0);
    });
    await test('Administration creates supervisor accounts and their linked profiles', async () => {
        doctorUser = (await admin.request('POST', '/admin/users', { username: 'doctor_' + run, email: `doctor-${run}@tests.local`, password: 'DoctorPassword!123', fullName: 'مشرف اختبار', department: 'علوم الحاسب', role: 'Supervisor' })).data;
        secondDoctorUser = (await admin.request('POST', '/admin/users', { username: 'second_' + run, email: `second-${run}@tests.local`, password: 'DoctorPassword!123', fullName: 'مشرف آخر', department: 'علوم الحاسب', role: 'Supervisor' })).data;
        await doctor.login(doctorUser.username, 'DoctorPassword!123'); await secondDoctor.login(secondDoctorUser.username, 'DoctorPassword!123');
        assert.equal((await admin.request('GET', '/supervisors')).data.length, 2);
    });
    let allSupervisorPermissions;
    await test('Only college administration can inspect and grant supervisor permissions', async () => {
        allSupervisorPermissions = (await admin.request('GET', '/admin/supervisor-permissions')).data.map(p => p.id);
        assert.equal(allSupervisorPermissions.length, 7);
        assert.deepEqual((await doctor.request('GET', '/profile')).data.permissions, []);
        await publicClient.request('GET', '/admin/supervisor-permissions', undefined, 401);
        for (const client of [leader, doctor]) {
            await client.request('GET', '/admin/supervisor-permissions', undefined, 403);
            await client.request('GET', `/admin/supervisors/${doctorUser.id}/permissions`, undefined, 403);
            await client.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: allSupervisorPermissions }, 403);
        }
        assert(!('permissions' in (await leader.request('GET', '/profile')).data));
        for (const permissionIds of [null, ['user.manage'], ['supervisor.assign'], ['team.view', 'Admin'], ['idea.review']])
            await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds }, 400);
        await admin.request('PUT', `/admin/supervisors/${owner.id}/permissions`, { permissionIds: [] }, 400);
        for (const user of [doctorUser, secondDoctorUser])
            await admin.request('PUT', `/admin/supervisors/${user.id}/permissions`, { permissionIds: allSupervisorPermissions });
        assert.deepEqual((await doctor.request('GET', '/profile')).data.permissions, allSupervisorPermissions);
    });
    await test('Team creation initializes four milestones and prevents multiple memberships', async () => {
        team = (await leader.request('POST', '/teams', { name: 'فريق تحليل المياه', maxMembers: 2 })).data;
        otherTeam = (await unrelated.request('POST', '/teams', { name: 'فريق آخر', maxMembers: 2 })).data;
        milestones = (await leader.request('GET', '/teams/' + team.id)).data.milestones; assert.equal(milestones.length, 4);
        await leader.request('POST', '/teams', { name: 'فريق مكرر', maxMembers: 2 }, 400);
        await leader.request('POST', '/teams', { name: 'سعة غير صالحة', maxMembers: 20 }, 400);
    });
    await test('Invitation acceptance creates membership and enforces capacity', async () => {
        invitation = (await leader.request('POST', `/teams/${team.id}/invitations`, { email: member.email, proposedRole: 'تطوير البرمجيات' })).data;
        await unrelated.request('POST', `/invitations/${invitation.id}/respond`, { accept: true }, 403);
        await colleague.request('POST', `/invitations/${invitation.id}/respond`, { accept: true });
        assert.equal((await leader.request('GET', '/teams/' + team.id)).data.team.members.length, 2);
        await leader.request('POST', `/teams/${team.id}/invitations`, { email: outsider.email }, 400);
        await colleague.request('POST', `/invitations/${invitation.id}/respond`, { accept: true }, 400);
    });
    let supervisor;
    await test('Leadership transfer requires an active team member and preserves team state on failure', async () => {
        await admin.request('PATCH', '/admin/users/' + member.id, { isActive: false });
        await leader.request('PATCH', '/teams/' + team.id, { name: team.name, leaderStudentId: member.id }, 400);
        assert.equal((await leader.request('GET', '/teams/' + team.id)).data.team.leaderStudentId, owner.id);
        await admin.request('PATCH', '/admin/users/' + member.id, { isActive: true });
        await colleague.login(member.username, 'StudentPassword!123');
        await leader.request('PATCH', '/teams/' + team.id, { name: team.name, leaderStudentId: outsider.id }, 400);
        await leader.request('PATCH', '/teams/' + team.id, { name: team.name, leaderStudentId: member.id });
        await colleague.request('PATCH', '/teams/' + team.id, { name: team.name, leaderStudentId: owner.id });
    });
    await test('Only administrators assign supervisors; repeated assignments respect quota', async () => {
        supervisor = (await admin.request('GET', '/supervisors')).data.find(s => s.userId === doctorUser.id);
        await admin.request('PATCH', '/supervisors/' + supervisor.id, { maxTeams: 1, specialization: 'إنترنت الأشياء' });
        await leader.request('POST', `/teams/${team.id}/supervisor`, { supervisorId: supervisor.id }, 403);
        await admin.request('POST', `/teams/${team.id}/supervisor`, { supervisorId: supervisor.id });
        await admin.request('POST', `/teams/${team.id}/supervisor`, { supervisorId: supervisor.id });
        const current = (await admin.request('GET', '/supervisors')).data.find(s => s.id === supervisor.id); assert.equal(current.activeTeamsCount, 1);
        await admin.request('POST', `/teams/${otherTeam.id}/supervisor`, { supervisorId: supervisor.id }, 400);
    });
    await test('Revoking access blocks assigned teams immediately without a new login', async () => {
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: [] });
        assert.deepEqual((await doctor.request('GET', '/teams')).data, []);
        assert.equal((await doctor.request('GET', '/dashboard')).data.teams.length, 0);
        await doctor.request('GET', '/teams/' + team.id, undefined, 403);
        await doctor.request('GET', '/tasks?teamId=' + team.id, undefined, 403);
        await doctor.request('GET', '/reports/export', undefined, 403);
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: ['team.view'] });
        assert.equal((await doctor.request('GET', '/teams/' + team.id)).data.team.id, team.id);
        await doctor.request('GET', '/teams/' + otherTeam.id, undefined, 403);
        await doctor.request('POST', '/ideas/missing/review', { decision: 'Approved' }, 403);
        await doctor.request('POST', '/submissions/missing/review', { decision: 'Approved', grade: 95 }, 403);
        await doctor.request('GET', '/submissions/missing/download', undefined, 403);
        await doctor.request('POST', `/teams/${team.id}/reminder`, { message: 'رسالة' }, 403);
        await doctor.request('POST', `/teams/${team.id}/archive`, { grade: 90, academicYear: '2026-2027' }, 403);
        await doctor.request('GET', '/reports', undefined, 403);
        await doctor.request('GET', '/reports/export', undefined, 403);
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: allSupervisorPermissions });
        const audit = (await admin.request('GET', '/admin/audit')).data;
        const record = audit.find(a => a.action === 'supervisor.permissions.details' && a.entityId === doctorUser.id);
        assert.equal(record.actorName, admin.user.profile.fullName);
        assert.deepEqual(JSON.parse(record.details).after, allSupervisorPermissions);
    });
    const proposal = { teamId: team.id, title: 'منصة تحليل جودة المياه بالحساسات', description: 'منصة تجمع قراءات الحساسات وتحلل جودة المياه وتعرض التغيرات والتنبيهات لفريق التشغيل بشكل مستمر.', objectives: 'تحسين المتابعة', technologies: ['Python', 'IoT'], track: 'IoT' };
    await test('Idea workflow supports edits, supervisor feedback, and automatic approved linking', async () => {
        idea = (await leader.request('POST', '/ideas', proposal)).data;
        await leader.request('POST', '/tasks', { teamId: team.id, title: 'مهمة قبل الاعتماد', dueDate: new Date(Date.now() + 86400000).toISOString() }, 409);
        await secondDoctor.request('POST', `/ideas/${idea.id}/review`, { decision: 'Approved', feedback: '' }, 403);
        await doctor.request('POST', `/ideas/${idea.id}/review`, { decision: 'ModificationRequested', feedback: 'أضف اختبار دقة الحساسات' });
        await leader.request('PUT', '/ideas/' + idea.id, { ...proposal, objectives: 'اختبار دقة الحساسات وتحسين المتابعة' });
        await doctor.request('POST', `/ideas/${idea.id}/review`, { decision: 'Approved', feedback: 'تم اعتماد خطة الاختبار' });
        assert.equal((await leader.request('GET', '/teams/' + team.id)).data.team.projectIdeaId, idea.id);
        await leader.request('PUT', '/ideas/' + idea.id, proposal, 400);
    });
    await test('Similarity comparison recognizes Arabic text and shared technology terms', async () => {
        const result = await leader.request('POST', '/ideas/similarity', proposal);
        assert(result.data.some(match => match.matchedItemId === idea.id && match.similarityScore >= .99));
    });
    await test('Arabic diacritics and missing technologies preserve an identical project match', async () => {
        const result = await leader.request('POST', '/ideas/similarity', {
            ...proposal, title: 'مِنَصَّة تَحْلِيل جَوْدَة المِيَاه بِالحَسَّاسَات', technologies: []
        });
        assert(result.data.some(match => match.matchedItemId === idea.id && match.similarityScore >= .99));
    });
    await test('Excluding an idea requires access and a valid existing identifier', async () => {
        await unrelated.request('POST', '/ideas/similarity', { ...proposal, teamId: undefined, excludeIdeaId: idea.id }, 403);
        await leader.request('POST', '/ideas/similarity', { ...proposal, excludeIdeaId: 'unknown-project' }, 404);
        await leader.request('POST', '/ideas/similarity', { ...proposal, excludeIdeaId: 'x'.repeat(65) }, 400);
    });
    await test('Other teams receive useful scores without private idea metadata', async () => {
        const matches = (await unrelated.request('POST', '/ideas/similarity', { ...proposal, teamId: undefined })).data;
        const privateMatches = matches.filter(match => match.matchedSourceType === 'CurrentIdea');
        assert(privateMatches.some(match => match.similarityScore >= .99));
        for (const match of privateMatches) {
            assert.equal(match.matchedItemId, null);
            assert.equal(match.matchedTitle, 'فكرة لفريق آخر');
            assert.deepEqual(match.overlappingKeywords, []);
        }
    });
    await test('Python classifies project tracks and evaluates proposal completeness', async () => {
        const classification = (await leader.request('POST', '/ideas/classify', proposal)).data;
        assert.equal(classification.engine, 'python-ai');
        assert(classification.recommendedTracks.length >= 1 && classification.recommendedTracks.length <= 3);
        assert.equal(classification.primaryTrack, classification.recommendedTracks[0].arabicName);
        assert(classification.recommendedTracks.every(track => Number.isFinite(track.confidence) && track.confidence >= 0 && track.confidence <= 1));
        const evaluation = (await leader.request('POST', '/ideas/evaluate', proposal)).data;
        assert.equal(evaluation.engine, 'python-ai');
        assert(Number.isInteger(evaluation.score) && evaluation.score >= 0 && evaluation.score <= 100);
        assert(evaluation.grade.length > 0 && evaluation.recommendations.length > 0);
    });
    await test('Text-analysis endpoints enforce title, description, and technology limits', async () => {
        for (const route of ['/ideas/classify', '/ideas/evaluate']) {
            await publicClient.request('POST', route, proposal, 401);
            await leader.request('POST', route, { ...proposal, title: 'x'.repeat(201) }, 400);
            await leader.request('POST', route, { ...proposal, description: 'x'.repeat(10001) }, 400);
            await leader.request('POST', route, { ...proposal, technologies: [null] }, 400);
        }
    });
    await test('Unrelated students and supervisors cannot read another team', async () => {
        await unrelated.request('GET', '/teams/' + team.id, undefined, 403); await secondDoctor.request('GET', '/teams/' + team.id, undefined, 403);
        assert.equal((await secondDoctor.request('GET', '/ideas')).data.length, 0);
    });
    const taskBody = { teamId: team.id, milestoneId: milestones[0].id, title: 'اختبار الحساسات', description: 'التحقق من القياسات', priority: 'High', startDate: new Date(Date.now() - 4 * 86400000).toISOString(), dueDate: new Date(Date.now() - 86400000).toISOString(), assignedStudentIds: [member.id] };
    await test('Task assignment validates team membership and milestone ownership atomically', async () => {
        task = (await leader.request('POST', '/tasks', taskBody)).data;
        const foreignMilestone = (await unrelated.request('GET', '/teams/' + otherTeam.id)).data.milestones[0].id;
        await leader.request('POST', '/tasks', { ...taskBody, milestoneId: foreignMilestone }, 400);
        await leader.request('POST', '/tasks', { ...taskBody, assignedStudentIds: [outsider.id] }, 400);
        assert.equal((await leader.request('GET', '/tasks?teamId=' + team.id)).data.length, 1);
    });
    await test('Task dates without an explicit timezone are rejected without adding tasks', async () => {
        const before = (await leader.request('GET', '/tasks?teamId=' + team.id)).data.length;
        await leader.request('POST', '/tasks', { ...taskBody, dueDate: '2026-11-20T10:00:00' }, 400);
        await leader.request('POST', '/tasks', { ...taskBody, startDate: '2026-11-13T10:00:00' }, 400);
        assert.equal((await leader.request('GET', '/tasks?teamId=' + team.id)).data.length, before);
    });
    await test('Administrator profile edits synchronize member, assignment, and supervisor details', async () => {
        const memberName = 'طالب اختبار بعد تعديل الإدارة';
        await admin.request('PATCH', '/admin/users/' + member.id, { fullName: memberName, department: 'حاسب آلي' });
        await admin.request('PATCH', '/admin/users/' + doctorUser.id, { fullName: 'مشرف بعد تعديل الإدارة', department: 'هندسة الحاسب' });
        const detail = (await leader.request('GET', '/teams/' + team.id)).data;
        assert.equal(detail.team.members.find(value => value.studentId === member.id).memberName, memberName);
        assert.equal(detail.tasks.find(value => value.id === task.id).assignments.find(value => value.assignedStudentId === member.id).studentName, memberName);
        const updatedSupervisor = (await admin.request('GET', '/supervisors')).data.find(value => value.userId === doctorUser.id);
        assert.equal(updatedSupervisor.name, 'مشرف بعد تعديل الإدارة');
        assert.equal(updatedSupervisor.department, 'هندسة الحاسب');
    });
    await test('Task permissions and invalid status values are enforced on the server', async () => {
        await unrelated.request('POST', `/tasks/${task.id}/status`, { status: 'Completed', actorId: owner.id }, 403);
        await colleague.request('POST', `/tasks/${task.id}/status`, { status: 'UnknownStatus' }, 400);
        await colleague.request('POST', `/tasks/${task.id}/status`, { status: 'InProgress' });
    });
    await test('Deadline notifications are generated once per task and recipient', async () => {
        await admin.request('POST', '/admin/maintenance');
        assert((await leader.request('GET', '/notifications')).data.some(n => n.kind === 'Overdue' && n.message.includes('بتوقيت القاهرة')));
        assert.equal((await admin.request('POST', '/admin/maintenance')).data.newNotifications, 0);
    });
    await test('Completion calculates progress and closes milestones from actual tasks', async () => {
        await colleague.request('POST', `/tasks/${task.id}/status`, { status: 'Completed' });
        for (const milestone of milestones.slice(1)) {
            const created = (await leader.request('POST', '/tasks', { ...taskBody, milestoneId: milestone.id, title: 'تنفيذ المرحلة ' + milestone.sequenceOrder, dueDate: new Date(Date.now() + 86400000).toISOString(), assignedStudentIds: [owner.id] })).data;
            await leader.request('POST', `/tasks/${created.id}/status`, { status: 'Completed' });
        }
        const detail = (await leader.request('GET', '/teams/' + team.id)).data;
        assert.equal(detail.team.timeline.completionPercentage, 100); assert(detail.milestones.every(m => m.isCompleted));
    });
    await test('Final archiving requires reviewed deliveries for every phase', async () => {
        await doctor.request('POST', `/teams/${team.id}/archive`, { grade: 92.5, academicYear: '2026-2027' }, 409);
    });
    await test('File upload validates size and format and persists downloadable deliveries', async () => {
        for (const milestone of milestones) {
            const form = new FormData(); form.set('teamId', team.id); form.set('milestoneId', milestone.id); form.set('title', 'تقرير المرحلة ' + milestone.sequenceOrder);
            form.set('file', new File(['Phase report ' + milestone.sequenceOrder], '../../report-' + milestone.sequenceOrder + '.txt', { type: 'text/plain' }));
            const submission = (await leader.request('POST', '/submissions', form)).data; submissions.push(submission);
            assert.equal(submission.originalFileName, 'report-' + milestone.sequenceOrder + '.txt');
        }
        const bad = new FormData(); bad.set('teamId', team.id); bad.set('milestoneId', milestones[0].id); bad.set('title', 'ملف غير صالح'); bad.set('file', new File(['not a pdf'], 'fake.pdf', { type: 'application/pdf' }));
        await leader.request('POST', '/submissions', bad, 400);
        const download = await colleague.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 200, true);
        assert.equal(await download.text(), 'Phase report 1'); assert(download.headers.get('content-disposition').includes('attachment'));
        await unrelated.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 403);
    });
    await test('Fake ZIP and Office uploads leave no files or submission records', async () => {
        const uploadDirectory = path.join(root, data, 'uploads');
        const beforeFiles = (await fs.readdir(uploadDirectory)).sort();
        for (const name of ['fake.zip', 'fake.docx']) {
            const form = new FormData(); form.set('teamId', team.id); form.set('milestoneId', milestones[0].id); form.set('title', 'ملف غير صالح');
            form.set('file', new File(['PK-not-a-real-container'], name, { type: 'application/octet-stream' }));
            await leader.request('POST', '/submissions', form, 400);
        }
        assert.equal((await leader.request('GET', '/teams/' + team.id)).data.submissions.length, submissions.length);
        assert.deepEqual((await fs.readdir(uploadDirectory)).sort(), beforeFiles);
    });
    await test('Deliveries are evaluated by the assigned supervisor with valid grades', async () => {
        await leader.request('POST', `/submissions/${submissions[0].id}/review`, { decision: 'Approved', grade: 95, feedback: 'جيد' }, 403);
        await doctor.request('POST', `/submissions/${submissions[0].id}/review`, { decision: 'Approved', grade: 101, feedback: 'جيد' }, 400);
        for (const submission of submissions) {
            const reviewed = (await doctor.request('POST', `/submissions/${submission.id}/review`, { decision: 'approved', grade: 95, feedback: 'تمت مراجعة التسليم' })).data;
            assert.equal(reviewed.decision, 'Approved');
        }
    });
    await test('Reviewing files and downloading files are independent grants', async () => {
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: ['team.view', 'submission.download'] });
        await doctor.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 200, true);
        await doctor.request('POST', `/submissions/${submissions[0].id}/review`, { decision: 'Approved', grade: 95 }, 403);
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: ['team.view', 'submission.review'] });
        await doctor.request('POST', `/submissions/${submissions[0].id}/review`, { decision: 'Approved', grade: 95 });
        await doctor.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 403);
        await admin.request('PUT', `/admin/supervisors/${doctorUser.id}/permissions`, { permissionIds: allSupervisorPermissions });
    });
    await test('Invalid academic-year ranges do not archive an otherwise completed project', async () => {
        await doctor.request('POST', `/teams/${team.id}/archive`, { grade: 92.5, academicYear: '٢٠٢٦-٢٠٢٥' }, 400);
        assert.equal((await leader.request('GET', '/teams/' + team.id)).data.team.isArchived, false);
        assert.equal((await leader.request('GET', '/archive')).data.length, 0);
    });
    await test('Archiving saves final grade, locks edits, and releases supervision capacity', async () => {
        await leader.request('PATCH', '/teams/' + team.id, { name: '=SUM(1+1)', leaderStudentId: owner.id });
        const archived = (await doctor.request('POST', `/teams/${team.id}/archive`, { grade: 92.5, academicYear: '٢٠٢٦–٢٠٢٧' })).data;
        assert.equal(archived.teamId, team.id); assert.equal(archived.finalGrade, '92.5');
        assert.equal(archived.academicYear, '2026-2027');
        assert.equal((await admin.request('GET', '/supervisors')).data.find(s => s.id === supervisor.id).activeTeamsCount, 0);
        await leader.request('POST', `/tasks/${task.id}/status`, { status: 'ToDo' }, 409);
        assert((await leader.request('GET', '/archive')).data.some(a => a.teamId === team.id));
        const matches = (await leader.request('POST', '/ideas/similarity', { ...proposal, excludeIdeaId: idea.id })).data;
        assert(!matches.some(match => match.matchedItemId === archived.id), 'An archived project must not be compared with its own archive entry');
    });
    await test('Reports use real data, enforce scope, and escape CSV formula inputs', async () => {
        const report = (await doctor.request('GET', '/reports')).data; assert.equal(report.rows.length, 1); assert.equal(report.rows[0].timeline.completionPercentage, 100);
        const csv = await doctor.request('GET', '/reports/export', undefined, 200, true); const text = await csv.text(); assert(text.includes("'=SUM(1+1)")); assert(text.includes('92.5'));
        await leader.request('GET', '/reports', undefined, 403);
    });
    await test('Backups include the SQLite database, attachments, and authentication keys', async () => {
        backupName = (await admin.request('POST', '/admin/backups')).data.name;
        assert((await admin.request('GET', '/admin/backups')).data.some(b => b.name === backupName));
        const zip = Buffer.from(await (await admin.request('GET', `/admin/backups/${backupName}/download`, undefined, 200, true)).arrayBuffer());
        assert.equal(zip.subarray(0, 2).toString(), 'PK'); assert(zip.includes(Buffer.from('graduation.db'))); assert(zip.includes(Buffer.from('uploads/'))); assert(zip.includes(Buffer.from('keys/')));
    });
    await test('Source code, database files, and mail files are never served as static assets', async () => {
        for (const route of ['/Backend/Member1_UserManagement/AuthService.cs', '/Backend/CollegeAdministration/CollegeAdministrationApi.cs', '/Backend/SupervisorManagement/SupervisorPermissions.cs', '/Backend/Python/similarity_worker.py', '/data/graduation.db', '/' + data.replaceAll('\\', '/') + '/graduation.db', '/appsettings.json']) assert.equal((await fetch(base + route)).status, 404);
    });
    await test('A full server restart preserves records, files, and valid sessions', async () => {
        await stop(); await start();
        assert.deepEqual((await doctor.request('GET', '/profile')).data.permissions, allSupervisorPermissions);
        assert.equal((await admin.request('GET', `/admin/supervisors/${doctorUser.id}/permissions`)).data.updatedBy, admin.user.id);
        const detail = (await leader.request('GET', '/teams/' + team.id)).data;
        assert.equal((await leader.request('GET', '/profile')).data.profile.studyYear, 2);
        assert.equal((await leader.request('GET', '/study/library')).data.courses[0].resources.length, 3);
        assert.equal(await (await leader.request('GET', studyFiles[0].downloadUrl.slice(4), undefined, 200, true)).text(), 'Study file Material');
        assert.equal(detail.team.isArchived, true); assert.equal(detail.team.grade, '92.5'); assert.equal(detail.submissions.length, 4); assert.equal(detail.tasks.length, 4);
        assert.equal(await (await colleague.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 200, true)).text(), 'Phase report 1');
    });
    await test('Deactivation immediately invalidates existing authentication sessions', async () => {
        await admin.request('PATCH', '/admin/users/' + member.id, { isActive: false }); await colleague.request('GET', '/profile', undefined, 401);
        await admin.request('PATCH', '/admin/users/' + admin.user.id, { isActive: false }, 400);
    });
    await test('Changing a role removes old supervisory grants and cannot restore them implicitly', async () => {
        await admin.request('PATCH', `/admin/users/${secondDoctorUser.id}`, { role: 'Student', department: 'حاسب آلي', studyYear: 2 });
        await secondDoctor.request('GET', '/profile', undefined, 401);
        await admin.request('PATCH', `/admin/users/${secondDoctorUser.id}`, { role: 'Supervisor' });
        await secondDoctor.login(secondDoctorUser.username, 'DoctorPassword!123');
        assert.deepEqual((await secondDoctor.request('GET', '/profile')).data.permissions, []);
    });
    await test('Password recovery invalidates old links and sessions without exposing tokens publicly', async () => {
        await publicClient.session(); const unknown = await publicClient.request('POST', '/auth/forgot-password', { email: 'not-registered@tests.local' });
        const first = await publicClient.request('POST', '/auth/forgot-password', { email: owner.email }); assert.equal(first.data, null); assert.equal(first.message, unknown.message);
        let messages = (await admin.request('GET', '/admin/mail')).data.messages.filter(m => m.to === owner.email && m.subject.includes('استعادة'));
        const oldToken = messages[0].body.match(/reset=([a-f0-9]{64})/)[1];
        await new Promise(resolve => setTimeout(resolve, 20)); await publicClient.request('POST', '/auth/forgot-password', { email: owner.email });
        messages = (await admin.request('GET', '/admin/mail')).data.messages.filter(m => m.to === owner.email && m.subject.includes('استعادة'));
        const token = messages[0].body.match(/reset=([a-f0-9]{64})/)[1]; assert.notEqual(token, oldToken);
        await publicClient.request('POST', '/auth/reset-password', { token: oldToken, newPassword: 'NewStudentPassword!123' }, 400);
        await publicClient.request('POST', '/auth/reset-password', { token, newPassword: 'NewStudentPassword!123' });
        await publicClient.request('POST', '/auth/reset-password', { token, newPassword: 'AnotherPassword!123' }, 400);
        await leader.request('GET', '/profile', undefined, 401); await leader.session();
        await leader.request('POST', '/auth/login', { usernameOrEmail: owner.username, password: 'StudentPassword!123' }, 401);
        await leader.login(owner.username, 'NewStudentPassword!123');
    });
    await test('Notification read state is persisted', async () => {
        await leader.request('POST', '/notifications/read-all'); assert((await leader.request('GET', '/notifications')).data.every(n => n.readAt));
    });
    await test('Restoring a backup recovers real accounts, project data, and downloadable files without overwriting existing data', async () => {
        await stop();
        const restoredDirectory = path.join('tests', '.runtime', 'restored-' + run);
        await restoreBackup(restoredDirectory);
        await restoreBackup(restoredDirectory, 1);
        await start(restoredDirectory);
        const restoredStudent = new Client(); await restoredStudent.login(owner.username, 'StudentPassword!123');
        const detail = (await restoredStudent.request('GET', '/teams/' + team.id)).data;
        assert.equal(detail.team.grade, '92.5'); assert.equal(detail.tasks.length, 4); assert.equal(detail.submissions.length, 4);
        assert.equal(await (await restoredStudent.request('GET', `/submissions/${submissions[0].id}/download`, undefined, 200, true)).text(), 'Phase report 1');
        assert.equal((await restoredStudent.request('GET', '/study/library')).data.courses[0].resources.length, 3);
        assert.equal(await (await restoredStudent.request('GET', studyFiles[1].downloadUrl.slice(4), undefined, 200, true)).text(), 'Study file Summary');
        assert.equal((await admin.request('GET', '/auth/session')).data.user.role, 'Admin', 'Backed-up authentication keys must keep the administrator session valid');
    });
    process.stdout.write(`\n${passed} API acceptance tests passed. Database: ${data}\n`);
} catch (error) {
    process.stderr.write(error.stack + '\n'); process.stderr.write(logs.slice(-3000) + '\n'); process.exitCode = 1;
} finally { await stop(); }
