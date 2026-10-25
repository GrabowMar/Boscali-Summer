// Run after Build-CodeAtlas.ps1: node tools/CodeAtlas/Verify-CodeAtlas.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const output = path.resolve(process.argv[2] || path.join(__dirname, '../../reports/code-atlas'));
const data = JSON.parse(fs.readFileSync(path.join(output, 'atlas-data.json'), 'utf8'));
const modules = new Map(data.modules.map(item => [item.id, item]));
const files = new Map(data.files.map(item => [item.id, item]));
assert.equal(modules.size, data.modules.length, 'Unique component IDs');
assert.equal(files.size, data.files.length, 'Unique source file IDs');
assert.equal(data.meta.sourceFiles, files.size, 'Source file total');
assert.equal(data.meta.sourceLines, data.files.reduce((sum, file) => sum + file.lines, 0), 'Line total');
assert.equal(data.meta.typeCount, data.files.reduce((sum, file) => sum + file.typeCount, 0), 'Type total');
for (const module of modules.values()) {
    const members = data.files.filter(file => file.module === module.id);
    assert.equal(module.files, members.length, `${module.id}: file count`);
    assert.equal(module.lines, members.reduce((sum, file) => sum + file.lines, 0), `${module.id}: lines`);
    assert.equal(module.types, members.reduce((sum, file) => sum + file.typeCount, 0), `${module.id}: types`);
}
for (const file of files.values()) {
    assert(modules.has(file.module), `${file.id}: component exists`);
    assert(!file.id.includes('Assets/Editor/') && !file.id.startsWith('tests/'), `${file.id}: project exclusion`);
    assert(!file.id.includes('\\'), `${file.id}: normalized source path`);
}
const aggregated = new Map();
const filePairs = new Set();
for (const edge of data.fileEdges) {
    assert(files.has(edge.source) && files.has(edge.target), 'File edge endpoints exist');
    assert(edge.source !== edge.target && edge.weight > 0, 'File edges are positive, directed cross-file references');
    const pair = JSON.stringify([edge.source, edge.target]);
    assert(!filePairs.has(pair), 'File edges are unique');
    filePairs.add(pair);
    const sourceModule = files.get(edge.source).module;
    const targetModule = files.get(edge.target).module;
    if (sourceModule !== targetModule) {
        const key = JSON.stringify([sourceModule, targetModule]);
        aggregated.set(key, (aggregated.get(key) || 0) + edge.weight);
    }
}
const modulePairs = new Set();
for (const edge of data.edges) {
    const pair = JSON.stringify([edge.source, edge.target]);
    assert(!modulePairs.has(pair), 'Component edges are unique');
    modulePairs.add(pair);
    assert(modules.has(edge.source) && modules.has(edge.target), 'Component edge endpoints exist');
    assert(edge.source !== edge.target && edge.weight > 0, 'Component edges are positive');
    assert.equal(edge.weight, aggregated.get(pair), `${pair}: sum of file reference weights`);
    assert(edge.evidence.length > 0 && edge.evidence.length <= 8, 'Couplings have bounded source evidence');
    for (const evidence of edge.evidence) {
        assert.equal(files.get(evidence.file)?.module, edge.source, 'Evidence source component');
        assert.equal(files.get(evidence.targetFile)?.module, edge.target, 'Evidence target component');
        assert(evidence.line > 0 && evidence.targetLine > 0 && evidence.symbol, 'Evidence has source locations and symbol');
    }
}
assert.equal(modulePairs.size, aggregated.size, 'All cross-component links are represented');
console.log(`PASS: ${modules.size} components, ${files.size} files, ${data.edges.length} component links, ${filePairs.size} file links.`);
