"""Save/restore only temporary trailer settings and the separate capture plugin."""
import json
from pathlib import Path
import re
import shutil
import sys

repo = Path(__file__).resolve().parents[1]
work = repo / '.nomodkit/trailer-production'
game = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option')
cfg = game / 'BepInEx/config/com.marci.boscalisummer.cfg'
plugin = game / 'BepInEx/plugins/TrailerCapture/TrailerCapture.dll'
state_file = work / 'temporary-settings.json'
pattern = r'(?ms)^\[Cinematography\]\r?\n.*?(?=^\[|\Z)'

if sys.argv[1] == 'enable':
    if state_file.exists():
        raise SystemExit('Temporary-settings record exists; restore it first')
    text = cfg.read_text(encoding='utf-8-sig')
    match = re.search(pattern, text)
    original = match.group() if match else ''
    enabled = original or '[Cinematography]\n\n'
    for key in ['Enabled', 'PrivateProductionSession', 'AllowSimulationSlowMotion', 'EnableScriptInbox']:
        if re.search(rf'(?m)^{key} = .*$', enabled):
            enabled = re.sub(rf'(?m)^{key} = .*$', key + ' = true', enabled)
        else:
            enabled += key + ' = true\n'
    if plugin.exists():
        raise SystemExit('Existing TrailerCapture plugin preserved; choose another production directory')
    work.mkdir(parents=True, exist_ok=True)
    state_file.write_text(json.dumps(dict(original=original, enabled=enabled), indent=2), encoding='utf-8')
    cfg.write_text(text.replace(original, enabled, 1) if original else text+'\n'+enabled, encoding='utf-8')
    plugin.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(repo / 'tools/TrailerCapture/bin/Release/netstandard2.1/TrailerCapture.dll', plugin)
    print('Temporary cinematics settings enabled; separate capture DLL installed')
else:
    state = json.loads(state_file.read_text(encoding='utf-8'))
    text = cfg.read_text(encoding='utf-8-sig')
    match = re.search(pattern, text)
    current = match.group() if match else ''
    # BepInEx can regenerate comments/order, so restore just the four owned values.
    for key in ['Enabled', 'PrivateProductionSession', 'AllowSimulationSlowMotion', 'EnableScriptInbox']:
        before = re.search(rf'(?m)^{key} = (.*)$', state['original'])
        now = re.search(rf'(?m)^{key} = (.*)$', current)
        if now and now.group(1).strip() != 'true':
            print(f'Concurrent setting preserved: {key}')
            continue
        value = before.group(1).strip() if before else 'false'
        current = re.sub(rf'(?m)^{key} = .*$', key+' = '+value, current)
    cfg.write_text(text[:match.start()]+current+text[match.end():], encoding='utf-8')
    plugin.unlink(missing_ok=True)
    state_file.rename(work / 'temporary-settings-restored.json')
    print('Owned settings restored; capture DLL removed')
