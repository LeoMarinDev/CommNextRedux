#!/usr/bin/env bash
#
# Rebuilds commnextredux_ui.bundle for KSP2 Redux 0.2.9.0.104521 (Unity 6000.5.8f1) from the UI
# sources in this repo, and drops the result into Deploy/CommNextRedux/assets/bundles/ ready to
# deploy.
#
# WHY THIS EXISTS
# ---------------
# A bundle built by an editor newer than the player's is rejected outright. Redux 0.2.9.0.104521
# ships a Unity 6000.5.8f1 player, which reads asset format SerializedFile version 23; the
# superseded 0.2.8.5.103184 player was a 6000.4.1f1 build that only read version 22. In the other
# direction, the released pre-Redux-era CommNextRedux UI bundle was built for a 2022.3.5f1-era
# toolchain and did not load here at all:
#
#   Failed to load 'archive:/CAB-...'. File may be corrupted or was serialized with a newer
#   version of Unity.
#   The AssetBundle 'commnextredux_ui.bundle' can't be loaded because it was not built with the right
#   version or build target.
#
# That is a serialisation-format wall, not a bad byte or a bad path, so it cannot be patched:
# the bundle has to be rebuilt by an editor of the matching version. This script does that
# from a minimal, throwaway project instead of the full mod project, because the full project
# pins packages that no longer resolve and requires the ThunderKit toolchain the backport
# deliberately avoids.
#
# The minimal project needs no external packages at all.
#
# The root page may root on a custom control from the uitkforksp2.controls package. That package
# is not on disk in this install, so a GUID-verified asset subset is vendored under
# Tools/unity-bundle/uitkforksp2.controls (see its PROVENANCE.md) and the shipped 0.2.8.5
# uitkforksp2.controls.Runtime.dll (+ its Addressables dependencies) is staged as a managed
# plugin for the editor to resolve the type with.
#
# uitkforksp2.controls: MEASURED, NOT ASSUMED (Phase 2, 2026-09-15)
# ------------------------------------------------------------------
# This port does not carry the vendored subset, because the legacy CommNext markup never names a
# `UitkForKsp2.Controls.*` element and never imports KerbalUI.uss. That is a *measurement of the
# markup as it stands*, re-taken every run, not a standing exemption:
#
#   PKG_REFS=$(rg -o --no-filename 'uitkforksp2\.controls' "$SRC_UI_DIR" -g '*.uxml' -g '*.uss' | wc -l)
#
# When that count is non-zero the subset is REQUIRED and both checks below fail hard. When it is
# zero the subset would be ~200 files of third-party payload that nothing resolves through.
#
# PHASE 2 CORRECTION - THE ELEMENT CENSUS THIS SCRIPT USED TO CARRY WAS WRONG
# -------------------------------------------------------------------------
# Phase 1 recorded "the legacy's UI is entirely built-in elements plus USS classes, and zero C#
# needs to travel for it", from this grep:
#
#   rg -o '<ui:[A-Za-z]+' <UI> -g '*.uxml'   ->  29 VisualElement 11 Label 6 Button 5 UXML
#                                                2 DropdownField 1 Toggle 1 ScrollView
#
# That pattern REQUIRES A COLON, so it cannot see a custom element written as a dotted full type
# name - which is exactly how this port's markup writes them:
#
#   <CommNext.Unity.Runtime.Controls.BandIcon color="#9A21B4FF" code="X" name="band-icon" ... />
#
# The legacy's three custom controls really are used from markup (4 sites in 3 pages), so that
# control C# DOES have to travel into the minimal project: the UXML importer cannot resolve an
# element type that no loaded assembly defines, and the import - and therefore the build - fails
# outright. The CUSTOM_CTRL_* block below now measures the dotted form and does the copy.
#
# USAGE
#   Tools/build-ui-bundle.sh
#   UNITY=/path/to/Unity Tools/build-ui-bundle.sh
#   PLAYER_UNITY=6000.5.8f1 Tools/build-ui-bundle.sh
#
# THE EDITOR DEFAULT IS OVERRIDDEN AT THE 0.2.9.0.104521 UPDATE. It used to name the superseded
# 6000.4.1f1 install this project was originally built with; it now follows $PLAYER_UNITY, whose
# default is this pin's player - 6000.5.8f1 - so a bare run selects the required editor generation
# (and an unsupported one is refused below). Passing UNITY= explicitly is still the documented
# invocation. $PLAYER_UNITY also derives the SerializedFile ceiling (6000.5.* -> 23,
# 6000.4.*/2022.3.* -> 22), so a correct v23 bundle cannot fail its own gate. Everything else
# version-dependent - ProjectVersion.txt, the com.unity.ugui package pin, and the post-build format
# assertions - is derived from the selected binary.
#
# Transplanted and de-hardcoded for CommNextRedux; the origin of every file in Tools/ is recorded
# in Deploy/obj/PORT-PROGRESS.md. Every gate the original performed is kept.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

log()  { printf '\033[1;34m[bundle]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[bundle]\033[0m %s\n' "$*" >&2; exit 1; }

# The player generation the bundle must load in. Redux 0.2.9.0.104521 ships a Unity 6000.5.8f1
# player, which reads SerializedFile 23; the superseded 0.2.8.5.103184 player was a 6000.4.1f1
# build that read only 22. The ceiling is DERIVED from this instead of hard-coded - the old
# `<= 22` guard refused a correct v23 bundle - and the editor default follows it, so a bare run
# builds for this pin's player unless the caller asks for another generation.
PLAYER_UNITY="${PLAYER_UNITY:-6000.5.8f1}"
case "$PLAYER_UNITY" in
    6000.5.*) MAX_SVER=23 ;;
    6000.4.*) MAX_SVER=22 ;;
    2022.3.*) MAX_SVER=22 ;;
    *) fail "unsupported PLAYER_UNITY $PLAYER_UNITY - supported: 6000.5.x (Redux 0.2.9.0), 6000.4.x (0.2.8.5), 2022.3.x" ;;
esac
# THE EDITOR DEFAULT IS OVERRIDDEN: it follows $PLAYER_UNITY (6000.5.8f1 at this pin), never the
# superseded 6000.4.1f1 install this project was ported from. UNITY= still wins when passed.
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/$PLAYER_UNITY/Editor/Unity}"
PROJECT="$REPO_ROOT/Deploy/ui-bundle-project"
OUT_BUNDLE="$REPO_ROOT/Deploy/CommNextRedux/assets/bundles/commnextredux_ui.bundle"

[ -x "$UNITY" ] || fail "Unity editor not found or not executable: $UNITY
Set UNITY=/path/to/Editor/Unity if it lives elsewhere (PLAYER_UNITY=$PLAYER_UNITY)."
log "Unity: $("$UNITY" -version 2>/dev/null | sed -n '1p')  ($UNITY)"
log "Player generation: $PLAYER_UNITY  (SerializedFile ceiling: $MAX_SVER)"

# --- detect the selected editor's version (drives files and assertions below) -----------
# NOTE: no `| head -1` here. This is an ASSIGNMENT, so with `set -o pipefail` a short-circuiting
# consumer makes the whole command substitution report SIGPIPE (141) and `set -e` aborts the
# script - silently, before it prints anything else. That is what this script did before it was
# ever run: `UNITY_VERSION=...` never completed. Read every line and take the first in bash.
UNITY_VERSION="$("$UNITY" -version 2>/dev/null \
    | sed -nE 's/^.*\b([0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+)\b.*$/\1/p')"
UNITY_VERSION="${UNITY_VERSION%%$'\n'*}"
[ -n "$UNITY_VERSION" ] || fail "could not read a Unity version from: $UNITY -version"
case "$UNITY_VERSION" in
    6000.5.*|6000.4.*|2022.3.*) ;;
    *) fail "unsupported Unity editor $UNITY_VERSION - supported: 6000.5.x (this pin's player; the default), 6000.4.x (0.2.8.5), 2022.3.x" ;;
