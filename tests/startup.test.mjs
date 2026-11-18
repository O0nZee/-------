import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';

assert.equal(process.platform, 'win32', 'These tests exercise the Windows launchers.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const run = randomUUID().slice(0, 8);
const data = path.join('tests', '.runtime', 'startup ' + run, 'data with spaces');
const cwd = path.join(root, 'tests', '.runtime', 'startup ' + run, 'another working directory');
await fs.mkdir(cwd, { recursive: true });
await fs.access(path.join(root, 'bin', 'Release', 'net8.0', 'GraduationPlatform.dll'));
const powershell = path.join(process.env.SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
const statePath = port => path.join(root, '.tools', 'run', `server-${port}.json`);
const readState = async port => JSON.parse((await fs.readFile(statePath(port), 'utf8')).replace(/^\uFEFF/, ''));
const exists = file => fs.access(file).then(() => true, () => false);
const literal = value => "'" + String(value).replaceAll("'", "''") + "'";
let passed = 0;

async function freePort() {
    const server = net.createServer();
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const port = server.address().port;
    await new Promise(resolve => server.close(resolve));
    return port;
}

async function runPowerShell(argumentsList, expected = 0, env = {}) {
    const child = spawn(powershell, ['-NoProfile', '-ExecutionPolicy', 'Bypass', ...argumentsList], {
        cwd, windowsHide: true, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe']
    });
    let output = '';
    child.stdout.on('data', chunk => output += chunk);
    child.stderr.on('data', chunk => output += chunk);
    let timer;
    try {
        const [code] = await Promise.race([
            once(child, 'close'),
            new Promise((_, reject) => { timer = setTimeout(() => {
                child.kill();
                child.stdout.destroy();
                child.stderr.destroy();
                reject(new Error('Launcher did not exit and close its streams within 45 seconds:\n' + output));
            }, 45000); })
        ]);
        assert.equal(code, expected, output || 'Launcher timed out without output.');
        return output;
    } finally { clearTimeout(timer); }
}

const runScript = (name, args = [], expected = 0, env = {}) =>
    runPowerShell(['-File', path.join(root, 'scripts', name), ...args.map(String)], expected, env);
const runCmd = (name, args = []) => runPowerShell(['-Command',
    '& ' + [path.join(root, name), ...args].map(literal).join(' ') + '; exit $LASTEXITCODE']);
const startArgs = port => ['-Port', port, '-NoBuild', '-NoBrowser', '-DataDirectory', data, '-StartupTimeoutSeconds', 20];
const health = async port => {
    const response = await fetch(`http://localhost:${port}/api/health`, { signal: AbortSignal.timeout(2000) });
    const body = await response.json();
    assert.equal(body.data.status, 'healthy');
    return body;
};
async function test(name, action) {
    await action();
    process.stdout.write(`PASS ${++passed}: ${name}\n`);
}

const port = await freePort();
const failurePort = await freePort();
const secondPort = await freePort();
let sentinel;
try {
    await test('Start.cmd exits while the server remains healthy, from another working directory', async () => {
        const output = await runCmd('Start.cmd', startArgs(port));
        assert.match(output, /The platform is ready/);
        await new Promise(resolve => setTimeout(resolve, 1000));
        await health(port);
        const response = await fetch(`http://localhost:${port}/`);
        assert.equal(response.status, 200);
        assert.equal((await response.text()).includes('id="loginForm"'), true, 'The root page should contain the login form.');
        assert.equal((await readState(port)).dataDirectory, path.resolve(root, data));
    });
    await test('Repeated starts reuse the existing process', async () => {
        const original = await readState(port);
        assert.match(await runCmd('Start.cmd', startArgs(port)), /already running/);
        assert.equal((await readState(port)).processId, original.processId);
    });
    await test('A running port cannot silently select another database', async () => {
        const args = startArgs(port);
        args[args.indexOf('-DataDirectory') + 1] = data + '-other';
        assert.match(await runScript('Start.ps1', args, 1), /Another data directory/);
        await health(port);
    });
    await test('A database already used on another port is rejected', async () => {
        assert.match(await runScript('Start.ps1', startArgs(secondPort), 1), /already used by another platform server/);
        await health(port);
    });
    await test('Stop.cmd stops the managed server and keeps its database', async () => {
        assert.match(await runCmd('Stop.cmd', ['-Port', port]), /has stopped/);
        assert.equal(await exists(statePath(port)), false);
        assert.equal(await exists(path.join(root, data, 'graduation.db')), true);
        await assert.rejects(() => health(port));
        assert.match(await runScript('Stop.ps1', ['-Port', port]), /No managed platform server/);
    });
    await test('Two simultaneous starts create one server', async () => {
        const outputs = await Promise.all([
            runScript('Start.ps1', startArgs(port)), runScript('Start.ps1', startArgs(port))
        ]);
        assert.equal(outputs.filter(output => /The platform is ready/.test(output)).length, 1);
        assert.equal(outputs.filter(output => /already running/.test(output)).length, 1);
        await health(port);
    });
    await test('Start recovers after the previous server exits unexpectedly', async () => {
        const original = await readState(port);
        await runPowerShell(['-Command', `$server = Get-Process -Id ${original.processId}; Stop-Process -InputObject $server; if (-not $server.WaitForExit(10000)) { throw 'Test server did not exit.' }`]);
        assert.match(await runScript('Start.ps1', startArgs(port)), /The platform is ready/);
        assert.notEqual((await readState(port)).startedUtcTicks, original.startedUtcTicks);
        await health(port);
    });
    await test('An occupied port reports the problem without replacing the other service', async () => {
        const otherService = net.createServer(socket => socket.destroy());
        otherService.listen(0, '127.0.0.1');
        await once(otherService, 'listening');
        const occupiedPort = otherService.address().port;
        try {
            assert.match(await runScript('Start.ps1', startArgs(occupiedPort), 1), /used by another application/);
            assert.equal(otherService.listening, true);
            assert.equal(await exists(statePath(occupiedPort)), false);
        } finally { await new Promise(resolve => otherService.close(resolve)); }
    });
    await test('Startup errors include the application log and leave no managed process', async () => {
        const args = startArgs(failurePort);
        args[args.indexOf('-DataDirectory') + 1] = data + '-failure';
        const output = await runScript('Start.ps1', args, 1, { Platform__MaxTeamMembers: '1' });
        assert.match(output, /Invalid platform limits/);
        assert.match(output, /Log:/);
        assert.equal(await exists(statePath(failurePort)), false);
        await assert.rejects(() => health(failurePort));
    });
    await test('A stale process record cannot stop an unrelated process with a reused ID', async () => {
        const state = await readState(port);
        await runScript('Stop.ps1', ['-Port', port]);
        sentinel = spawn(powershell, ['-NoProfile', '-Command', 'Start-Sleep -Seconds 90'], {
            cwd, windowsHide: true, stdio: 'ignore'
        });
        await once(sentinel, 'spawn');
        await fs.writeFile(statePath(port), JSON.stringify({ ...state,
            processId: sentinel.pid, executablePath: powershell, startedUtcTicks: '0'
        }));
        assert.match(await runScript('Stop.ps1', ['-Port', port]), /No managed platform server/);
        assert.equal(sentinel.exitCode, null);
        assert.equal(await exists(statePath(port)), false);
    });
    process.stdout.write(`\n${passed} Windows startup tests passed. Test data: ${data}\n`);
} finally {
    if (sentinel && sentinel.exitCode === null) {
        const exited = once(sentinel, 'exit');
        sentinel.kill();
        await exited;
    }
    for (const managedPort of [port, failurePort, secondPort]) {
        await runScript('Stop.ps1', ['-Port', managedPort]).catch(error => process.stderr.write(error.message + '\n'));
    }
}
