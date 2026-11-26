import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../CloudShell.Components/wwwroot/jsonCodeEditor.js', import.meta.url), 'utf8');

test('shared JSON assets, theme observation, and cleanup across multiple editors', async () => {
    const scripts = [];
    const themes = [];
    let diagnostics;
    let observer;
    globalThis.document = {
        baseURI: 'https://local.test/portal/', body: { dataset: { theme: 'dark' } },
        createElement: () => ({ remove() {} }),
        head: { appendChild(element) { scripts.push(element.src); queueMicrotask(() => element.onload()); } }
    };
    globalThis.monaco = {
        editor: { setTheme: theme => themes.push(theme) },
        languages: { json: { jsonDefaults: { setDiagnosticsOptions: value => diagnostics = value } } }
    };
    globalThis.require = (_, resolve) => resolve();
    globalThis.MutationObserver = class {
        constructor(callback) { this.callback = callback; observer = this; }
        observe(target, options) { this.target = target; this.options = options; }
        disconnect() { this.disconnected = true; }
    };
    const editor = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
    try {
        await Promise.all([editor.initialize(), editor.initialize()]);
        assert.equal(scripts.length, 2, 'concurrent editors share one asset load');
        assert.ok(scripts.every(url => url.startsWith('https://local.test/portal/_content/')));
        assert.equal(diagnostics.enableSchemaRequest, false);
        assert.equal(diagnostics.validate, true);
        assert.equal(themes.at(-1), 'vs-dark');
        document.body.dataset.theme = 'light';
        observer.callback();
        assert.equal(themes.at(-1), 'vs');
        editor.release();
        assert.notEqual(observer.disconnected, true, 'second editor still follows theme');
        editor.release();
        assert.equal(observer.disconnected, true);
        await editor.initialize();
        assert.equal(scripts.length, 2, 'reopening reuses loaded assets');
        assert.notEqual(observer.disconnected, true);
        editor.release();
    } finally {
        for (const name of ['document', 'monaco', 'require', 'MutationObserver']) delete globalThis[name];
    }
});
