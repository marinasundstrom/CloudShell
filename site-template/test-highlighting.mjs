// Run after `dotnet docfx docfx.json` to test the actual bundled engine and hook.
import assert from 'node:assert/strict';
import { readdir } from 'node:fs/promises';
import configuration from './public/main.js';

const assets = new URL('../_site/public/', import.meta.url);
const engine = (await readdir(assets)).find(name => /^es-.*\.min\.js$/.test(name));
assert.ok(engine, 'Build the DocFX website first.');
const { default: hljs } = await import(new URL(engine, assets));
configuration.configureHljs(hljs);
for (const language of ['raven', 'rav', 'rvn']) {
  for (const source of ['func `Calculate total price`() {}', 'let `if /* comment */` = 1', 'let @if = 1', 'object.@class()']) {
    const html = hljs.highlight(source, { language }).value;
    assert.match(html, /hljs-(?:variable|title function_)/);
    assert.doesNotMatch(html, /hljs-keyword">(?:if|class)</);
    assert.doesNotMatch(html, /hljs-comment/);
  }
}
console.log('CloudShell DocFX Raven highlighting passed.');
