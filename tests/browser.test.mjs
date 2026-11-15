import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import net from 'node:net';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const require = createRequire(import.meta.url);
let playwright;
try { playwright = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright'); }
catch {
    const bundled = path.join(os.homedir(), '.cache', 'codex-runtimes', 'codex-primary-runtime', 'dependencies', 'node', 'node_modules', 'playwright');
    try { playwright = require(bundled); }
    catch { throw new Error('Install Playwright and Chromium, or set PLAYWRIGHT_MODULE_PATH. See README.md.'); }
}
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const localSdk = path.join(root, '.tools', 'dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
const sdk = process.env.GRADUATION_DOTNET || (await fs.access(localSdk).then(() => localSdk, () => 'dotnet'));
const run = randomUUID().slice(0, 8), data = path.join('tests', '.runtime', 'browser-' + run);
const outputDirectory = path.join(root, 'tests', 'browser-results', run);
await fs.mkdir(outputDirectory, { recursive: true });
const reservation = net.createServer(); reservation.listen(0, '127.0.0.1'); await once(reservation, 'listening');
const port = reservation.address().port; await new Promise(resolve => reservation.close(resolve));
const base = `http://localhost:${port}`, password = 'BrowserStudent!123', adminPassword = 'BrowserAdministrator!123';
const accounts = { student: 'student_' + run, member: 'member_' + run, supervisor: 'supervisor_' + run };
let child, browser, serverLogs = '', passed = 0, team, details, task;
const browserErrors = [], pages = [];

async function start() {
    const app = process.env.GRADUATION_APP_DLL || path.join(root, 'bin', 'Release', 'net8.0', 'GraduationPlatform.dll');
    child = spawn(sdk, [app, '--contentRoot', root, '--environment', 'Development', '--urls', base, '--Platform:PublicUrl', base, '--Platform:DataDirectory', data, '--Platform:DemoData', 'false', '--Bootstrap:AdminPassword', adminPassword], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.on('data', chunk => serverLogs += chunk); child.stderr.on('data', chunk => serverLogs += chunk);
    for (let i = 0; i < 100; i++) {
        if (child.exitCode !== null) throw new Error('Test server exited: ' + serverLogs.slice(-2000));
        try { if ((await fetch(base + '/api/health')).ok) return; } catch { }
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error('The browser test server did not become ready.');
}
async function pageFor(name) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'ar-EG', timezoneId: 'Africa/Cairo', acceptDownloads: true });
    const page = await context.newPage(); page.testName = name; pages.push(page);
    page.on('pageerror', error => browserErrors.push(name + ': ' + error.message));
    page.on('console', message => { if (message.type() === 'error') browserErrors.push(name + ': ' + message.text()); });
    page.on('dialog', dialog => dialog.accept());
    page.setDefaultTimeout(10000); return page;
}
async function idle(page) {
    await page.waitForFunction(() => !document.querySelector('.section-loader') && !document.querySelector('form[aria-busy="true"]'));
}
async function section(page, name) {
    if (await page.locator('.menu-toggle').isVisible()) await page.locator('[data-action="toggle-menu"]').click();
    await page.locator(`.nav-item[data-section="${name}"]`).click();
    await idle(page);
    assert(!await page.locator('#content > .message-box.error').count(), `Unable to open ${name}`);
}
async function login(page, username, loginPassword = password) {
    await page.goto(base); await page.locator('#loginForm').waitFor({ state: 'visible' });
    await page.locator('#loginForm [name="usernameOrEmail"]').fill(username);
    await page.locator('#loginForm [name="password"]').fill(loginPassword);
    await page.locator('#loginForm [type="submit"]').click();
    await page.waitForURL(/\/(student|admin|supervisor)(#.*)?$/);
    await page.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(page);
}
async function submit(page, kind, route, method = 'POST') {
    const [response] = await Promise.all([
        page.waitForResponse(response => response.url() === base + '/api' + route && response.request().method() === method),
        page.locator(`form[data-form="${kind}"], form[data-auth-form="${kind}"]`).locator('[type="submit"]').click()
    ]);
    const result = await response.json();
    assert(response.ok(), `${kind}: ${result.message}`); await idle(page); return result.data;
}
async function action(page, locator, route, method = 'POST') {
    const [response] = await Promise.all([
        page.waitForResponse(response => response.url() === base + '/api' + route && response.request().method() === method),
        locator.click()
    ]);
    assert(response.ok(), await response.text()); return response;
}
async function read(page, route) {
    const response = await page.request.get(base + '/api' + route); assert.equal(response.status(), 200, route); return (await response.json()).data;
}
async function screenshot(page, name) { await page.screenshot({ path: path.join(outputDirectory, name + '.png'), fullPage: true }); }
async function test(name, runTest) { await runTest(); passed++; process.stdout.write(`PASS ${passed}: ${name}\n`); }
async function createAccount(admin, username, fullName, role) {
    await admin.locator('[data-action="create-user"]').click();
    const form = admin.locator('[data-form="user-create"]');
    for (const [name, value] of Object.entries({ fullName, username, email: username + '@browser.local', password, department: 'علوم الحاسب' })) await form.locator(`[name="${name}"]`).fill(value);
    await form.locator('[name="role"]').selectOption(role); await submit(admin, 'user-create', '/admin/users');
    await admin.locator('#usersTable tr').filter({ hasText: username }).waitFor({ state: 'visible' });
}

try {
    await start();
    let channel = process.env.PLAYWRIGHT_CHANNEL;
    if (!channel && process.platform === 'win32' && !await fs.access(playwright.chromium.executablePath()).then(() => true, () => false)) {
        const edge = path.join(process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)', 'Microsoft', 'Edge', 'Application', 'msedge.exe');
        if (await fs.access(edge).then(() => true, () => false)) channel = 'msedge';
    }
    browser = await playwright.chromium.launch({ headless: true, channel });
    const student = await pageFor('student'), admin = await pageFor('admin'), supervisor = await pageFor('supervisor'), member = await pageFor('member');
    await test('The login page appears first and registration remains available on desktop and mobile', async () => {
        await student.goto(base); await student.locator('#loginForm').waitFor({ state: 'visible' });
        assert.equal(await student.locator('html').getAttribute('dir'), 'rtl'); await screenshot(student, 'login-desktop');
        await student.setViewportSize({ width: 390, height: 844 }); await screenshot(student, 'login-mobile');
        assert(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        const loginControls = await student.locator('#loginUsername, #loginPassword, #loginForm [type="submit"]').evaluateAll(elements => elements.map(element => ({ top: element.getBoundingClientRect().top, bottom: element.getBoundingClientRect().bottom })));
        assert(loginControls.every(control => control.top >= 0 && control.bottom <= 844), 'Login controls should fit on the first mobile screen');
        await student.setViewportSize({ width: 320, height: 844 });
        assert(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        await student.setViewportSize({ width: 1440, height: 1000 });
        await student.locator('[data-auth-tab="register"]').click(); const form = student.locator('#registerForm');
        for (const [name, value] of Object.entries({ fullName: 'ليلى اختبار', username: accounts.student, email: accounts.student + '@browser.local', department: 'علوم الحاسب', academicId: 'UI-001', password, confirmPassword: 'MismatchingPassword!' })) await form.locator(`[name="${name}"]`).fill(value);
        await form.locator('[type="submit"]').click(); await student.locator('#authMessage.error').waitFor({ state: 'visible' });
        await form.locator('[name="confirmPassword"]').fill(password); await form.locator('[type="submit"]').click();
        await student.waitForURL(base + '/student#overview'); await student.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(student);
        assert.equal((await read(student, '/profile')).role, 'Student');
    });
    await test('All login aliases open the username and password form and protect private portals', async () => {
        const entry = await pageFor('login-entry');
        for (const route of ['/', '/login', '/login.html', '/Frontend/Pages/login.html', '/index.html']) {
            await entry.goto(base + route); await entry.locator('#loginForm').waitFor({ state: 'visible' });
            assert.equal(await entry.title(), 'تسجيل الدخول | منصة مشروعات التخرج');
            assert.equal(await entry.locator('#loginForm input:visible').count(), 2);
            assert.equal(await entry.locator('#loginForm [name="role"]').count(), 0);
            assert(!await entry.locator('#registerForm').isVisible());
        }
        for (const route of ['/student', '/supervisor', '/admin']) {
            await entry.goto(base + route); await entry.locator('#loginForm').waitFor({ state: 'visible' });
            assert.equal(await entry.locator('#appRoot').count(), 0);
        }
        await entry.close();
    });
    await test('Administration creates student and supervisor accounts from the account dialog', async () => {
        await login(admin, 'admin', adminPassword); await section(admin, 'users');
        await createAccount(admin, accounts.member, 'عمر اختبار', 'Student'); await createAccount(admin, accounts.supervisor, 'د. سارة اختبار', 'Supervisor');
        const users = await read(admin, '/admin/users'); assert.equal(users.length, 4);
    });
    await test('An unassigned supervisor can refresh an empty overview', async () => {
        await login(supervisor, accounts.supervisor); await supervisor.getByRole('button', { name: 'تحديث الفرق', exact: true }).click();
        await idle(supervisor); assert.equal(new URL(supervisor.url()).hash, '#overview');
    });
    await test('Existing student, supervisor, and administrator sessions return to their own portals', async () => {
        for (const [page, role, portal] of [[student, 'Student', '/student'], [supervisor, 'Supervisor', '/supervisor'], [admin, 'Admin', '/admin']]) {
            await page.goto(base + '/login'); await page.waitForURL(base + portal + '#overview');
            await page.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(page);
            assert.equal(await page.locator('body').getAttribute('data-role'), role);
        }
        await student.goto(base + '/admin'); await student.waitForURL(base + '/student#overview');
        await student.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(student);
        assert.equal(await student.locator('body').getAttribute('data-role'), 'Student');
    });
    await test('A student creates a team and sends an invitation through the real forms', async () => {
        await section(student, 'team'); await student.locator('[data-form="create-team"] [name="name"]').fill('فريق المياه & القياس');
        team = await submit(student, 'create-team', '/teams');
        await student.locator('[data-form="invite"] [name="email"]').fill(accounts.member + '@browser.local');
        await submit(student, 'invite', `/teams/${team.id}/invitations`); details = await read(student, '/teams/' + team.id); assert.equal(details.milestones.length, 4);
    });
    await test('Invitation acceptance joins the colleague to the same persisted team', async () => {
        await login(member, accounts.member); const invitation = (await read(member, '/invitations'))[0];
        await action(member, member.locator(`[data-action="respond-invitation"][data-id="${invitation.id}"][data-accept="true"]`), `/invitations/${invitation.id}/respond`);
        await member.locator('#teamSelect').waitFor({ state: 'visible' }); details = await read(member, '/teams/' + team.id); assert.equal(details.team.members.length, 2);
    });
    await test('Administration edits supervisor capacity and assigns the team', async () => {
        await section(admin, 'supervisors'); const profile = (await read(admin, '/supervisors'))[0];
        await admin.locator(`[data-action="edit-supervisor"][data-id="${profile.id}"]`).click();
        await admin.locator('[data-form="supervisor-edit"] [name="maxTeams"]').fill('2');
        await admin.locator('[data-form="supervisor-edit"] [name="specialization"]').fill('الحساسات وتحليل البيانات');
        await submit(admin, 'supervisor-edit', '/supervisors/' + profile.id, 'PATCH');
        await section(admin, 'team'); await admin.locator('[data-form="supervisor-assign"] [name="supervisorId"]').selectOption(profile.id);
        await submit(admin, 'supervisor-assign', `/teams/${team.id}/supervisor`);
        assert.equal((await read(admin, '/teams/' + team.id)).team.supervisor.name, 'د. سارة اختبار');
    });
    await test('College administration grants, revokes, and restores permissions through the supervisor dialog', async () => {
        await section(admin, 'supervisors');
        const profile = (await read(admin, '/supervisors'))[0];
        const openPermissions = async () => {
            await admin.locator(`[data-action="supervisor-permissions"][data-id="${profile.id}"]`).click();
            await admin.locator('[data-form="supervisor-permissions"]').waitFor({ state: 'visible' });
        };
        await openPermissions();
        assert.equal(await admin.locator('[name="permissionIds"]:checked').count(), 0);
        await admin.locator('[name="permissionIds"][value="team.view"]').check();
        await submit(admin, 'supervisor-permissions', `/admin/supervisors/${profile.userId}/permissions`, 'PUT');
        await supervisor.reload(); await supervisor.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(supervisor);
        assert(await supervisor.locator('.nav-item[data-section="ideas"]').isVisible());
        assert(!await supervisor.locator('.nav-item[data-section="reports"]').isVisible());
        await section(supervisor, 'team'); assert.equal(await supervisor.locator('[data-action="reminder"]').count(), 0);
        await openPermissions(); await admin.locator('[name="permissionIds"][value="team.view"]').uncheck();
        await submit(admin, 'supervisor-permissions', `/admin/supervisors/${profile.userId}/permissions`, 'PUT');
        await supervisor.locator('[data-action="refresh"]').first().click(); await idle(supervisor);
        assert(!await supervisor.locator('.nav-item[data-section="ideas"]').isVisible());
        assert.equal(new URL(supervisor.url()).hash, '#overview');
        await openPermissions();
        for (const checkbox of await admin.locator('[name="permissionIds"]').all()) await checkbox.check();
        await admin.setViewportSize({ width: 390, height: 844 }); await screenshot(admin, 'admin-supervisor-permissions-mobile');
        const dimensions = await admin.evaluate(() => ({ width: innerWidth, content: document.documentElement.scrollWidth }));
        assert(dimensions.content <= dimensions.width + 1);
        await submit(admin, 'supervisor-permissions', `/admin/supervisors/${profile.userId}/permissions`, 'PUT');
        await admin.setViewportSize({ width: 1440, height: 1000 });
        await supervisor.reload(); await supervisor.locator('#pageTitle').waitFor({ state: 'visible' }); await idle(supervisor);
        assert(await supervisor.locator('.nav-item[data-section="reports"]').isVisible());
        assert.equal((await read(supervisor, '/profile')).permissions.length, 7);
        await section(student, 'team'); assert.equal(await student.locator('[data-action="supervisor-permissions"]').count(), 0);
    });
    await test('The student submits a proposal and receives an automatic similarity result', async () => {
        await section(student, 'ideas'); const form = student.locator('[data-form="idea"]');
        for (const [name, value] of Object.entries({ title: 'متابعة جودة المياه بالحساسات', description: 'تجميع قياسات الحساسات وتحليل تغير جودة المياه مع لوحة متابعة وتقارير وتنبيهات لفريق التشغيل.', objectives: 'قياس الدقة وتحسين الاستجابة', technologies: 'Python, React, IoT', track: 'IoT' })) await form.locator(`[name="${name}"]`).fill(value);
        await student.locator('[data-action="similarity"]').click(); await student.locator('#similarityOutput .message-box').waitFor({ state: 'visible' });
        await submit(student, 'idea', '/ideas'); await section(student, 'tasks'); assert.equal(await student.locator('[data-action="new-task"]').count(), 0);
    });
    await test('Supervisor feedback, student revision, and approval are visible in the review history', async () => {
        await section(supervisor, 'ideas'); const idea = (await read(supervisor, '/ideas'))[0];
        await supervisor.locator('[data-action="idea-review"][data-decision="ModificationRequested"]').click();
        await supervisor.locator('[data-form="idea-review"] [name="feedback"]').fill('أضف خطة اختبار دقة الحساسات.');
        await submit(supervisor, 'idea-review', `/ideas/${idea.id}/review`);
        await section(student, 'ideas'); await student.locator('[data-form="idea"] [name="objectives"]').fill('اختبار دقة الحساسات وتحسين الاستجابة');
        await submit(student, 'idea', '/ideas/' + idea.id, 'PUT');
        await section(supervisor, 'ideas'); await supervisor.locator('[data-action="idea-review"][data-decision="Approved"]').click();
        await supervisor.locator('[data-form="idea-review"] [name="feedback"]').fill('الخطة معتمدة.'); await submit(supervisor, 'idea-review', `/ideas/${idea.id}/review`);
        assert.equal((await read(student, '/teams/' + team.id)).ideas[0].reviews.length, 2); await screenshot(supervisor, 'supervisor-idea-review');
    });
    await test('Task creation respects Cairo dates and assigns a colleague', async () => {
        await section(student, 'tasks'); await student.locator('[data-action="new-task"]').click(); const form = student.locator('[data-form="task"]');
        await form.locator('[name="title"]').fill('معايرة الحساسات'); await form.locator('[name="description"]').fill('اختبار قراءات الحساسات مع قيم مرجعية');
        await form.locator('[name="startDate"]').fill('2026-11-10T09:30'); await form.locator('[name="dueDate"]').fill('2026-11-13T15:00');
        const studentUser = await read(student, '/profile'), memberUser = await read(member, '/profile');
        await form.locator(`[name="assignedStudentIds"][value="${studentUser.id}"]`).uncheck(); await form.locator(`[name="assignedStudentIds"][value="${memberUser.id}"]`).check();
        task = await submit(student, 'task', '/tasks'); assert.equal(task.dueDate, '2026-11-13T13:00:00Z'); assert.equal(task.assignments[0].assignedStudentId, memberUser.id);
    });
    await test('The assigned colleague starts and completes a Kanban task', async () => {
        await section(member, 'tasks');
        await action(member, member.locator(`[data-action="task-status"][data-id="${task.id}"][data-status="InProgress"]`), `/tasks/${task.id}/status`);
        await member.locator(`[data-task-id="${task.id}"][data-status="InProgress"]`).waitFor({ state: 'visible' });
        await action(member, member.locator(`[data-action="task-status"][data-id="${task.id}"][data-status="Completed"]`), `/tasks/${task.id}/status`);
        await member.locator(`[data-task-id="${task.id}"][data-status="Completed"]`).waitFor({ state: 'visible' });
        assert.equal((await read(member, '/teams/' + team.id)).team.timeline.completionPercentage, 100);
    });
    await test('Task editing and dragging between Kanban columns update the actual project', async () => {
        await section(student, 'tasks'); const created = [];
        for (const milestone of details.milestones.slice(1)) {
            await student.locator('[data-action="new-task"]').click(); const form = student.locator('[data-form="task"]');
            await form.locator('[name="title"]').fill('تنفيذ المرحلة ' + milestone.sequenceOrder);
            await form.locator('[name="milestoneId"]').selectOption(milestone.id); created.push(await submit(student, 'task', '/tasks'));
        }
        const edited = created[0]; await student.locator(`[data-action="edit-task"][data-id="${edited.id}"]`).click();
        await student.locator('[data-form="task"] [name="title"]').fill('تصميم قاعدة القياسات');
        await student.locator('[data-form="task"] [name="priority"]').selectOption('High');
        await submit(student, 'task', '/tasks/' + edited.id, 'PUT');
        const dragged = student.waitForResponse(response => response.url() === base + `/api/tasks/${edited.id}/status` && response.request().method() === 'POST');
        await student.locator(`[data-task-id="${edited.id}"]`).dragTo(student.locator('[data-drop-status="InProgress"]'));
        assert((await dragged).ok()); await student.locator(`[data-task-id="${edited.id}"][data-status="InProgress"]`).waitFor({ state: 'visible' });
        await screenshot(student, 'student-kanban-desktop');
        for (const item of created) {
            const status = item.id === edited.id ? 'InProgress' : 'ToDo';
            if (status === 'ToDo') {
                await action(student, student.locator(`[data-action="task-status"][data-id="${item.id}"][data-status="InProgress"]`), `/tasks/${item.id}/status`);
                await student.locator(`[data-task-id="${item.id}"][data-status="InProgress"]`).waitFor({ state: 'visible' });
            }
            await action(student, student.locator(`[data-action="task-status"][data-id="${item.id}"][data-status="Completed"]`), `/tasks/${item.id}/status`);
            await student.locator(`[data-task-id="${item.id}"][data-status="Completed"]`).waitFor({ state: 'visible' });
        }
        details = await read(student, '/teams/' + team.id); assert(details.milestones.every(m => m.isCompleted));
    });
    await test('Every phase accepts a real upload and the colleague can download its content', async () => {
        await section(student, 'submissions');
        for (const milestone of details.milestones) {
            await student.locator(`[data-action="upload"][data-id="${milestone.id}"]`).click();
            await student.locator('[data-form="upload"] [name="title"]').fill('تقرير المرحلة ' + milestone.sequenceOrder);
            await student.locator('[data-form="upload"] [name="file"]').setInputFiles({ name: `report-${milestone.sequenceOrder}.txt`, mimeType: 'text/plain', buffer: Buffer.from('Verified phase ' + milestone.sequenceOrder) });
            await submit(student, 'upload', '/submissions');
        }
        details = await read(student, '/teams/' + team.id); assert.equal(details.submissions.length, 4);
        await section(member, 'submissions'); const downloadEvent = member.waitForEvent('download');
        await member.locator(`a[href="/api/submissions/${details.submissions[0].id}/download"]`).click(); const download = await downloadEvent;
        assert.equal(download.suggestedFilename(), 'report-1.txt'); assert.equal(await fs.readFile(await download.path(), 'utf8'), 'Verified phase 1');
    });
    await test('The supervisor evaluates deliveries and their review history remains visible', async () => {
        await section(supervisor, 'submissions'); assert(!await supervisor.locator('[data-action="archive-final"]').isEnabled());
        for (let i = 0; i < details.submissions.length; i++) {
            const submission = details.submissions[i]; await supervisor.locator(`[data-action="submission-review"][data-id="${submission.id}"]`).click();
            await supervisor.locator('[data-form="submission-review"] [name="grade"]').fill(String(94 + i));
            await supervisor.locator('[data-form="submission-review"] [name="feedback"]').fill('تمت مراجعة الملف والموافقة على التسليم.');
            await submit(supervisor, 'submission-review', `/submissions/${submission.id}/review`);
        }
        const current = await read(supervisor, '/teams/' + team.id); assert(current.submissions.every(s => s.decision === 'Approved' && s.reviews.length === 1));
        assert(await supervisor.locator('[data-action="archive-final"]').isEnabled()); await screenshot(supervisor, 'supervisor-deliveries-desktop');
    });
    await test('Supervisor reminders appear in student notifications and read state persists', async () => {
        await section(supervisor, 'team'); await supervisor.locator('[data-action="reminder"]').click();
        await supervisor.locator('[data-form="reminder"] [name="message"]').fill('راجعوا تجهيزات المناقشة النهائية.');
        await submit(supervisor, 'reminder', `/teams/${team.id}/reminder`);
        await section(student, 'notifications'); assert((await read(student, '/notifications')).some(n => n.message.includes('تجهيزات المناقشة')));
        await action(student, student.locator('[data-action="read-all"]'), '/notifications/read-all');
        await student.waitForFunction(() => !document.querySelector('#content .notification.unread'));
        assert((await read(student, '/notifications')).every(n => n.readAt));
    });
    await test('Profile updates persist while HTML entered into a biography stays harmless', async () => {
        await section(student, 'profile'); const biography = '</textarea><img src=x onerror="window.__xss=1">';
        await student.locator('[data-form="profile"] [name="fullName"]').fill('ليلى أحمد');
        await student.locator('[data-form="profile"] [name="phone"]').fill('+201000000001'); await student.locator('[data-form="profile"] [name="bio"]').fill(biography);
        await submit(student, 'profile', '/profile', 'PATCH'); await student.reload(); await student.locator('[data-form="profile"]').waitFor({ state: 'visible' });
        assert.equal(await student.locator('[data-form="profile"] [name="bio"]').inputValue(), biography);
        assert.equal(await student.evaluate(() => window.__xss), undefined); assert.equal(await student.locator('#content img').count(), 0);
        assert.equal((await read(student, '/profile')).profile.phone, '+201000000001');
    });
    await test('Administration adds and searches historical projects with escaped content', async () => {
        await section(admin, 'archive'); await admin.locator('[data-action="archive-history"]').click();
        const form = admin.locator('[data-form="archive-history"]');
        for (const [name, value] of Object.entries({ title: 'إدارة أجهزة الجامعة', abstract: 'خبرة سابقة <img src=x onerror="window.__xss=1"> في تحليل القياسات.', academicYear: '2025-2026', grade: 'A', department: 'علوم الحاسب', track: 'IoT', supervisorName: 'د. سارة اختبار', technologies: 'Python, IoT' })) await form.locator(`[name="${name}"]`).fill(value);
        await submit(admin, 'archive-history', '/archive'); await admin.locator('[data-form="archive-search"] [name="keyword"]').fill('إدارة أجهزة');
        const searched = admin.waitForResponse(response => response.url().includes('/api/archive?keyword=') && response.request().method() === 'GET');
        await admin.locator('[data-form="archive-search"] [type="submit"]').click(); assert((await searched).ok()); await idle(admin);
        assert.equal(await admin.locator('#archiveResults .card').count(), 1); assert.equal(await admin.locator('#archiveResults img').count(), 0); assert.equal(await admin.evaluate(() => window.__xss), undefined);
    });
    await test('Final evaluation archives the project and removes editing actions', async () => {
        await section(supervisor, 'submissions'); await supervisor.locator('[data-action="archive-final"]').click();
        assert.equal(await supervisor.locator('[data-form="archive-final"] [name="grade"]').inputValue(), '95.5');
        await supervisor.locator('[data-form="archive-final"] [name="grade"]').fill('92.5'); await supervisor.locator('[data-form="archive-final"] [name="academicYear"]').fill('2026-2027');
        await submit(supervisor, 'archive-final', `/teams/${team.id}/archive`); await supervisor.locator('#content > .message-box.success').waitFor({ state: 'visible' });
        await section(student, 'tasks'); assert.equal(await student.locator('[data-action="new-task"], [data-action="task-status"]').count(), 0);
        assert.equal((await read(student, '/teams/' + team.id)).team.grade, '92.5'); assert.equal((await read(admin, '/supervisors'))[0].activeTeamsCount, 0);
    });
    await test('Reports download real CSV data and provide a clean printable PDF', async () => {
        await section(admin, 'reports'); const downloadEvent = admin.waitForEvent('download');
        await admin.locator('a[href="/api/reports/export"]').click(); const download = await downloadEvent;
        const csv = await fs.readFile(await download.path(), 'utf8'); assert(csv.includes('92.5')); assert(csv.includes('فريق المياه & القياس'));
        await screenshot(admin, 'admin-reports-desktop'); await admin.emulateMedia({ media: 'print' });
        assert.equal(await admin.locator('.sidebar').isVisible(), false); await screenshot(admin, 'report-print');
        const pdf = await admin.pdf({ path: path.join(outputDirectory, 'report.pdf'), format: 'A4', landscape: true, printBackground: true });
        assert.equal(pdf.subarray(0, 4).toString(), '%PDF'); assert(pdf.length > 1000); await admin.emulateMedia({ media: 'screen' });
    });
    await test('Administration downloads a backup and previews mail actually saved as local EML', async () => {
        await section(admin, 'operations'); const response = await action(admin, admin.locator('[data-action="backup"]'), '/admin/backups');
        const backup = (await response.json()).data; const link = admin.locator(`a[href="/api/admin/backups/${backup.name}/download"]`); await link.waitFor({ state: 'visible' });
        const downloadEvent = admin.waitForEvent('download'); await link.click(); const download = await downloadEvent;
        assert.equal((await fs.readFile(await download.path())).subarray(0, 2).toString(), 'PK');
        const mail = await read(admin, '/admin/mail'); assert.equal(mail.mode, 'Local'); assert.equal(mail.localPreview, true);
        const record = mail.messages.at(-1), eml = path.join(root, data, 'mail', record.id + '.eml');
        for (let i = 0; i < 50; i++) {
            if (await fs.access(eml).then(() => true, () => false)) break;
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        const saved = await fs.readFile(eml, 'utf8'); assert.equal(Buffer.from(saved.split('\r\n\r\n')[1], 'base64').toString('utf8'), record.body);
        await admin.locator(`[data-action="view-mail"][data-id="${record.id}"]`).click(); await admin.locator('#actionDialog .mail-body').waitFor({ state: 'visible' });
        await admin.locator('#actionDialog [data-action="close-dialog"]').first().click(); await screenshot(admin, 'admin-operations-desktop');
    });
    for (const [name, page] of [['student', student], ['supervisor', supervisor], ['admin', admin]]) {
        await test(`${name} navigation and all portal sections fit desktop and small mobile screens`, async () => {
            const sections = await page.locator('.nav-item').evaluateAll(items => items.map(item => item.dataset.section));
            for (const width of [1440, 390, 320]) {
                await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 });
                for (const name of sections) {
                    await section(page, name);
                    const dimensions = await page.evaluate(() => ({ width: innerWidth, content: document.documentElement.scrollWidth }));
                    assert(dimensions.content <= dimensions.width + 1, `${page.testName}/${name}: ${JSON.stringify(dimensions)}`);
                }
                await section(page, 'overview'); if (width !== 320) await screenshot(page, `${name}-overview-${width === 1440 ? 'desktop' : 'mobile'}`);
                if (width === 390 && name === 'student') { await section(page, 'profile'); await screenshot(page, 'student-profile-mobile'); }
                if (width === 390 && name === 'admin') {
                    await section(page, 'users'); await page.locator('[data-action="edit-user"]').first().click();
                    await screenshot(page, 'admin-account-dialog-mobile'); await page.locator('#actionDialog [data-action="close-dialog"]').first().click();
                }
            }
            await page.setViewportSize({ width: 1440, height: 1000 });
        });
    }
    const recovery = await pageFor('password-recovery');
    await test('Password recovery works through the local mail preview and a new login', async () => {
        await recovery.goto(base); await recovery.locator('[data-auth-tab="forgot"]').click();
        await recovery.locator('[data-auth-form="forgot"] [name="email"]').fill(accounts.student + '@browser.local');
        await submit(recovery, 'forgot', '/auth/forgot-password'); await recovery.locator('#authMessage.success').waitFor({ state: 'visible' });
        await section(admin, 'operations'); const mail = await read(admin, '/admin/mail');
        const message = mail.messages.find(m => m.to === accounts.student + '@browser.local' && m.subject.includes('استعادة'));
        await admin.locator(`[data-action="view-mail"][data-id="${message.id}"]`).click(); const resetLink = await admin.locator('#actionDialog .mail-body a').getAttribute('href');
        await recovery.goto(resetLink); await recovery.locator('#resetForm').waitFor({ state: 'visible' });
        const newPassword = 'UpdatedBrowserStudent!123'; await recovery.locator('#resetForm [name="newPassword"]').fill(newPassword);
        await recovery.locator('#resetForm [name="confirmPassword"]').fill(newPassword); await submit(recovery, 'reset', '/auth/reset-password');
        await recovery.locator('#loginForm').waitFor({ state: 'visible' }); await login(recovery, accounts.student, newPassword);
        assert.equal((await student.request.get(base + '/api/profile')).status(), 401); await student.close();
    });
    await test('Logout clears the session and returns the user to the account screen', async () => {
        await recovery.locator('[data-action="logout"]').click(); await recovery.waitForURL(base + '/'); await recovery.locator('#loginForm').waitFor({ state: 'visible' });
        assert.equal((await read(recovery, '/auth/session')).user, null);
    });
    await test('Every tested page has no browser errors, failed assets, or unsafe script execution', async () => {
        assert.deepEqual(browserErrors, []);
        for (const page of pages) if (!page.isClosed()) assert.equal(await page.evaluate(() => window.__xss), undefined);
    });
    process.stdout.write(`\n${passed} browser acceptance tests passed. Screenshots: ${outputDirectory}\n`);
} catch (error) {
    for (const page of pages) if (!page.isClosed()) await screenshot(page, 'failure-' + page.testName).catch(() => {});
    process.stderr.write(error.stack + '\n' + serverLogs.slice(-2000) + '\n'); process.exitCode = 1;
} finally {
    if (browser) await browser.close();
    if (child && child.exitCode === null) { const exited = once(child, 'exit'); child.kill(); await exited; }
}