esac
# Revision hash for ProjectVersion.txt: the one embedded in the editor binary if it can be
# found, else the known revision for the supported versions.
if command -v strings >/dev/null 2>&1; then
    UNITY_REVISION="${UNITY_REVISION:-$(strings -a "$UNITY" 2>/dev/null \
        | sed -nE "s/^${UNITY_VERSION} \(([0-9a-f]{12})\)$/\1/p" | sed -n '1p')}"
fi
case "$UNITY_VERSION" in
    6000.5.8f1) UNITY_REVISION="${UNITY_REVISION:-5cb7df797b7d}" ;;
    6000.4.1f1) UNITY_REVISION="${UNITY_REVISION:-336a400b9ea2}" ;;
    2022.3.5f1) UNITY_REVISION="${UNITY_REVISION:-9674261d40ee}" ;;
esac

# --- sources that must exist, or the rebuild cannot be faithful -------------------------
# The UXML/USS (and the fonts/images they reference) live in the mod's asset-level UI tree.
SRC_UI_DIR="Assets/CommNextRedux/UI"

# The mod's own custom UI controls. They DO travel into the minimal project - see the PHASE 2
# CORRECTION in this file's header: the legacy markup names three of them as dotted element
# types, and the UXML importer resolves that name against the assemblies the editor project
# actually loaded. Without the sources the import fails and the build dies.
#
# They are cheap to transport because they are pure UI Toolkit: measured, the five files'
# complete `using` set is {System, System.Collections.Generic, System.Linq, UnityEngine,
# UnityEngine.UIElements} - no KSP, no ReduxLib, no SpaceWarp. That is why no game assembly has
# to be staged for them (contrast the uitkforksp2.controls route below, which ships a runtime DLL).
CUSTOM_CTRL_SRC="Assets/CommNextRedux/Code/UI/Controls"

# Where the EVIDENCE-ONLY legacy ShaderGraph chain is staged when COMMNEXTREDUX_RENDER_ASSETS=1
# (see the render-group block below). Nothing from this directory ships as of P6: the line
# material is built in code at runtime, so the shipping bundle packs no material at all.
#
# The one render-group asset that DOES ship, from P7 on, is the range-ruler sphere MESH
# (`Tools/legacy-render-chain/Meshes/RulerSphere.fbx`) - geometry carries no shader reference, so
# the gate that forbids materials does not reach it, and the mesh block below stages it with a
# transformed importer meta (no material remap, no material import, readable).
D3_MAT_DIR="Assets/CommNextRedux/Shaders"

# The namespace prefix a custom element carries. Measured over the markup: this is the only
# non-`ui` element namespace in the tree.
CUSTOM_CTRL_NS="CommNext.Unity.Runtime.Controls"

[ -d "$SRC_UI_DIR" ]   || fail "missing $SRC_UI_DIR"
[ -f "$SRC_UI_DIR/CN_UI.uxml" ] || fail "missing $SRC_UI_DIR/CN_UI.uxml (the root page)"
[ -d "$CUSTOM_CTRL_SRC" ] || fail "missing $CUSTOM_CTRL_SRC (the port's custom UI controls)"

# --- why the legacy ShaderGraph chain does NOT ship (D3, decided in Phase 2) -------------
# The legacy draws its connection lines with CommConnectionMat.mat, whose shader is the
# ShaderGraph CommConnectionShad.shadergraph, and its ruler with RulerSphere.prefab (+ its FBX).
#
# A .shadergraph is an EDITOR-SIDE asset: it only imports when the `com.unity.shadergraph`
# package is DECLARED. Undeclared, its importer never registers, the asset falls through to
# DefaultImporter, and BuildPipeline rejects it with "Unrecognized assets cannot be included in
# AssetBundles" - a message that reads exactly like "ShaderGraph cannot survive the bundle round
# trip" and is not that at all. That was the FIRST measurement, and it was not sufficient.
#
# So the faithful route was TESTED, not assumed: COMMNEXTREDUX_RENDER_ASSETS=1 declares
# com.unity.shadergraph at the version THIS EDITOR ships (17.4.0, read from its own
# BuiltInPackages - never the legacy manifest's 14.0.8, a 2022.3 package; see D11). The whole
# dependency closure resolves, including the registry package that was the first suspect:
#
#   $ python3 -c "import json;print(json.load(open('$U/com.unity.shadergraph/package.json'))['dependencies'])"
#   {'com.unity.render-pipelines.core': '17.4.0', 'com.unity.searcher': '4.9.3'}
#   packages-lock.json -> searcher 4.9.4 registry, render-pipelines.core 17.4.0 builtin
#   Deploy/ui-bundle-project/Library/PackageCache/ -> com.unity.searcher@d45a78918735 present
#
# The package resolves and then FAILS TO COMPILE, inside itself, against this editor's own
# assemblies - exactly 2 errors, both in the package, no bundle produced (faithful-attempt-2.log):
#
#   Library/PackageCache/com.unity.shadergraph@4790a86f4d6e/Editor/Generation/Contexts/TargetSetupContext.cs(62,40):
#     error CS0246: The type or namespace name 'GUID' could not be found
#   .../Targets/BuiltIn/Editor/ShaderGraph/Targets/BuiltInCanvasSubTarget.cs(10,25): the same
#
# `GUID` is UnityEngine.GUID, which this pinned editor does not expose where the package expects
# it: the BuiltInPackages copy of ShaderGraph shipped in this install disagrees with the editor
# core. Every route out is forbidden or absent - upgrading the editor or the package is hard rule
# 1, the legacy 14.0.8 is the wrong generation (D11), and there is no other 6000.4.1f1-conformant
# ShaderGraph on this box. So the faithful route is not merely unproven: it is unreachable, which
# is the pre-authorised D3 condition. D3 is taken:
#
#   * the line material ships as a BUILT-IN-shader material, authored in this project by
#     BuildCommNextReduxUIBundle from Shader.Find (no hand-written YAML, no guessed GUIDs), and
#     proof that built-in shaders are available here is in the editor's own resources:
#       $ strings -a $U/Data/Resources/unity_builtin_extra | grep -c 'Sprites/Default'   ->  1
#
#     ^^ P6 SUPERSEDED THIS ARM, and the two halves must be read separately. The last line above
#     measured whether the SHADER EXISTS in this editor's built-in resources, which it does; P2
#     then packed a material referencing it and the live player read it back as
#     'Hidden/InternalErrorShader' - which is a fact about SERIALIZED shader references in an
#     AssetBundle, and it stands. What was generalised from it - "Sprites/Default is absent from
#     the player" - does not, because the sibling port (CommLinesRedux) resolves the same shader
#     at RUNTIME and three separate launches log it succeeding:
#       [CommLinesRedux] material: shader resolved to "Sprites/Default" (looked up "Sprites/Default" then "Unlit/Transparent")
#     A runtime Shader.Find and a serialized bundle reference are different mechanisms resolving
#     against different sets. So P6 builds the material in CODE at runtime
#     (CommNextRedux.Rendering.LineMaterials) and this bundle now packs NO material at all - the
#     NO-MATERIAL gate below fails the build if one ever reappears. D3 is narrowed, not reversed:
#     the legacy ShaderGraph chain still does not ship, and now nothing else does either.
#   * the sphere/ring geometry is built in code at runtime instead of from prefabs + FBX.
#     (Measured, and worth carrying to P8: the FBX and the prefabs DO pack - it is the MATERIAL
#      that cannot ship. In the faithful attempt the legacy material packed, at a doubled path,
#      carrying shader='Hidden/InternalErrorShader' + mainTex=NULL: the blank-window trap itself.)
#
# UNANSWERED, and stated rather than glossed: because the package never compiled, nothing here
# measures whether a 2022.3-authored ShaderGraph would have round-tripped to 6000.4. That
# question is not what decides D3 - at the pin, the editor's own ShaderGraph is unreachable and
# the fallback is the only shippable route. The evidence is kept and is re-runnable: set
# COMMNEXTREDUX_RENDER_ASSETS=1 to attempt the faithful chain; the failure lands at package
# compile, before any bundle exists. That path is EVIDENCE, never shippable.
#
# COMMNEXTREDUX_RENDER_ASSETS: 0 (default) = ship the P6 route (no material packed; the line
# material is built in code at runtime); 1 = pack the legacy render chain for the audit only. The
# editor script reads the same variable.
RENDER_ASSETS="${COMMNEXTREDUX_RENDER_ASSETS:-0}"

