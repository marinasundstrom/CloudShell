let loading;
let users = 0;
let observer;
const script = path => new Promise((resolve, reject) => {
    const element = document.createElement('script');
    element.src = new URL(path, document.baseURI).href;
    element.onload = resolve;
    element.onerror = () => { element.remove(); reject(new Error('Editor assets unavailable')); };
    document.head.appendChild(element);
});
function applyTheme() {
    const dark = document.body.dataset.theme === 'dark' ||
        (!document.body.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches);
    monaco.editor.setTheme(dark ? 'vs-dark' : 'vs');
}
export async function initialize() {
    loading ??= (async () => {
        await script('_content/BlazorMonaco/jsInterop.js');
        await script('_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js');
        await new Promise((resolve, reject) => require(['vs/editor/editor.main'], resolve, reject));
        monaco.languages.json.jsonDefaults.setDiagnosticsOptions({ validate: true, allowComments: false, enableSchemaRequest: false });
    })().catch(error => { loading = undefined; throw error; });
    await loading;
    users++;
    applyTheme();
    if (!observer) {
        observer = new MutationObserver(applyTheme);
        observer.observe(document.body, { attributes: true, attributeFilter: ['data-theme'] });
    }
}
export function release() {
    users = Math.max(0, users - 1);
    if (!users) { observer?.disconnect(); observer = undefined; }
}
