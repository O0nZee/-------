import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

// Source-level checks use a fake document, never the user's browser or account.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [loginHtml, entryHtml, apiSource, authSource, schema] = await Promise.all([
    'Frontend/Pages/login.html', 'index.html', 'Frontend/Scripts/apiService.js',
    'Frontend/Scripts/authController.js', 'data base/schema.sql'
].map(file => fs.readFile(path.join(root, file), 'utf8')));
const expected = ['حاسب آلي إنجليزي', 'حاسب آلي', 'تكنولوجيا ذوي الاحتياجات الخاصة', 'تكنولوجيا', 'مسرح', 'إعلام', 'فنية', 'موسيقى'];
const catalog = { departments: expected.map((name, index) => ({ id: String(index), name })),
    years: [1, 2, 3, 4].map(value => ({ value, name: 'الفرقة ' + value })) };
const selectContent = (html, id) => html.match(new RegExp(`<select[^>]*id="${id}"[^>]*>([\\s\\S]*?)</select>`))[1];
const optionValues = html => Array.from(html.matchAll(/<option value="([^"]*)"/g), match => match[1]);
let passed = 0;
async function test(name, action) { await action(); console.log(`PASS ${++passed}: ${name}`); }

async function initialize({ catalogFails = false, sessionFails = false, oldDependencies = false, department = '', year = '' } = {}) {
    const elements = {
        authMessage: { hidden: true }, registerDepartment: { value: department, innerHTML: selectContent(loginHtml, 'registerDepartment') },
        registerStudyYear: { value: year, innerHTML: selectContent(loginHtml, 'registerStudyYear') }
    };
    const context = vm.createContext({
        URLSearchParams, location: { protocol: 'http:', search: '' }, history: {}, console: { warn() {} },
        document: { getElementById: id => elements[id], addEventListener() {} },
        catalog, catalogFails, sessionFails
    });
    if (!oldDependencies) vm.runInContext(apiSource, context);
    vm.runInContext(`${oldDependencies ? 'const api = {};' : ''}
        api.request = async () => { if (catalogFails) throw new Error('Catalog unavailable'); return catalog; };
        api.init = async () => { if (sessionFails) throw new Error('Session unavailable'); return null; };`, context);
    await vm.runInContext(authSource, context);
    return elements;
}

await test('All eight departments and four years exist before any JavaScript runs', () => {
    for (const html of [loginHtml, entryHtml]) {
        assert.deepEqual(optionValues(selectContent(html, 'registerDepartment')).slice(1), expected);
        assert.deepEqual(optionValues(selectContent(html, 'registerStudyYear')).slice(1), ['1', '2', '3', '4']);
        assert.match(html, /apiService\.js\?v=/);
        assert.match(html, /authController\.js\?v=/);
    }
    for (const name of expected) assert(schema.includes("'" + name + "'"), 'The database contains ' + name);
});
await test('Failed catalog loading or older cached dependencies leave registration choices available', async () => {
    for (const options of [{ catalogFails: true }, { oldDependencies: true }]) {
        const elements = await initialize(options);
        assert.deepEqual(optionValues(elements.registerDepartment.innerHTML).slice(1), expected);
        assert.deepEqual(optionValues(elements.registerStudyYear.innerHTML).slice(1), ['1', '2', '3', '4']);
    }
});
await test('A session failure cannot prevent the department catalog from populating', async () => {
    const elements = await initialize({ sessionFails: true });
    assert.deepEqual(optionValues(elements.registerDepartment.innerHTML).slice(1), expected);
    assert.equal(elements.authMessage.textContent, 'Session unavailable');
});
await test('A delayed catalog refresh preserves the department and year already chosen', async () => {
    const elements = await initialize({ department: 'موسيقى', year: '4' });
    assert.match(elements.registerDepartment.innerHTML, /value="موسيقى" selected/);
    assert.match(elements.registerStudyYear.innerHTML, /value="4" selected/);
});
console.log(`\n${passed} registration regression tests passed.`);