# --- uitkforksp2.controls: the package that owns UitkForKsp2.Controls.* controls ---------
# The root page may root on one of that package's controls and reference its KerbalUI.uss; the
# package is declared in Packages/manifest.json but is not present on disk anywhere in this
# install. The vendored subset carries the original .meta files, so the GUIDs the UXML/USS
# reference resolve. The runtime assembly comes from the game's own 0.2.8.5 files so its
# UxmlSerializedData attribute set matches the runtime exactly; the DLLs are never modified.
#
# MEASURED, NOT ASSUMED: the subset is required only when this port's own markup actually names
# the package. To satisfy the check when the need is proven, copy the subset out of the read-only
# donor (it is the same package, GUID-verified, and is not a CommNext asset):
#
#   cp -a mods/FlightPlanRedux/Tools/unity-bundle/uitkforksp2.controls Tools/unity-bundle/
PKG_SUBSET="Tools/unity-bundle/uitkforksp2.controls"
# NOTE ON `|| true`: `rg` exits 1 when it finds NOTHING, and "nothing" is its success signal for a
# count. Under `set -euo pipefail` an assignment whose command substitution ends in a non-zero
# status aborts the whole script - so a measurement that legitimately finds zero matches kills the
# build silently, before a single further line is printed. That is exactly what happened the first
# time this script was ever executed, in the PKG_REFS=0 case below. Every rg-based measurement in
# an assignment must be `|| true`-guarded. The same applies to `ls` on a glob that matches nothing.
PKG_REFS=$({ rg -o --no-filename 'uitkforksp2\.controls' "$SRC_UI_DIR" -g '*.uxml' -g '*.uss' 2>/dev/null || true; } | wc -l)
if [ "$PKG_REFS" -gt 0 ]; then
    log "uitkforksp2.controls: $PKG_REFS reference(s) in the markup - the vendored subset is REQUIRED"
    [ -f "$PKG_SUBSET/package.json" ] || fail "missing vendored package: $PKG_SUBSET/package.json
The markup references uitkforksp2.controls $PKG_REFS time(s), so the UXML importer must find the
package's assets AND its runtime types. Copy the GUID-verified subset out of the donor:
  cp -a mods/FlightPlanRedux/Tools/unity-bundle/uitkforksp2.controls Tools/unity-bundle/"
    [ -f "$PKG_SUBSET/Assets/Theme/KerbalUI.uss" ] || fail "missing vendored $PKG_SUBSET/Assets/Theme/KerbalUI.uss
The markup references uitkforksp2.controls, so its stylesheet must resolve. Same copy as above."
    NEED_PKG=1
else
    log "uitkforksp2.controls: 0 references in the markup - the vendored subset is not packed"
    log "  (measurement: rg -o --no-filename 'uitkforksp2\\.controls' \"$SRC_UI_DIR\" -g '*.uxml' -g '*.uss' | wc -l)"
    NEED_PKG=0
fi

