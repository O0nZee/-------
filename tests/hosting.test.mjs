import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sdk = path.join(root, '.tools', 'dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
const application = process.env.GRADUATION_APP_DLL || path.join(root, 'tests', '.runtime', 'build', 'GraduationPlatform', 'net8.0', 'GraduationPlatform.dll');
const reservation = net.createServer(); reservation.listen(0, '127.0.0.1'); await once(reservation, 'listening');
const port = reservation.address().port; await new Promise(resolve => reservation.close(resolve));
const base = `http://localhost:${port}`, data = path.join('tests', '.runtime', 'hosting-' + randomUUID());
const password = 'HostingTestAdministrator!123';
let child, logs = '';
async function stop() {
    if (!child || child.exitCode !== null) return;
    const exit = once(child, 'exit'); child.kill(); await exit;
}
try {
    child = spawn(sdk, [application, '--contentRoot', root, '--environment', 'Production', '--urls', base,
        '--Platform:PublicUrl', 'https://public.example.test', '--Platform:DataDirectory', data, '--Platform:DemoData', 'false',
        '--Platform:UseHttpsProxy', 'true', '--Bootstrap:AdminPassword', password], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.on('data', chunk => logs += chunk); child.stderr.on('data', chunk => logs += chunk);
    let ready = false;
    for (let attempt = 0; attempt < 100; attempt++) {
        if (child.exitCode !== null) throw new Error(logs);
        try { if ((await fetch(base + '/api/health')).ok) { ready = true; break; } } catch { }
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(ready, logs);
    const session = await fetch(base + '/api/auth/session');
    const token = (await session.json()).data.csrfToken;
    const csrfCookie = session.headers.getSetCookie().find(cookie => cookie.startsWith('graduation.csrf='));
    assert(csrfCookie && /; secure(?:;|$)/i.test(csrfCookie), 'The HTTPS proxy emits a secure CSRF cookie');
    // Simulate the internal proxy hop; the public connection uses HTTPS.
    const login = await fetch(base + '/api/auth/login', { method: 'POST', headers: {
        'Content-Type': 'application/json', 'X-CSRF-TOKEN': token, Cookie: csrfCookie.split(';')[0]
    }, body: JSON.stringify({ usernameOrEmail: 'admin', password }) });
    assert.equal(login.status, 200, await login.text());
    const authCookie = login.headers.getSetCookie().find(cookie => cookie.startsWith('graduation.session='));
    assert(authCookie && /; secure(?:;|$)/i.test(authCookie), 'The HTTPS proxy emits a secure session cookie');
    const profile = await fetch(base + '/api/profile', { headers: { Cookie: authCookie.split(';')[0] } });
    assert.equal(profile.status, 200);
    assert.equal((await profile.json()).data.role, 'Admin');
    console.log('PASS: Production startup, CSRF, login and secure cookies behind the HTTPS proxy.');
} finally { await stop(); }
