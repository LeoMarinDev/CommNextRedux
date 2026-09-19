#!/usr/bin/env bash
#
# Builds CommNextRedux.dll against the EXACT managed assemblies of an installed
# KSP2 Redux runtime, instead of the Unity/ThunderKit editor pipeline.
#
# Why this exists: the ThunderKit editor pipeline builds against whatever Unity SDK
# generation the project's package refs resolve to, which is how a pre-Redux-era mod
# binary came to fail on Redux 0.2.8.5.103184 (Unity 6000.4.1f1) with TypeLoadException
# on UnityEngine.UIElements.PanelRenderer. This script compiles the same sources against
# the runtime DLLs that actually ship with the game, which is the only way to guarantee
# the compiler sees the same API surface the loader will.
#
# It never modifies anything inside the game installation: the managed DLLs are
# copied (read-only source) into Packages/KSP2_x64, which is the staging directory
# this repository already assumes (see the /[Pp]ackages/KSP2_x64 entry in .gitignore).
#
# PROVENANCE OF THIS FILE
# -----------------------
# Copied out of mods/FlightPlanRedux/Tools/build.sh and re-authored for
# CommNextRedux. That file is itself the de-hardcoded transplant of
# mods/K2D2Redux/Tools/build.sh (K2D2's tree is hardcoded to K2D2.dll and
# Assets/K2D2 throughout; FlightPlan's is not). Both donors are read-only.
#
# Every gate the donor performed is kept. The mechanical part of the retarget was an
# ordered identifier rename (FLIGHTPLAN->COMMNEXTREDUX, FlightPlan->CommNextRedux,
# flightplan->commnextredux, "Flight Plan"->"CommNext Redux"); the prose and the
# provenance block above are hand-authored, and the table of what came from where is in
# Deploy/obj/PORT-PROGRESS.md.
#
# Usage:
#   Tools/build.sh
#   KSP2="/path/to/Kerbal Space Program 2" Tools/build.sh
#   Tools/build.sh --print-compiler          # report the selected compiler route, then exit
#   COMMNEXT_CSC=dotnet Tools/build.sh       # explicit compiler override (see section 3)
#
# Output:
#   Packages/KSP2_x64/     staged reference assemblies (gitignored)
#   Deploy/CommNextRedux/  deployable mod folder: CommNextRedux.dll + swinfo.json + the
#                          COMPLETE payload set (assets/ + localizations/ + patches/), which
#                          is the same file set the live install carries. P9 fixed this: from
#                          P1 to P8 only assets/ was staged, so the tree was missing both Lua
#                          patches and the localization CSV - harmless while every deploy was
#                          an explicit per-file copy, fatal for a release archive built from
#                          this tree.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

KSP2="${KSP2:-$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program 2}"
MANAGED="$KSP2/KSP2_x64_Data/Managed"
STAGING="$REPO_ROOT/Packages/KSP2_x64"
OUT_DIR="$REPO_ROOT/Deploy/CommNextRedux"
OBJ_DIR="$REPO_ROOT/Deploy/obj"
OUT_DLL="$OBJ_DIR/CommNextRedux.dll"

