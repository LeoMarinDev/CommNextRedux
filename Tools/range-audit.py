#!/usr/bin/env python3
"""pre-launch audit: my Lua targets vs the game's own pre-patch part definitions.

Reads the ranges and the per-part EC rates straight out of the two shipped Lua files
(so the audit cannot drift from what was deployed), then compares them against
$KSP2_ROOT/pm_cache/parts_data.zip, which PatchManager dumps from the game BEFORE any
patch runs. That makes every number below a pre-patch fact, not a memory.

Re-create the input before running:

    KSP2_ROOT="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program 2"
    rm -rf /tmp/pmparts && mkdir /tmp/pmparts && cd /tmp/pmparts
    unzip -q "$KSP2_ROOT/pm_cache/parts_data.zip"          # 426 files, one JSON per part id
    cd /home/user/repositories/KSP2-Redux-Mods/mods/CommNextRedux
    python3 Tools/p4-range-audit.py | tee Deploy/obj/p4-range-audit.txt

Caveat measured in P4: `pm_cache/parts_data.zip` is written back after patching, so it is
post-patch state from the *previous* launch. For the parts audited here that equals stock -
no loaded mod patches `CommunicationRange` (proved by `rg -l 'CommunicationRange'
$KSP2_ROOT/Mods/*/patches/*.lua`, which returns this mod's two files and nothing else), and
the OrbitalSurvey entries present in the cache touch experiments, not ranges.
"""
import json
import os
import re
import sys

ROOT = os.path.expanduser('~/.local/share/Steam/steamapps/common/Kerbal Space Program 2')
PARTS = "/tmp/pmparts"   # unzip of $KSP2_ROOT/pm_cache/parts_data.zip; see the README line in this file
LUA = '/home/user/repositories/KSP2-Redux-Mods/mods/CommNextRedux/Assets/CommNextRedux/Copied/patches'


def parse_lua(path):
    """Pull the {id=..., range=..., ec=..., kind=...} rows out of a patch table."""
    rows = []
    for line in open(path):
        m = re.search(r'\{\s*id\s*=\s*"([^"]+)"\s*,\s*range\s*=\s*([0-9.eE+-]+)'
                      r'(?:\s*,\s*ec\s*=\s*([0-9.eE+-]+))?\s*,\s*kind\s*=\s*"([^"]+)"', line)
        if m:
            rows.append({'id': m.group(1), 'range': float(m.group(2)),
                         'ec': float(m.group(3)) if m.group(3) else None, 'kind': m.group(4)})
    return rows


def modules_of(part):
    with open(os.path.join(PARTS, part)) as fh:
        return json.load(fh)['data']


def stock_range(data):
    """The transmitter's CommunicationRange in the pre-patch definition."""
    for m in data.get('serializedPartModules') or []:
        for md in m.get('ModuleData') or []:
            if md.get('Name') == 'Data_Transmitter':
                obj = md.get('DataObject') or {}
                return obj.get('CommunicationRange')
    return None


def has_module(data, name):
    """Match a module by its COMPONENT name (`PartComponentModule_*`) or its BEHAVIOUR
    name (`Module_*`) - the two live in different columns of the same entry, and matching
    only one of them silently reports 0 for everything (measured: this script's first
    version matched ComponentType strings against BehaviourType and counted 0/426)."""
    for m in data.get('serializedPartModules') or []:
        comp = (m.get('Name') or '').strip()
        beh = (m.get('BehaviourType') or '').split(',')[0].rsplit('.', 1)[-1].strip()
        if name in (comp, beh):
            return True
    return False


antennas = parse_lua(os.path.join(LUA, 'commnext_antennas.lua'))
relays = parse_lua(os.path.join(LUA, 'commnext_relays.lua'))

print('=' * 78)
print('P4 RANGE AUDIT - the Lua tables against the game\'s pre-patch definitions')
print('=' * 78)
print()
print('-- the four relays (commnext_relays.lua) --')
print(f"{'part id':32} {'stock range':>18} {'patched range':>18}  ec   kind       mod already?")
for r in relays:
    d = modules_of(r['id'])
    s = stock_range(d)
    already = has_module(d, 'Module_NextRelay') or has_module(d, 'Module_NextModulator')
    print(f"{r['id']:32} {s:>18,.1f} {r['range']:>18,.1f}  {r['ec']:<4} {r['kind']:<10} {already}")
print()
print('-- the five named antennas (commnext_antennas.lua) --')
print(f"{'part id':32} {'stock range':>18} {'patched range':>18}  kind       mod already?")
for a in antennas:
    d = modules_of(a['id'])
    s = stock_range(d)
    already = has_module(d, 'Module_NextRelay') or has_module(d, 'Module_NextModulator')
    print(f"{a['id']:32} {s:>18,.1f} {a['range']:>18,.1f}  {a['kind']:<10} {already}")

print()
print('-- blanket rule: parts carrying BOTH a transmitter and a command module --')
tx, cmd, both, already = [], [], [], 0
for name in sorted(os.listdir(PARTS)):
    d = modules_of(name)
    t = has_module(d, 'PartComponentModule_DataTransmitter')
    c = has_module(d, 'PartComponentModule_Command')
    if t:
        tx.append(name)
    if c:
        cmd.append(name)
    if t and c:
        both.append(name)
    if t and (has_module(d, 'Module_NextRelay') or has_module(d, 'Module_NextModulator')):
        already += 1
print(f"parts total                : {len(os.listdir(PARTS))}")
print(f"with a transmitter         : {len(tx)}")
print(f"with a command module      : {len(cmd)}")
print(f"with BOTH (the blanket set): {len(both)}")
print(f"  stock pods in that set   : {sum(1 for n in both if n.startswith('pod_'))}")
print(f"  stock probes in it       : {sum(1 for n in both if n.startswith('probe_'))}")
print(f"  stock cockpits in it     : {sum(1 for n in both if n.startswith('cockpit_'))}")
print()
print('  BOTH-set sample + its stock range (the blanket rule resets these to 5000.0):')
for n in both[:6]:
    d = modules_of(n)
    print(f"    {n:44} {stock_range(d)!r}")
print()
print('-- control: does any stock part already ship CommNextRedux\'s modules? --')
print(f"transmitters already carrying Module_NextRelay/Module_NextModulator: {already}")
print('   (if 0, every such module after launch can only have come from the Lua patch)')
print()
print('-- control: does the PAM visuals field already exist on the targets? --')
for r in [x['id'] for x in relays] + [x['id'] for x in antennas]:
    d = modules_of(r)
    v = d.get('PAMModuleVisualsOverride')
    print(f"  {r:32} PAMModuleVisualsOverride={v!r}")
