"""Copy only the native frigate's transitive placeholder dependencies into our scratch Unity project."""
import re, sys, shutil, json
from pathlib import Path
source=Path(sys.argv[1]).resolve(); target=Path(sys.argv[2]).resolve()
index={}
for meta in source.rglob('*.meta'):
    match=re.search(r'^guid: ([a-f0-9]{32})$',meta.read_text(errors='replace'),re.M)
    if match: index[match[1]]=Path(str(meta)[:-5])
todo=[source/'GameObject/Frigate1_PLACEHOLDER.prefab',source/'MonoBehaviour/Frigate1_PLACEHOLDER.asset']
seen=set()
while todo:
    path=todo.pop()
    if path in seen or not path.is_file(): continue
    seen.add(path)
    dest=target/path.relative_to(source); dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,dest)
    if Path(str(path)+'.meta').exists(): shutil.copy2(str(path)+'.meta',str(dest)+'.meta')
    with path.open('rb') as stream: yaml=stream.read(5)==b'%YAML'
    if yaml:
        for guid in re.findall(r'guid: ([a-f0-9]{32})',path.read_text(errors='replace')):
            if guid in index: todo.append(index[guid])
print('COPIED',len(seen),'native placeholders;',sum(p.stat().st_size for p in seen),'bytes')
(target.parent/'ship-donor-closure.json').write_text(json.dumps([str(p.relative_to(source)).replace('\\','/') for p in sorted(seen)],indent=2))