log()  { printf '\033[1;34m[build]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[warn]\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

# ---------------------------------------------------------------------------
# 1. Sanity-check the runtime we are about to build against
# ---------------------------------------------------------------------------
[ -d "$MANAGED" ] || die "Managed assemblies not found at: $MANAGED
Set KSP2=/path/to/Kerbal Space Program 2 and re-run."

for required in \
    Assembly-CSharp.dll \
    ReduxLib.dll \
    SpaceWarp2.dll \
    SpaceWarp2.UI.dll \
    UitkForKsp2.dll \
    UnityEngine.UIElementsModule.dll \
    UnityEngine.CoreModule.dll \
    Unity.Scripting.dll
do
    [ -f "$MANAGED/$required" ] || die "Missing required runtime assembly: $MANAGED/$required"
done

# The 0.2.9.0.104521 runtime ships exactly 220 DLLs. The count is a gate, not a note: a partial
# Managed/ makes every monodis answer below read exactly like "the member does not exist" (a
# completeness assertion can pass on a set that is simply incomplete), and step 2 would copy that
# partial set in as the compile reference set.
DLL_COUNT="$(find "$MANAGED" -maxdepth 1 -name '*.dll' | wc -l)"
[ "$DLL_COUNT" -eq 220 ] || die "Managed/ holds $DLL_COUNT DLL(s); Redux 0.2.9.0.104521 ships 220.
This is not the runtime this build targets - re-check KSP2 and the install, then re-run."

# Cache the type/method dumps once. (Captured into variables rather than piped straight into
# `grep -q`: grep -q exits on first match, which SIGPIPEs monodis, and with `pipefail` enabled that
# reads as a failed pipeline - which would invert these checks.)
UIE_TYPEDEFS="$(monodis --typedef "$MANAGED/UnityEngine.UIElementsModule.dll" 2>/dev/null || true)"
# --method is one of monodis's row-printing flags: on an assembly whose signature rows reference
# assemblies it cannot load it replaces the row with `failed to parse` and still exits 0, so the
# dump is taken with MONO_PATH pointing at Managed/. A per-command prefix, never exported - an
# exported MONO_PATH changes which mscorlib mono resolves and poisons every other tool.
UITK_METHODS="$(MONO_PATH="$MANAGED" monodis --method "$MANAGED/UitkForKsp2.dll" 2>/dev/null || true)"

# ADDED IN THE CommNextRedux TRANSPLANT. Both dumps above swallow failure with `|| true`, so an
# unreadable assembly makes every check below a silent no-op: an empty variable matches nothing,
# and the generation assertions `die` for the wrong reason. Assert the dumps are non-empty so a
# broken monodis/assembly reads as an error instead of as a clean runtime. A real dump is thousands
# of lines; 100 is a floor, not a threshold.
for pair in "UIE_TYPEDEFS:$UIE_TYPEDEFS" "UITK_METHODS:$UITK_METHODS"; do
    name="${pair%%:*}"
    lines=$(printf '%s\n' "${pair#*:}" | wc -l)
    [ "$lines" -ge 100 ] || die "$name came back with $lines line(s) - monodis could not read the
runtime, so the runtime sanity checks below cannot be trusted. Refusing to compile against a
runtime that cannot be inspected."
done

# The runtime must be the exact 0.2.9.0.104521 generation this update targets:
# UnityEngine.UIElementsModule.dll defines PanelRenderer, and BOTH
# UitkForKsp2.API.Window.Create overloads return it. The superseded 0.2.8.5 runtime is the exact
# inverse (no PanelRenderer; Window.Create returns UIDocument), so a wrong $KSP2 is refused here
# instead of producing a plausible DLL.
#
# INVERTED AT THE 0.2.9.0.104521 UPDATE. The block that stood here was written for the superseded
# pin: it warned when the concrete PanelRenderer was found and `die`d unless `UIDocument Create`
# was present - i.e. it refused THIS pin's own runtime. Its anchored-pattern lesson is kept:
# monodis --typedef prints
#   <row>: <Full.Type.Name> (flist=..., mlist=..., flags=..., extends=...)
# so a `$`-anchored name pattern can never match, and the only typedef whose name *contains*
# PanelRenderer on this runtime is the interface `UnityEngine.UIElements.IPanelRenderer` - which is
# why the assertions below match the real row shape with a trailing ` (`, not a bare substring.
#
# The load-bearing check is not these greps alone: step 5a asserts the BUILT assembly's typeref
# table too, so the two halves together refuse both directions of the wrong binding.
#
# Kept as a function so two-sided controls can drive the same code with synthetic dumps: the real
# runtime must pass, and each superseded shape must be refused.
assert_runtime_generation() {
    local uie_typedefs="$1" uitk_methods="$2"

    # Completeness first: --method exits 0 even when its rows did not resolve, so a count read from
    # a broken dump is not evidence of anything.
    [ "$(grep -c 'failed to parse' <<< "$uitk_methods" || true)" = "0" ] \
        || die "monodis --method could not resolve UitkForKsp2.dll's signatures.
Pass MONO_PATH as a per-command prefix (never exported) and re-run."

    # Exactly one row of the real shape: `NNN: UnityEngine.UIElements.PanelRenderer (flist=...)`.
    [ "$(grep -cE '^[0-9]+: UnityEngine\.UIElements\.PanelRenderer \(' <<< "$uie_typedefs" || true)" = "1" ] \
        || die "UnityEngine.UIElementsModule.dll does not define exactly one UnityEngine.UIElements.PanelRenderer.
This is not the Redux 0.2.9.0.104521 runtime this build targets."

    # Both documented Window.Create overloads (options+root, options+uxml) return PanelRenderer here.
    [ "$(grep -c 'PanelRenderer Create' <<< "$uitk_methods" || true)" = "2" ] \
        || die "UitkForKsp2.dll does not expose both Window.Create(...) -> PanelRenderer overloads.
This is not the Redux 0.2.9.0.104521 runtime this build targets."

    if grep -q 'UIDocument Create' <<< "$uitk_methods"; then
        die "UitkForKsp2.dll exposes Window.Create(...) -> UIDocument: the superseded 0.2.8.5 shape.
This build targets Redux 0.2.9.0.104521, where Window.Create returns PanelRenderer."
    fi
}
assert_runtime_generation "$UIE_TYPEDEFS" "$UITK_METHODS"

GAME_VERSION="$(strings "$MANAGED/../globalgamemanagers" 2>/dev/null \
    | grep -m1 -E '^6000\.[0-9]+\.[0-9]+' || true)"
log "Runtime: ${GAME_VERSION:-unknown Unity version}  ($MANAGED)"

# ---------------------------------------------------------------------------
# 2. Stage every managed DLL as a reference (copy only, never modify source)
# ---------------------------------------------------------------------------
# $STAGING is long-lived (gitignored) and used to accumulate whichever generation was staged last:
# measured during this update it held 247 DLLs - the 220 shipped by 0.2.9.0.104521 plus 27 stale
# names (glTFast*, nunit.framework, UnityEngine.TestRunner, ...) with no source in Managed/. Since
# every staged DLL becomes a `-r:` entry, that set silently let the compile resolve against
# assemblies this pin does not ship, i.e. it was weaker than the header claims. Clear it first,
# then assert the result instead of trusting the copy: names subset of Managed/, count equal, every
# file byte-identical (md5 diff). Kept as a function so two-sided controls can drive the same code
# on synthetic trees.
stage_runtime() {
    local staging="$1" managed="$2"
    local staged_count managed_count stale=""
    mkdir -p "$staging"
    rm -f "$staging"/*.dll
    cp -f "$managed"/*.dll "$staging"/
    staged_count="$(find "$staging" -maxdepth 1 -name '*.dll' | wc -l)"
    managed_count="$(find "$managed" -maxdepth 1 -name '*.dll' | wc -l)"
    for staged in "$staging"/*.dll; do
        [ -f "$managed/$(basename "$staged")" ] || stale="$stale $(basename "$staged")"
    done
    [ -z "$stale" ] || die "Staged assemblies with no source in Managed/:$stale
Refusing to compile against a set that carries assemblies this pin does not ship."
    [ "$staged_count" = "$managed_count" ] \
        || die "Staged $staged_count assemblies but Managed/ holds $managed_count - refusing the set."
    if ! diff <(cd "$staging" && md5sum *.dll | LC_ALL=C sort) \
              <(cd "$managed" && md5sum *.dll | LC_ALL=C sort) >/dev/null; then
        die "Staged assemblies are not byte-identical to their Managed/ sources (md5 mismatch)."
    fi
    log "Staging verified: $staged_count assemblies, all present in Managed/, byte-identical (md5)."
}

mkdir -p "$OBJ_DIR" "$OUT_DIR"
log "Staging managed assemblies into Packages/KSP2_x64 ..."
stage_runtime "$STAGING" "$MANAGED"

# ---------------------------------------------------------------------------
# 3. Locate a C# compiler
# ---------------------------------------------------------------------------
# PRIMARY route: the Roslyn csc.exe bundled with Proton's wine-mono, run under mono - the compiler
# this mod's certified builds were compiled with (Roslyn 3.9, which is why C# 9 is the ceiling;
# -langversion pins that assumption instead of leaving it implicit).
#
# The .NET SDK's Roslyn is NEVER auto-selected. /usr/bin/dotnet 10.0.112 was found live on PATH
# during the 0.2.9.0 update; the preference order that stood here picked it silently, and the loader
# pre-flight (5d, below) then could not build its probe under the dotnet route and was skipped while
# the build still exited 0. A system SDK is still reachable, but only by asking:
#   COMMNEXT_CSC=dotnet     - a .NET SDK's csc.dll, run under dotnet
#   COMMNEXT_CSC=/abs/path  - an explicit compiler (.dll run under dotnet, anything else under mono)
find_csc() {
    if [ -n "${COMMNEXT_CSC:-}" ]; then
        case "$COMMNEXT_CSC" in
            dotnet)
                local sdk_dll
                sdk_dll="$(find /usr/share/dotnet /usr/lib/dotnet "$HOME/.dotnet" \
                    -path '*/Roslyn/bincore/csc.dll' 2>/dev/null | sort | tail -1 || true)"
                [ -n "$sdk_dll" ] || return 1
                echo "dotnet:$sdk_dll"
                ;;
            /*.dll) echo "dotnet:$COMMNEXT_CSC" ;;
            /*)     echo "mono:$COMMNEXT_CSC" ;;
            *)      return 1 ;;
        esac
        return 0
    fi
    command -v mono >/dev/null 2>&1 || return 1
    local proton_csc
    proton_csc="$(find "$HOME/.local/share/Steam/steamapps/common" \
        -path '*wine/mono*Roslyn/csc.exe' 2>/dev/null | sort | tail -1 || true)"
    [ -n "$proton_csc" ] || return 1
    echo "mono:$proton_csc"
}

CSC_INFO="$(find_csc || true)"
[ -n "$CSC_INFO" ] || die "No C# compiler found. Set COMMNEXT_CSC, or install the
Mono/Proton toolchain that ships Roslyn csc.exe."
CSC_KIND="${CSC_INFO%%:*}"
CSC_PATH="${CSC_INFO#*:}"

run_csc() {
    case "$CSC_KIND" in
        dotnet) dotnet "$CSC_PATH" "$@" ;;
        mono)   mono "$CSC_PATH" "$@" ;;
    esac
}
CSC_VERSION="$(run_csc -version 2>/dev/null | head -1 || true)"
log "Compiler: $CSC_KIND ($CSC_PATH) version ${CSC_VERSION:-unknown}"

# Report the selection without compiling - the two-sided control for the compiler-hazard fix (the
# primary route must choose the wine-mono Roslyn even with a PATH dotnet present, and an explicit
# COMMNEXT_CSC override must still be honoured).
if [ "${1:-}" = "--print-compiler" ]; then
    if [ -n "${COMMNEXT_CSC:-}" ]; then
        printf 'compiler route: COMMNEXT_CSC override %s -> %s\n' "$COMMNEXT_CSC" "$CSC_KIND"
    else
        printf 'compiler route: auto - Wine-Mono Roslyn under mono (a PATH dotnet is never auto-selected)\n'
    fi
    exit 0
fi

# ---------------------------------------------------------------------------
# 4. Build the reference response file
# ---------------------------------------------------------------------------
RSP="$OBJ_DIR/csc.rsp"
: > "$RSP"

{
    echo "-target:library"
    echo "-unsafe"
    echo "-nostdlib+"
    echo "-langversion:9.0"
    echo "-optimize+"
    echo "-nologo"
    # CS0618: deprecated UxmlTraits/base Init members are intentionally used by
    # custom UI Toolkit controls against this old UI Toolkit; CS0649/0169/0414 are
    # serialization-shaped warnings that do not affect a mod assembly.
    echo "-nowarn:0618,0612,0672,0169,0649,0414"
    echo "-out:\"$OUT_DLL\""
} >> "$RSP"

# Reference every staged DLL: the asmdef's precompiledReferences list is a
# hand-maintained subset that does not always match this runtime (the sibling project
# this was transplanted from named DLLs that are not installed here at all).
# Compiling against the complete installed set is both simpler and stricter: any API
# that is not in the shipped 0.2.9.0.104521 runtime then fails to resolve at compile time.
for dll in "$STAGING"/*.dll; do
    echo "-r:\"$dll\"" >> "$RSP"
done

# Only the mod's own code; the editor-only assembly is not part of the runtime
# mod and is skipped (it references UnityEditor, which the game does not ship).
while IFS= read -r src; do
    echo "\"$src\"" >> "$RSP"
done < <(find Assets/CommNextRedux/Code -name '*.cs' | sort)

log "Compiling $(find Assets/CommNextRedux/Code -name '*.cs' | wc -l) source files ..."
set +e
COMPILE_LOG="$OBJ_DIR/compile.log"
run_csc "@$RSP" > "$COMPILE_LOG" 2>&1
COMPILE_STATUS=$?
set -e

if [ $COMPILE_STATUS -ne 0 ]; then
    cat "$COMPILE_LOG" >&2
    die "Compilation failed (see $COMPILE_LOG)."
fi
grep -E 'warning' "$COMPILE_LOG" || true
[ -f "$OUT_DLL" ] || die "Compiler reported success but $OUT_DLL does not exist."

# ---------------------------------------------------------------------------
# 5. Verify the built assembly against the runtime we compiled against
# ---------------------------------------------------------------------------
log "Verifying $OUT_DLL ..."
FAILED=0

TYPEREFS="$(monodis --typeref "$OUT_DLL" 2>/dev/null || true)"
[ -n "$TYPEREFS" ] || die "monodis could not read $OUT_DLL."

# 5a. The built assembly's window-API generation. INVERTED at 0.2.9.0.104521. The superseded gate
#     here failed a build for carrying a PanelRenderer reference: against 0.2.8.5 that type did not
#     exist (the original TypeLoadException). At this pin UitkForKsp2.API.Window.Create RETURNS
#     PanelRenderer, so a build bound to this pin's window API MUST carry the typeref, and a
#     UIDocument typeref means csc compiled against the superseded UitkForKsp2.dll.
#     Kept as a function so two-sided controls can drive the same code with real DLLs: this pin's
#     build must pass it, and the port's superseded 0.2.8.5 DLL must fail it.
assert_window_api_binding() {
    local typerefs="$1"
    if ! grep -q 'PanelRenderer' <<< "$typerefs"; then
        warn "No PanelRenderer typeref in the built assembly - it does not bind this pin's Window.Create."
        FAILED=1
    else
        log "PanelRenderer typeref present - bound to the 0.2.9.0.104521 window API."
    fi
    if grep -q 'UIDocument' <<< "$typerefs"; then
        grep -n 'UIDocument' <<< "$typerefs" >&2
        warn "Found a UIDocument typeref - the built assembly is bound to the superseded 0.2.8.5 window API."
        FAILED=1
    fi
}
assert_window_api_binding "$TYPEREFS"

# 5b. Preserve must resolve to UnityEngine.CoreModule, not Unity.Scripting
#     (the assembly Unity.Scripting does not export PreserveAttribute).
if grep -q 'PreserveAttribute' <<< "$TYPEREFS"; then
    # monodis --typeref prints "<index>: [AssemblyScope]Namespace.Type"; the assembly scope is what
    # matters here, so strip the index and keep the bracketed name.
    PRESERVE_SCOPE="$(grep -m1 'PreserveAttribute' <<< "$TYPEREFS" \
        | sed -n 's/^[0-9]*: *\[\([^]]*\)\].*/\1/p')"
    log "PreserveAttribute typeref scope: [$PRESERVE_SCOPE]"
    case "$PRESERVE_SCOPE" in
        UnityEngine.CoreModule) : ;;
        *) warn "PreserveAttribute is bound to [$PRESERVE_SCOPE]; expected UnityEngine.CoreModule."
           FAILED=1 ;;
    esac