# --- the port's OWN custom controls: measured, and required when the markup names them -----
# PHASE 2 CORRECTION at the top of this file explains why the count below uses the DOTTED form.
# A non-zero count means the minimal project cannot import the pages without these sources.
CUSTOM_CTRL_HITS=$({ rg -o --no-filename "<$CUSTOM_CTRL_NS\\.[A-Za-z0-9_]+" "$SRC_UI_DIR" -g '*.uxml' 2>/dev/null || true; } | wc -l)
CUSTOM_CTRL_TYPES=$({ rg -o --no-filename "<$CUSTOM_CTRL_NS\\.([A-Za-z0-9_]+)" -r '$1' "$SRC_UI_DIR" -g '*.uxml' 2>/dev/null || true; } | sort -u | tr '\n' ' ')
log "custom controls in the markup: $CUSTOM_CTRL_HITS site(s), types=[${CUSTOM_CTRL_TYPES% }]"
if [ "$CUSTOM_CTRL_HITS" -gt 0 ]; then
    CUSTOM_CTRL_FILES=$({ ls -1 "$CUSTOM_CTRL_SRC"/*.cs 2>/dev/null || true; } | wc -l)
    [ "$CUSTOM_CTRL_FILES" -gt 0 ] || fail "the markup names $CUSTOM_CTRL_NS.* but $CUSTOM_CTRL_SRC holds no .cs
The UXML importer resolves an element type against the loaded assemblies, so without these the
import - and the build - fails. This is not a warning: a missing type is an unimportable page."
    # Every type the markup names must exist as a source file that gets compiled into the project.
    for t in $CUSTOM_CTRL_TYPES; do
        rg -q "class $t\b" "$CUSTOM_CTRL_SRC"/*.cs \
            || fail "the markup names <$CUSTOM_CTRL_NS.$t> but no source under $CUSTOM_CTRL_SRC declares it."
    done
    TRAVEL_CONTROLS=1
else
    TRAVEL_CONTROLS=0
fi

# --- assemble the minimal project -------------------------------------------------------
log "Creating minimal project at Deploy/ui-bundle-project ..."
rm -rf "$PROJECT"
mkdir -p "$PROJECT/Assets" "$PROJECT/Packages" "$PROJECT/ProjectSettings"

# Pin the editor to the selected version. Without this the editor treats the project as
# belonging to a different Unity and refuses to open it in batchmode. Both fields come from
# the selected binary (revision via $UNITY_REVISION if it cannot be read from the binary).
{
    printf 'm_EditorVersion: %s\n' "$UNITY_VERSION"
    if [ -n "$UNITY_REVISION" ]; then
        printf 'm_EditorVersionWithRevision: %s (%s)\n' "$UNITY_VERSION" "$UNITY_REVISION"
    fi
} > "$PROJECT/ProjectSettings/ProjectVersion.txt"

# Only built-in packages: com.unity.ugui is shipped inside the editor and provides
# TextMeshPro, which a TMP_FontAsset needs to import.
#
# The value below is a MINIMUM-VERSION REQUEST for a module the editor ships, not a selector for a
# copy: there is exactly one copy of com.unity.ugui on the box, inside the selected editor. The
# built-in versions were measured on this machine - 6000.4.1f1 ships 2.0.0, 6000.5.8f1 ships 2.5.0,
# 2022.3.x ships 1.0.0 - and the 6000.5.8f1 build below declares the 2.0.0 request the sibling
# FlightPlan script also declares at this pin: the editor satisfied it from its own 2.5.0 copy and
# logged no resolution error (measured, U03). Requesting HIGHER than the editor ships is the
# direction that fails, which is why the row stays conservative rather than tracking each editor.
#
# com.unity.modules.assetbundle is REQUIRED. Without it, BuildPipeline builds a bundle that
# has no class-142 AssetBundle object and therefore an empty container - the 0.2.8.5 player
# rejects it with "not compatible with this newer version of the Unity runtime" (and the
# UXML lookup then null-references). The causal bisect (minimal PNG + a full asset set, both
# editors, both marking paths) is in the sibling port's Deploy/obj/bundle-fingerprints.md;
# both known-good mod projects' root manifests include this module.
case "$UNITY_VERSION" in
    2022.3.*) UGUI_VERSION="1.0.0" ;;
    6000.5.*) UGUI_VERSION="2.0.0" ;;   # editor ships 2.5.0 - a request at or below it resolves
    6000.4.*) UGUI_VERSION="2.0.0" ;;   # editor ships 2.0.0 - exact
    *)        UGUI_VERSION="2.0.0" ;;
esac

# --- the ShaderGraph package, EVIDENCE PATH ONLY -------------------------------------------------
#
# MEASURED (Phase 2, 2026-09-15): a .shadergraph does NOT import unless com.unity.shadergraph is
# DECLARED, even though the editor ships it under PackageManager/BuiltInPackages. Undeclared, its
# importer never registers, the asset falls through to DefaultImporter, and BuildPipeline then
# rejects it with "Unrecognized assets cannot be included in AssetBundles" - which reads exactly
# like "ShaderGraph cannot survive the bundle round trip" and is not that at all.
#
# So the faithful route is TESTED rather than assumed. The declaration goes in only when
# COMMNEXTREDUX_RENDER_ASSETS=1, for one reason that is worth being explicit about:
# com.unity.shadergraph 17.4.0 depends on com.unity.searcher 4.9.3, which is NOT a built-in
# package, so declaring it makes the fixture resolve a REGISTRY package over the network. The
# shipping build must stay hermetic and offline-reproducible, so it declares neither.
#
# The version is read FROM THIS EDITOR's own copy - never the legacy manifest's 14.0.8 (D11).
SHADERGRAPH_BLOCK=""
if [ "$RENDER_ASSETS" = "1" ]; then
    SG_PKG=$(find "$(dirname "$UNITY")/Data/Resources/PackageManager/BuiltInPackages" \
                -maxdepth 1 -type d -name 'com.unity.shadergraph' 2>/dev/null | head -1)
    if [ -z "$SG_PKG" ]; then
        fail "COMMNEXTREDUX_RENDER_ASSETS=1 but this editor ships no com.unity.shadergraph under
  BuiltInPackages - the faithful route cannot even be attempted. Editor: $UNITY"
    fi
    SG_VERSION=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['version'])" \
                    "$SG_PKG/package.json")
    log "RENDER_ASSETS=1: declaring com.unity.shadergraph $SG_VERSION (read from this editor, not the legacy 14.0.8)"
    # Trailing comma is required: this is spliced in above the final entry.
    SHADERGRAPH_BLOCK="    \"com.unity.shadergraph\": \"$SG_VERSION\",
"
fi

cat > "$PROJECT/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.ugui": "$UGUI_VERSION",
$SHADERGRAPH_BLOCK    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}
EOF
log "fixture manifest:"
sed 's/^/    /' "$PROJECT/Packages/manifest.json"

# Copy the sources WITH their .meta files. The GUIDs in those .meta files are what the
# UXML/USS cross-references (project://database/...?guid=...) and the image/font references
# are resolved by, so preserving them is what keeps the rebuilt UI pointing at the same
# assets as the original. .meta files are also where the original bundle markings live.
copy_tree() {
    local src="$1" dst="$2"
    [ -e "$src" ] || { log "  skip (absent): $src"; return; }
    mkdir -p "$(dirname "$dst")"
    cp -a "$src" "$dst"
}

log "Copying UI sources ..."
copy_tree "$SRC_UI_DIR"  "$PROJECT/$SRC_UI_DIR"

# The port's own custom controls travel as SOURCE, not as a DLL: they are pure UI Toolkit, so the
# minimal project can compile them into its own Assembly-CSharp and the UXML importer can then
# resolve `<CommNext.Unity.Runtime.Controls.*>`. Shipping the sources rather than a prebuilt DLL
# also keeps this project free of game assemblies.
if [ "$TRAVEL_CONTROLS" = "1" ]; then
    log "Copying the port's custom controls (the markup names them) ..."
    copy_tree "$CUSTOM_CTRL_SRC" "$PROJECT/$CUSTOM_CTRL_SRC"
    ( cd "$PROJECT" && ls -1 "$CUSTOM_CTRL_SRC"/*.cs | sed 's|^|    |' )
else
    log "Skipping the custom controls: the markup names no custom element type"
fi

if [ "$NEED_PKG" = "1" ]; then
    # The package assets (KerbalUI.uss, its sprites and the JetBrains LED/pixel fonts) go in as an
    # *embedded* package: copied into Packages/ with the original package.json and .meta files, so
    # the project://database/Packages/uitkforksp2.controls/... references in the UXML/USS resolve
    # without any Package Manager network access at build time.
    log "Copying vendored uitkforksp2.controls package subset ..."
    copy_tree "$PKG_SUBSET" "$PROJECT/Packages/uitkforksp2.controls"

    # The control TYPE itself comes from the shipped runtime DLL. Without it the UXML importer
    # cannot resolve a <UitkForKsp2.Controls.*> element and the root page fails to import.
    log "Staging uitkforksp2.controls runtime plugins ..."
    MANAGED_DIR="${COMMNEXTREDUX_MANAGED:-$REPO_ROOT/Packages/KSP2_x64}"
    if [ ! -f "$MANAGED_DIR/uitkforksp2.controls.Runtime.dll" ]; then
        MANAGED_DIR="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program 2/KSP2_x64_Data/Managed"
    fi
    for dll in uitkforksp2.controls.Runtime.dll Unity.Addressables.dll Unity.ResourceManager.dll; do
        [ -f "$MANAGED_DIR/$dll" ] || fail "missing managed plugin $MANAGED_DIR/$dll (needed to resolve UitkForKsp2.Controls types)"
    done
    mkdir -p "$PROJECT/Assets/Plugins"
    cp -f "$MANAGED_DIR/uitkforksp2.controls.Runtime.dll" "$PROJECT/Assets/Plugins/"
    cp -f "$MANAGED_DIR/Unity.Addressables.dll"           "$PROJECT/Assets/Plugins/"
    cp -f "$MANAGED_DIR/Unity.ResourceManager.dll"        "$PROJECT/Assets/Plugins/"
    log "  plugins from: $MANAGED_DIR"
else
    log "Skipping the uitkforksp2.controls subset and its plugins (markup references the package 0 times)"
fi

# PHASE 7: the range-ruler sphere MESH is the one render-group asset that ships.
#
# Geometry is not a material. The legacy chain's MATERIAL cannot ship - its serialized shader
# reference resolves to Hidden/InternalErrorShader in this player (D3, measured) - but the mesh is
# plain vertex data with no shader reference in it at all, so the NO-MATERIAL gate below (which is
# about `t:Material` and nothing else) permits it and the audit still demands 0 materials.
#
# THE IMPORTER META IS TRANSFORMED, NOT COPIED. The vendored `RulerSphere.fbx.meta` is the legacy
# project's own importer state, and two of its fields describe the LEGACY project rather than this
# geometry:
#
#   * `externalObjects` remaps the model's material slot to a GUID in the legacy project
#     (`{guid: e4bbd53f...}`). Nonexistent here, but exactly the kind of dangling reference that
#     turns into a material dependency at pack time - and a packed material is the failure this
#     whole block exists to avoid.
#   * `materialImportMode: 2` (ImportViaMaterialDescription) lets the importer create material
#     assets from the model. The FBX carries no material description at all
#     (`strings RulerSphere.fbx | grep -c material` -> 0), so this would create nothing today and
#     something the day the asset is replaced.
#
# So the staged meta is: `externalObjects: []` (no remaps), `materialImportMode: 0` (import no
# materials, create none) and `isReadable: 1`. Readability is set deliberately: the runtime reads
# `mesh.bounds` and `mesh.vertexCount` to normalise the sphere's scale, and `bounds` access on a
# non-readable mesh is exactly the kind of thing that works in one Unity generation and not the
# next. A 44 KB mesh is not worth that risk. Everything else in the meta is the legacy's.
MESH_SRC="Tools/legacy-render-chain/Meshes/RulerSphere.fbx"
MESH_STAGE_DIR="$PROJECT/Assets/CommNextRedux/Meshes"
[ -f "$MESH_SRC" ] || fail "the vendored range-ruler mesh is missing: $MESH_SRC"
mkdir -p "$MESH_STAGE_DIR"
cp -f "$MESH_SRC" "$MESH_STAGE_DIR/RulerSphere.fbx"
python3 - "$MESH_SRC.meta" "$MESH_STAGE_DIR/RulerSphere.fbx.meta" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines()
out, i, dropped = [], 0, 0
while i < len(lines):
    line = lines[i]
    if line.startswith("  externalObjects:"):
        out.append("  externalObjects: []")
        i += 1
        # drop the whole block list that follows (entries are indented at least two spaces and
        # start with '- ' or are its continuation lines)
        while i < len(lines) and (lines[i].startswith("  - ") or
                                  (lines[i].startswith("    ") and lines[i].strip())):
            dropped += 1
            i += 1
        continue
    if line.strip().startswith("materialImportMode:"):
        out.append("    materialImportMode: 0")
        i += 1
        continue
    if line.strip().startswith("isReadable:"):
        # PRESERVE THE INDENTATION. The vendored field sits inside the `animations:` group (four
        # spaces), and Unity's meta reader is position-sensitive: a known field written at the
        # wrong nesting is dropped rather than honoured. Measured - the first version of this
        # transform wrote it at two spaces and the delivered mesh still read isReadable=False.
        indent = line[:len(line) - len(line.lstrip())]
        out.append(indent + "isReadable: 1")
        i += 1
        continue
    out.append(line)
    i += 1
open(dst, "w", encoding="utf-8").write("\n".join(out) + "\n")
print(f"    meta transformed: externalObjects -> [] ({dropped} remap line(s) dropped), "
      f"materialImportMode -> 0, isReadable -> 1")
PY
grep -q 'materialImportMode: 0' "$MESH_STAGE_DIR/RulerSphere.fbx.meta" \
    || fail "the staged mesh's meta does not disable material import - the pack could drag a material in"
grep -q 'externalObjects: \[\]' "$MESH_STAGE_DIR/RulerSphere.fbx.meta" \
    || fail "the staged mesh's meta still carries the legacy project's external object remaps"
log "Range-ruler mesh staged (mesh + transformed meta): $MESH_STAGE_DIR/RulerSphere.fbx"

# PHASE 6: nothing else from the render group is staged for the shipping bundle. The line material
# is built in code at RUNTIME (CommNextRedux.Rendering.LineMaterials), so there is no material to
# author, pack or resolve. The legacy ShaderGraph/prefab chain is still staged for the
# COMMNEXTREDUX_RENDER_ASSETS=1 measurement only, which is evidence and never ships.
if [ "$RENDER_ASSETS" = "1" ]; then
    log "COMMNEXTREDUX_RENDER_ASSETS=1 - staging the legacy ShaderGraph/prefab chain (EVIDENCE ONLY, never ship)"
    # The legacy chain is NOT in Assets/: a .shadergraph cannot import without the shadergraph
    # package, and a project that cannot import it would emit a junk asset for every build - and
    # a junk asset is exactly what would make the audit's NULL-shader reading ambiguous. It is
    # kept as a vendored corpus under Tools/ and staged into the project only for this measurement.
    copy_tree "Tools/legacy-render-chain/Shaders" "$PROJECT/$D3_MAT_DIR"
    # The two PREFABS only - the mesh itself is already staged above, with the transformed meta.
    # Copying the vendored Meshes tree here would overwrite that meta with the legacy one and put
    # the material-remapped prefabs back into a build that is meant to be measured, not shipped.
    for prefab in RulerSphere.prefab TestSphere.prefab; do
        cp -f "Tools/legacy-render-chain/Meshes/$prefab" "$MESH_STAGE_DIR/$prefab"
        cp -f "Tools/legacy-render-chain/Meshes/$prefab.meta" "$MESH_STAGE_DIR/$prefab.meta"
    done
    ( cd "$PROJECT" && find "$D3_MAT_DIR" -name '*.shadergraph' | sed 's|^|    evidence-only: |' )
else
    log "COMMNEXTREDUX_RENDER_ASSETS=0 - the render group is NOT staged: no material ships at all (P6)"
fi

mkdir -p "$PROJECT/Assets/Editor"
cp -f "Tools/unity-bundle/BuildCommNextReduxUIBundle.cs" "$PROJECT/Assets/Editor/"
cp -f "Tools/unity-bundle/VerifyCommNextReduxUxml.cs"    "$PROJECT/Assets/Editor/"
cp -f "Tools/unity-bundle/AuditCommNextReduxBundle.cs"   "$PROJECT/Assets/Editor/"

log "Project contents:"
( cd "$PROJECT" && find Assets Packages -type f \( -name '*.uxml' -o -name '*.uss' -o -name '*.cs' -o -name '*.dll' \) | sort | sed 's/^/    /' )

# --- build ------------------------------------------------------------------------------
LOG_FILE="$REPO_ROOT/Deploy/obj/unity-bundle-build.log"
mkdir -p "$(dirname "$LOG_FILE")"
log "Running Unity batchmode (log: $LOG_FILE) ..."

set +e
# COMMNEXTREDUX_BUNDLE_TARGET pins BuildTarget.StandaloneWindows (5) explicitly instead of relying
# on the C# helper's own default: the game is a Windows player under Proton, and a bundle built for
# this Linux box is the other "not built with the right build target" failure.
COMMNEXTREDUX_RENDER_ASSETS="$RENDER_ASSETS" COMMNEXTREDUX_BUNDLE_TARGET="StandaloneWindows" \
    "$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod BuildCommNextReduxUIBundle.Build \
    -logFile - > "$LOG_FILE" 2>&1
UNITY_EXIT=$?
set -e

# Unity's own exit code is not trustworthy in batchmode (script compile errors still exit 0),
# so the bundle's presence *and* the absence of compiler errors are what decide success.
if grep -qE "error CS[0-9]+" "$LOG_FILE"; then
    log "C# compile errors:"
    grep -E "error CS[0-9]+" "$LOG_FILE" | sort -u | sed -n '1,40p' | sed 's/^/    /'
    fail "bundle project failed to compile - see $LOG_FILE"
fi

BUILT="$PROJECT/BundleOutput/commnextredux_ui.bundle"
[ -f "$BUILT" ] || {
    log "Last lines of the Unity log:"
    tail -40 "$LOG_FILE" | sed 's/^/    /'
    fail "no bundle produced (Unity exit $UNITY_EXIT) - see $LOG_FILE"
}

log "Unity reported:"
grep -E "\[bundle\]" "$LOG_FILE" | sed 's/^.*\[bundle\]/[bundle]/' | sed 's/^/    /'

# --- verify it is the right format before shipping it ------------------------------------
# The outer header names the BUILDER revision. It is checked against $PLAYER_UNITY as a
# generation comparison, not against a hard-coded allow-list: a bundle from an editor NEWER
# than the player is exactly what the player rejects outright, while a same-or-older builder
# is the legal direction (the SerializedFile ceiling below is the field that actually gates).
python3 - "$BUILT" "$PLAYER_UNITY" <<'PY'
import struct, sys
path, player = sys.argv[1], sys.argv[2]
with open(path, "rb") as fh:
    head = fh.read(64)
if head[:8] != b"UnityFS\0":
    raise SystemExit("not a UnityFS bundle")
off = 8
struct.unpack_from(">I", head, off)[0]; off += 4
def cstr(buf, o):
    e = buf.index(b"\0", o); return buf[o:e].decode("utf-8", "replace"), e + 1
_, off = cstr(head, off)          # minimum player version ("5.x.x")
builder, off = cstr(head, off)    # builder revision
print(f"    builder revision : {builder}")


def generation(s):
    parts = s.split(".")
    try:
        return int(parts[0]), int(parts[1])
    except (IndexError, ValueError):
        return None


bg, pg = generation(builder), generation(player)
if bg is None:
    raise SystemExit(f"could not parse the bundle's builder revision: {builder!r}")
if bg > pg:
    raise SystemExit(f"bundle was built by {builder}, newer than the {player} player - "
                     f"a bundle from a newer editor is what the player rejects outright")
print(f"    builder <= player : {builder} <= {player}")
PY

# The inner SerializedFile version is the field that actually gates the player (this pin's
# 6000.5.8f1 player reads v23; the superseded 0.2.8.5.103184 player read only v22). Read it from
# the decompressed directory rather than trusting the outer header, which does not carry it.
#
# THREE ASSERTIONS RUN HERE, and the third is the one that carries the container map at this pin:
#
#  1. The SerializedFile ceiling, DERIVED from $PLAYER_UNITY (6000.5.* -> 23) rather than the old
#     hard-coded `<= 22`, which refused a correct v23 bundle.
#  2. The target platform must be 5 (StandaloneWindows) - the same platform the C# builder pins
#     via its own default, read back from the bytes so a silently-changed default cannot pass.
#  3. The class-142 type-table walk is KEPT (it still prints `present` on a v22 bundle), but it
#     takes its CHECK SKIPPED branch on every v23 bundle: the v22 metadata layout it walks is not
#     the v23 layout, so its struct reads run off the end of the buffer (measured against both
#     bundles). A skip is not a container assertion, so the parse-free CAB census below is the
#     load-bearing check - on the decompressed bytes, count `assets/` container entries. It is
#     calibrated on known bundles of this project and its sibling: the accepted v22 CommNextRedux
#     bundle reads 49, the accepted v23 FlightPlan bundle reads 84, and a bundle built without
#     com.unity.modules.assetbundle reads 0. name_hits is printed for diagnosis, NOT asserted.
python3 - "$BUILT" "$MAX_SVER" "$PLAYER_UNITY" <<'PY'
import os, re, struct, sys
path, max_sver, player = sys.argv[1], int(sys.argv[2]), sys.argv[3]
sys.path.insert(0, os.path.expanduser("~/.local/lib/python3.14/site-packages"))
try:
    import UnityPy
    from UnityPy.files import BundleFile
except ImportError as exc:
    raise SystemExit(
        f"format/container verification UNAVAILABLE: UnityPy is not installed ({exc}) - "
        "refusing to ship a bundle whose format and container were not read")
cap = {}
BundleFile.read_files = lambda self, r, d: cap.update(r=r, d=d)
try:
    UnityPy.load(path)
except Exception:
    pass
r, nodes = cap.get("r"), cap.get("d")
if r is None or not nodes:
    raise SystemExit(
        "format/container verification UNAVAILABLE: the bundle directory could not be read")


def cstr(buf, off):
    end = buf.index(b"\0", off)
    return buf[off:end], end + 1


def type_class_ids(meta, sver, endian, enable_tt, type_count):
    """Walk a v22 SerializedFile metadata body to its type table (sfscan layout)."""
    le = "<" if endian == 0 else ">"
    p = 0
    _, p = cstr(meta, p)                                    # unity version
    p += 4                                                  # target platform
    p += 1                                                  # enableTypeTree byte
    p += 4                                                  # type count (value passed in)
    ids = []
    for _ in range(min(type_count, 256)):
        class_id = struct.unpack_from(le + "i", meta, p)[0]
        p += 4
        ids.append(class_id)
        if sver >= 16:
            p += 1                                          # is_stripped_type
        if sver >= 17:
            p += 2                                          # script_type_index
        if (sver < 16 and class_id < 0) or (sver >= 16 and class_id == 114):
            p += 16                                         # script_id hash
        p += 16                                             # old_type_hash
        if enable_tt:
            if sver >= 12 or sver == 10:
                node_count, sbuf = struct.unpack_from(le + "ii", meta, p)
                p += 8 + node_count * (32 if sver >= 19 else 24) + sbuf
            if sver >= 21:
                dep_count = struct.unpack_from(le + "i", meta, p)[0]
                p += 4 + 4 * dep_count
    return ids


cab = bytearray()
first = True
for node in nodes:
    if node.path.endswith(".resS"):
        continue
    r.Position = node.offset
    head = r.read_bytes(48)
    meta_size = struct.unpack_from(">I", head, 20)[0]
    sver = struct.unpack_from(">I", head, 8)[0]
    endian = head[16]
    print(f"    SerializedFile version : {sver}  ({player} player accepts <= {max_sver})")
    if sver > max_sver:
        raise SystemExit(f"SerializedFile version {sver} is too new for the {player} player")
    if first:
        first = False
        r.Position = node.offset + 48
        meta = r.read_bytes(meta_size)
        platform = struct.unpack_from("<i", meta, meta.index(b"\0") + 1)[0]
        print(f"    target platform        : {platform}  (5 = StandaloneWindows)")
        if platform != 5:
            raise SystemExit(
                f"the bundle targets platform {platform}, not 5 (StandaloneWindows) - the game "
                "is a Windows player under Proton and rejects an AssetBundle as the wrong build "
                "target")
        try:
            enable_tt = meta[meta.index(b"\0") + 5]
            type_count = struct.unpack_from("<i", meta, meta.index(b"\0") + 6)[0]
            ids = type_class_ids(meta, sver, endian, enable_tt, type_count)
        except Exception as exc:  # parser limitation, not bundle evidence - do not fail the build
            print(f"    class 142 (AssetBundle) : CHECK SKIPPED ({exc!r}) - the v22 type-table "
                  f"walk cannot read a v{sver} bundle; the CAB census below is the container "
                  "assertion")
        else:
            print(f"    class 142 (AssetBundle) : "
                  f"{'present' if 142 in ids else 'ABSENT'}   (type table {ids})")
            if 142 not in ids:
                raise SystemExit(
                    "built bundle has no class-142 AssetBundle object - the player would reject "
                    "it. Check that Packages/manifest.json includes com.unity.modules.assetbundle")
    # Accumulate the decompressed CAB for the parse-free census below.
    r.Position = node.offset
    cab += r.read_bytes(node.size)

payload = bytes(cab)
basename = os.path.basename(path).encode()
assets_hits = len(re.findall(b"assets/", payload))
name_hits = len(re.findall(re.escape(basename), payload))
print(f"    container census       : assets_hits={assets_hits} name_hits={name_hits} "
      f"(decompressed CAB {len(payload)} B)")
if assets_hits == 0:
    raise SystemExit(
        "container census read 0 `assets/` entries - the CAB carries no container strings; a "
        "bundle built without com.unity.modules.assetbundle reads exactly this and the player "
        "rejects it as container-less")
PY

# --- render proof: instantiate every UXML page before shipping anything -----------------
# "Loads" is not "renders": a VisualTreeAsset can import cleanly and still clone with zero
# children (measured on 2022.3.5f1-built bundles). This second batchmode run
# on the same project instantiates every UXML and logs its childCount; any zero/negative is a
# hard build failure. Raw output is kept at Deploy/obj/uxml-verify.log - it is the pre-deploy
# evidence the orchestrator reads.
VERIFY_LOG="$REPO_ROOT/Deploy/obj/uxml-verify.log"
mkdir -p "$(dirname "$VERIFY_LOG")"
log "Verifying UXML instantiation (log: $VERIFY_LOG) ..."
set +e
"$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod VerifyCommNextReduxUxml.Verify \
    -logFile - > "$VERIFY_LOG" 2>&1
VERIFY_EXIT=$?
set -e
if grep -qE "error CS[0-9]+" "$VERIFY_LOG"; then
    log "C# compile errors (verify run):"
    grep -E "error CS[0-9]+" "$VERIFY_LOG" | sort -u | sed -n '1,20p' | sed 's/^/    /'
    fail "verify project failed to compile - see $VERIFY_LOG"
fi
if ! grep -q "\[uxml-verify\] result:" "$VERIFY_LOG"; then
    log "Last lines of the verify log:"
    tail -30 "$VERIFY_LOG" | sed 's/^/    /'
    fail "UXML verification did not run to completion (Unity exit $VERIFY_EXIT) - see $VERIFY_LOG"
fi
UXML_LINES=$(grep -c "\[uxml-verify\] Assets/CommNextRedux/UI/.* childCount=" "$VERIFY_LOG" || true)
UXML_ZERO=$(grep -cE "\[uxml-verify\] Assets/CommNextRedux/UI/.* childCount=(0|-1|LOAD-FAILED)" "$VERIFY_LOG" || true)
log "UXML templates instantiated: $UXML_LINES  (zero/negative childCount: $UXML_ZERO)"
grep "\[uxml-verify\]" "$VERIFY_LOG" | sed 's/^.*\[uxml-verify\]/[uxml-verify]/' | sed 's/^/    /'
[ "$UXML_LINES" -gt 0 ] || fail "no UXML templates were instantiated - see $VERIFY_LOG"
[ "$UXML_ZERO" -eq 0 ]  || fail "at least one UXML template instantiated with childCount <= 0 - see $VERIFY_LOG"

# The custom-control half of this gate. The markup names three of the port's own control types as
# dotted element names, so the project must resolve them or the pages do not import. The verify
# script logs one line per assertion; this counts the ones that PASSED, so a silently-skipped
# probe cannot pass the gate (a skipped probe emits neither PASS nor FAIL, and the count falls).
#
# COUNTED PER ROUTE, because the two fail for different reasons and only one of them is fatal at
# import time. BY TYPE answers "does the control exist in the loaded assemblies"; BY NAME answers
# "can a controller reach it with Q("name")". A dropped name leaves a correctly-typed element that
# nothing can ever bind to - the window renders empty with no error anywhere - so both must pass,
# and the earlier single-wording grep would have read that as a pass the moment one route logged.
CTRL_TYPE_OK=$(grep -c "\[uxml-verify\] custom control resolved (by type):" "$VERIFY_LOG" || true)
CTRL_NAME_OK=$(grep -c "\[uxml-verify\] custom control reachable by name:" "$VERIFY_LOG" || true)
CTRL_BAD=$(grep -c "CONTROL TYPE MISMATCH\|CONTROL NOT FOUND\|CONTROL NAME DROPPED\|CONTROL NAME COLLISION" "$VERIFY_LOG" || true)
log "custom control assertions: type=$CTRL_TYPE_OK/3, name=$CTRL_NAME_OK/3, failed=$CTRL_BAD"
[ "$CTRL_BAD" -eq 0 ]        || fail "a custom control did not resolve (type, name or collision) - see $VERIFY_LOG"
[ "$CTRL_TYPE_OK" -eq 3 ]    || fail "expected 3 custom-control TYPE assertions to pass, got $CTRL_TYPE_OK - see $VERIFY_LOG"
[ "$CTRL_NAME_OK" -eq 3 ]    || fail "expected 3 custom-control NAME assertions to pass, got $CTRL_NAME_OK - see $VERIFY_LOG"

# --- container + font + material audit, from the BUILT BYTES ----------------------------
# The verify pass above reads the sources out of the project, so it cannot see what survived
# the pack. This pass loads the built bundle back and lists its container, its fonts'
# material/shader/mainTexture and its stylesheets' unresolved-asset slots. It is the only
# route that reports the container keys, and a font that lost its shader or its atlas is the
# blank-window failure mode, so a NULL shader or mainTexture on a FONT here is a hard build
# failure.
#
# FONTS AND MATERIALS ARE CHECKED SEPARATELY, ON PURPOSE. They have opposite remedies: a font
# that loses its shader is a recipe bug with no fallback, while a material that loses its shader
# is exactly the D3 condition (and is answered by not shipping that material, as this port does
# with the legacy ShaderGraph chain). One blanket `shader=NULL` grep conflates the two - and it
# would also fail a correct build, because a built-in Sprites/Default line material legitimately
# has mainTex=NULL.
AUDIT_LOG="$REPO_ROOT/Deploy/obj/bundle-audit.log"
log "Auditing built bundle from its bytes (log: $AUDIT_LOG) ..."
set +e
COMMNEXTREDUX_AUDIT_BUNDLES="$BUILT" "$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod AuditCommNextReduxBundle.Run \
    -logFile - > "$AUDIT_LOG" 2>&1
AUDIT_EXIT=$?
set -e
if ! grep -q "\[audit\] done" "$AUDIT_LOG"; then
    log "Last lines of the audit log:"
    tail -30 "$AUDIT_LOG" | sed 's/^/    /'
    fail "bundle audit did not run to completion (Unity exit $AUDIT_EXIT) - see $AUDIT_LOG"
fi
grep -E "\[audit\]" "$AUDIT_LOG" | sed 's/^.*\[audit\]/[audit]/' | sed 's/^/    /'
CONTAINER_COUNT=$(sed -nE 's/^.*\[audit\]   container assets: ([0-9]+).*$/\1/p' "$AUDIT_LOG" | sed -n '1p')
[ -n "$CONTAINER_COUNT" ] || fail "the audit reported no container count - see $AUDIT_LOG"
[ "$CONTAINER_COUNT" -gt 0 ] || fail "the built bundle's container is EMPTY - the 0.2.8.5 player would reject it"

# --- the expected container set, DERIVED FROM THE SOURCES -------------------------------
# A hard-coded floor ("at least 29") goes stale the moment the UI tree changes, and it cannot see
# an asset that is silently absent while others take its place. This set is computed from the
# files the build is supposed to pack - every UXML page, the stylesheet and every image - so the
# gate states what the bundle must contain rather than how big it should be.
#
# PHASE 6 REMOVED THE D3 MATERIAL FROM THIS SET, and the removal is the point rather than a
# consequence: the line material is built in CODE at runtime now (LineMaterials, the sibling
# port's proven Shader.Find chain), so the bundle must carry NO material at all. Phase 2 authored
# one here and named it in this set, which made its absence a hard failure; the NO-MATERIAL
# assertion further down replaces that gate with a stricter one - any material in the bundle is
# now the failure, because a serialized shader reference cannot resolve in this player (P2's
# measurement, which stands) and nothing loads a bundled material any more.
EXPECTED="$REPO_ROOT/Deploy/obj/expected-container.txt"
{
    find "$SRC_UI_DIR" -type f \( -name '*.uxml' -o -name '*.uss' -o -name '*.png' \)
    # PHASE 7: the range-ruler sphere mesh always ships (a mesh is not a material), so its own
    # container path is named here and the gate below fails if it is absent. That is the offline
    # half of D34: the shipping bundle cannot lose the legacy mesh without failing its build.
    printf '%s\n' "Assets/CommNextRedux/Meshes/RulerSphere.fbx"
    if [ "$RENDER_ASSETS" = "1" ]; then
        find Tools/legacy-render-chain -type f ! -name '*.meta' \
            | sed 's|^Tools/legacy-render-chain/Shaders/|'"$D3_MAT_DIR"'/|; s|^Tools/legacy-render-chain/Meshes/|Assets/CommNextRedux/Meshes/|'
    fi
} | tr 'A-Z' 'a-z' | sort -u > "$EXPECTED"

sed -nE 's/^.*\[audit\]     container: (.*)$/\1/p' "$AUDIT_LOG" | sort -u > "$EXPECTED.actual"
MISSING=$(comm -23 "$EXPECTED" "$EXPECTED.actual" || true)
EXTRA=$(comm -13 "$EXPECTED" "$EXPECTED.actual" || true)
log "expected container assets: $(wc -l < "$EXPECTED")   in the bundle: $(wc -l < "$EXPECTED.actual")"
if [ -n "$MISSING" ]; then
    printf '%s\n' "$MISSING" | sed 's/^/    MISSING FROM BUNDLE: /'
    fail "the built bundle does not contain every asset the sources require - see $AUDIT_LOG"
fi
if [ -n "$EXTRA" ]; then
    printf '%s\n' "$EXTRA" | sed 's/^/    UNEXPECTED IN BUNDLE: /'
    if [ "$RENDER_ASSETS" != "1" ]; then
        fail "the shipping bundle packs assets outside the derived set - see $AUDIT_LOG"
    fi
fi

# A FONT that lost its shader or its main texture is the blank-window cause. Scoped to FONT lines
# only; see the block comment above for why materials are not covered by this grep.
if grep -E "\[audit\].*FONT " "$AUDIT_LOG" | grep -qE "shader=NULL|mainTex=NULL"; then
    fail "a font in the built bundle lost its shader or its main texture (blank-window cause) - see $AUDIT_LOG"
fi

# A MATERIAL that ships must have a resolvable shader - and as of P6 no material ships at all.
MAT_BAD=$(grep -E "\[audit\].*MAT '" "$AUDIT_LOG" | grep -c "shader=NULL" || true)
MAT_ALL=$(grep -cE "\[audit\].*MAT '" "$AUDIT_LOG" || true)
log "materials in the built bundle: $MAT_ALL checked, $MAT_BAD with shader=NULL"
[ "$MAT_BAD" -eq 0 ] || fail "$MAT_BAD material(s) shipped with shader=NULL - see $AUDIT_LOG"
if [ "$RENDER_ASSETS" = "1" ]; then
    # Evidence pass: the legacy ShaderGraph chain IS packed on purpose, so materials are expected -
    # the whole point of this pass is to record their shaders coming back NULL.
    log "COMMNEXTREDUX_RENDER_ASSETS=1 (evidence pass): materials are expected, so the NO-MATERIAL assertion is not applied"
else
    # The shipping bundle carries NO material: the line material is built in code at runtime. Two
    # independent routes to that fact - the audit's dedicated assertion, and the general material
    # dump above, which must also be empty. The second one is what would catch a material arriving
    # through a path nobody thought to name (a dependency of a UXML/stylesheet, say).
    grep -q "\[audit\]   NO-MATERIAL OK" "$AUDIT_LOG" \
        || fail "the audit did not confirm the shipping bundle carries 0 materials - see $AUDIT_LOG"
    [ "$MAT_ALL" -eq 0 ] \
        || fail "$MAT_ALL material(s) are packed in the shipping bundle; the line material is built at runtime and a packed material carries a shader reference this player cannot resolve - see $AUDIT_LOG"
fi

# PHASE 7: the ruler sphere mesh, asserted on the same terms as the material - from the delivered
# bytes, not from the project the build came from. `MESH FAIL` is raised by the audit when the mesh
# is absent, when no runtime read can reach it, and it is a hard failure either way: the runtime
# would silently draw a code-built sphere instead, which is D34's fallback rather than its choice.
MESH_FAIL=$(grep -cE "\[audit\].*MESH FAIL" "$AUDIT_LOG" || true)
[ "$MESH_FAIL" -eq 0 ] || {
    grep -E "\[audit\].*MESH FAIL" "$AUDIT_LOG" | sed 's/^/    /'
    fail "$MESH_FAIL 'MESH FAIL' line(s) in the audit - see $AUDIT_LOG"
}
grep -q "\[audit\]   MESH OK" "$AUDIT_LOG" \
    || fail "the audit did not confirm a reachable ruler mesh in the built bundle - the range rulers would fall back to the code-built sphere - see $AUDIT_LOG"
log "ruler mesh in the built bundle: $(grep -cE "\[audit\]     MESH '" "$AUDIT_LOG") mesh(es), reachable by one of the three runtime reads (MESH OK)"

# The markers, custom controls and dropdowns, asserted against an instance cloned from the
# DELIVERED bytes. Each of these is a LogError in the audit, so counting them is the gate.
  for pattern in "PAGE MISSING from the bundle" "PAGE CLONED EMPTY" "MARKER MISSING" \
                 "CONTROL NOT FOUND BY TYPE" "CONTROL TYPE MISMATCH" "CONTROL NAME DROPPED" \
                 "CONTROL NAME COLLISION" "DROPDOWN '.*' NOT FOUND" "PAGE .* Instantiate THREW"; do
    n=$(grep -cE "\[audit\].*$pattern" "$AUDIT_LOG" || true)
    if [ "$n" -ne 0 ]; then
        grep -E "\[audit\].*$pattern" "$AUDIT_LOG" | sed 's/^/    /'
        fail "the bundle audit reported $n '$pattern' failure(s) - see $AUDIT_LOG"
    fi
done
DROPDOWN_LINES=$(grep -cE "\[audit\]   DROPDOWN '" "$AUDIT_LOG" || true)
[ "$DROPDOWN_LINES" -eq 2 ] || fail "expected 2 dropdowns to be reported from the bundle, got $DROPDOWN_LINES - see $AUDIT_LOG"
log "container assets in the built bundle: $CONTAINER_COUNT (every expected asset present - the 24 UI assets + the range-ruler mesh, root page + markers + 3 custom controls + 2 dropdowns verified, no NULL font shader, no material packed, the ruler mesh reachable by the runtime's own reads)"

  # --- publish ----------------------------------------------------------------------------
  # The bundle AND its Unity-generated `.manifest` travel together. The manifest is the dependency
  # index BuildPipeline.BuildAssetBundles writes beside the bundle (the sibling projects K2D2 and
  # FlightPlan both track and ship theirs). The game does not load it - the runtime resolves the
  # bundle by the path the code asks for - but it is the artefact's own record of its contents, so
  # it is kept next to the bytes it describes rather than left in scratch.
  mkdir -p "$(dirname "$OUT_BUNDLE")"
  cp -f "$BUILT" "$OUT_BUNDLE"
  log "Wrote $OUT_BUNDLE ($(stat -c %s "$OUT_BUNDLE") bytes)"
  BUILT_MANIFEST="$BUILT.manifest"
  [ -f "$BUILT_MANIFEST" ] || fail "the build produced no manifest beside the bundle: $BUILT_MANIFEST"
  cp -f "$BUILT_MANIFEST" "$OUT_BUNDLE.manifest"
  log "Wrote $OUT_BUNDLE.manifest ($(stat -c %s "$OUT_BUNDLE.manifest") bytes)"

  # ALSO into the asset source tree, because Tools/build.sh begins its asset step with
  # `rm -rf "$OUT_DIR/assets"` and re-copies Assets/CommNextRedux/Copied/assets. Writing only to
  # $OUT_BUNDLE would make the DLL and the bundle mutually destructive: whichever build ran second
  # would delete the other's output, both exiting 0. With the bundle in Copied/, build.sh
  # reproduces the whole deployable tree on its own, so the order stops mattering.
  COPIED_BUNDLES="$REPO_ROOT/Assets/CommNextRedux/Copied/assets/bundles"
  mkdir -p "$COPIED_BUNDLES"
  cp -f "$BUILT" "$COPIED_BUNDLES/commnextredux_ui.bundle"
  cp -f "$BUILT_MANIFEST" "$COPIED_BUNDLES/commnextredux_ui.bundle.manifest"
  log "Wrote $COPIED_BUNDLES/commnextredux_ui.bundle (+ its .manifest) (so build.sh's asset step keeps it)"
  log ""
  log "Deploy with:"
  log "  cp -f \"$OUT_BUNDLE\" \"\$KSP2/mods/CommNextRedux/assets/bundles/commnextredux_ui.bundle\""