else
    log "No PreserveAttribute typeref in the assembly."
fi

# 5c. Every assembly reference must exist in the staged runtime.
MISSING_REFS=""
while IFS= read -r ref; do
    [ -n "$ref" ] || continue
    [ -f "$STAGING/$ref.dll" ] || MISSING_REFS="$MISSING_REFS $ref"
done < <(monodis --assemblyref "$OUT_DLL" 2>/dev/null | sed -n 's/^[[:space:]]*Name=\(.*\)$/\1/p')
if [ -n "$MISSING_REFS" ]; then
    warn "Referenced assemblies not present in the runtime:$MISSING_REFS"
    FAILED=1
else
    log "All assembly references resolve against the staged 0.2.9.0.104521 runtime."
fi

[ $FAILED -eq 0 ] || die "Verification failed - refusing to package this build."

# 5d. Loader pre-flight: force resolution of every type/field/property/method against the same
#     runtime assemblies, which is what the game's loader does when it registers the plugin. This
#     catches anything the typeref greps above do not name (e.g. an interface that only exists in
#     the newer Redux snapshots).
#
#     It runs through the SAME csc entry point as the mod assembly, so switching compiler kind
#     cannot make it fall through unrun. On the primary route (mono) the toolchain that runs the
#     pre-flight IS the selected toolchain, so a failure to build it is fatal - a green build must
#     not be able to mean "the pre-flight did not run". Only on the opt-in dotnet route is a
#     missing mono reported as NOT RUN, because mono is not part of that route.
PROBE_SRC="$REPO_ROOT/Tools/ApiProbe.cs"
probe_required=0
[ "$CSC_KIND" = "mono" ] && probe_required=1
if command -v mono >/dev/null 2>&1 && [ -f "$PROBE_SRC" ]; then
    PROBE_LIB="$OBJ_DIR/probe-lib"
    rm -rf "$PROBE_LIB"; mkdir -p "$PROBE_LIB"

    # Copy the staged runtime, minus everything Mono's own BCL provides - otherwise Mono picks up
    # the game's mscorlib and refuses to run ("your mono runtime and class libraries are out of sync").
    cp -f "$STAGING"/*.dll "$PROBE_LIB"/
    if [ -d /usr/lib/mono/4.5 ]; then
        ls /usr/lib/mono/4.5/*.dll 2>/dev/null | xargs -r -n1 basename | sed 's/\.dll$//' | sort -u > "$OBJ_DIR/bcl-names.txt"
    fi
    if [ -s "$OBJ_DIR/bcl-names.txt" ]; then
        ( cd "$PROBE_LIB"
          for f in *.dll; do
              # Written as an if/else rather than `grep && rm` so a non-match cannot trip `set -e`.
              if grep -qxF "${f%.dll}" "$OBJ_DIR/bcl-names.txt"; then
                  rm -f "$f"
              fi
          done )
    fi
    rm -f "$PROBE_LIB/mscorlib.dll" "$PROBE_LIB/netstandard.dll"
    cp -f "$OUT_DLL" "$PROBE_LIB/CommNextRedux.dll"

    # The probe itself must compile against Mono's BCL, not the game's: dropping -nostdlib lets csc.exe
    # (running under Mono) resolve mscorlib/System from Mono's own 4.5 profile.
    if run_csc -nologo -out:"$OBJ_DIR/probe.exe" \
            -r:System.dll -r:System.Core.dll "$PROBE_SRC" > "$OBJ_DIR/probe-build.log" 2>&1; then
        if MONO_PATH="$PROBE_LIB" mono "$OBJ_DIR/probe.exe" "$PROBE_LIB/CommNextRedux.dll"; then
            log "Loader pre-flight passed."
        else
            die "Loader pre-flight failed - the runtime cannot resolve this assembly's type graph."
        fi
    elif [ $probe_required -eq 1 ]; then
        die "Could not build the loader pre-flight probe under the selected mono compiler - refusing
to report a successful build with the pre-flight unrun (see $OBJ_DIR/probe-build.log)."
    else
        warn "Could not build the loader pre-flight probe (dotnet route) - the loader pre-flight is"
        warn "NOT RUN (see $OBJ_DIR/probe-build.log)."
    fi
elif [ $probe_required -eq 1 ]; then
    die "mono or $PROBE_SRC missing - the loader pre-flight cannot run on the primary compiler route."
else
    warn "mono not available - the loader pre-flight probe is NOT RUN (dotnet route)."
fi

# ---------------------------------------------------------------------------
# 6. Assemble the deployable mod folder
# ---------------------------------------------------------------------------
log "Assembling $OUT_DIR ..."
cp -f "$OUT_DLL" "$OUT_DIR/CommNextRedux.dll"
cp -f Assets/CommNextRedux/swinfo.json "$OUT_DIR/swinfo.json"
# ============================ WARNING - READ BEFORE DEPLOYING ============================
# "$OUT_DIR" is assembled from Assets/CommNextRedux/Copied/, which is the PROJECT'S OWN
# payload tree, and it is now the COMPLETE payload set: assets/ (the UI bundle and the mod's
# icon), localizations/ (the string table) and patches/ (the Lua part patches). Every subtree
# is removed and re-copied, so a file deleted from the source cannot survive here.
#
# It is complete, NOT a mirror of the live install: the live folder additionally carries
# CommNextRedux-config.json, which the loader GENERATES at runtime and which must never be
# shipped.
#
# NEVER rsync/cp -a "$OUT_DIR" onto $KSP2/mods/CommNextRedux/. Deploy by explicit file copies
# only, because this assembly step can re-assemble a stale tree over a good one.
# =========================================================================================
# One subtree at a time, each `rm -rf` + `cp -a`: a removed source file must not survive in
# the staged tree, and a removed source SUBTREE must fail the build rather than silently
# ship an incomplete payload (which is exactly how the CSV and both Lua files went missing
# from this tree for eight phases).
for payload in assets localizations patches; do
    src="Assets/CommNextRedux/Copied/$payload"
    [ -d "$src" ] || die "Payload subtree missing: $src - the deployable tree would be incomplete."
    rm -rf "$OUT_DIR/$payload"
    cp -a "$src" "$OUT_DIR/$payload"
done
# `.gitkeep` is source-tree scaffolding, not payload: it is excluded from the staged tree.
# (Proved by `find Deploy/CommNextRedux -type f -not -name '.gitkeep' | sort`.)
find "$OUT_DIR" -name '.gitkeep' -delete

log "Done."
log "DLL:     $OUT_DIR/CommNextRedux.dll  ($(stat -c%s "$OUT_DIR/CommNextRedux.dll") bytes)"
log "Package: $OUT_DIR"
# The staged FILE SET, not a size and not a count: this is the line a later phase reads to
# prove the payload is complete without re-deriving it from the source tree.
log "Staged payload set:"
( cd "$OUT_DIR" && find . -type f | sed 's|^\./||' | LC_ALL=C sort | sed 's/^/  /' )
log ""
log "Deploy with:"
log "  cp -f \"$OUT_DIR/CommNextRedux.dll\" \"$KSP2/mods/CommNextRedux/CommNextRedux.dll\""
