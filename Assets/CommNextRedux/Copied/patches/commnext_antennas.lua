-- CommNextRedux - the antenna ranges, and the modulator every transmitter carries.
--
-- LEGACY PROVENANCE (all paths under mods-outdated/CommNext/plugin_template/patches/)
--   configs/antennas.patch        - three of the four jobs, translated below
--   libraries/_constants.patch    - the ranges, as the named locals at the top
--   libraries/_relay_mixins.patch - `override-range` and `add-next-modulator`, as the two
--                                   helpers below (the relay half of the mixin is in
--                                   commnext_relays.lua, which is where it is used)
--   stages.patch                  - the ordering, reproduced with patch ordering rather than
--                                   a stage definition; see ORDERING
--
-- ORDERING - the legacy stage intent, in the Lua patch model
--   The legacy declared two stages: `setup-relays` before the mod GUID `falki.orbital_survey`
--   and `cleanup-relays` after `setup-relays`. Its own comment says why: the relay rows have
--   to be on the part before OrbitalSurvey appends its rows, or the Part Details window pushes
--   them onto a second page.
--
--   The Lua model has three ordering tools and they do not mix: a pass (`Early`/`Default`/
--   `Late`), an ordering bucket within that pass (`First`/`Default`/`Last`), and a relative
--   order against named patches or mods. Cross-bucket relative targets are silently ignored -
--   so the blanket job, which must run before everything, uses `:First()` alone, and the
--   three jobs that need to beat OrbitalSurvey stay in the default bucket and use
--   `:Before("OrbitalSurvey")`. That reproduces:
--
--     job A (blanket pod range)   legacy first rule in `setup-relays`  -> :First()
--     job B (5 named antennas)    legacy `setup-relays`               -> :Before("OrbitalSurvey")
--     job D (leftover modulators) legacy `cleanup-relays`             -> :Last()
--
--   `OrbitalSurvey` is the patch-manager mod id of the live OrbitalSurvey install, read from
--   `$KSP2_ROOT/pm_summary.log` ("Recognized Mod IDs", and the `OrbitalSurvey:<patch>` id
--   prefix). The legacy's `falki.orbital_survey` was that mod's SpaceWarp GUID, which does not
--   appear in the patch manager at all.
--
-- WHY `:First()` AND NOT `:Early()`
--   A pass is a full sweep over every label before the next pass starts; a bucket only orders
--   the patches already running in the same sweep. The legacy's blanket rule was first in its
--   stage, not in a separate sweep, so a bucket is the faithful translation. Using `:Early()`
--   would additionally put this patch ahead of other mods' early passes, which is a change no
--   part of the legacy asked for.
--
-- THE BLANKET SET IS A CONJUNCTION, AND IT IS ENFORCED IN THE BODY, NOT BY A FILTER
--   The two blanket jobs carry **no `:parts` filter**: they register against every part and the
--   conjunction is the `if` inside `:Do`. Measured against the game's own serialisation
--   ($KSP2_ROOT/pm_cache/parts_data.zip, 426 parts): 37 carry a transmitter, 29 carry a command
--   module, 28 carry both - and those 28 are all `pod_*`, `probe_*` or `cockpit_*` command parts.
--
--   WHY THE DISTINCTION MATTERS - it changes what PatchManager's report means (F49)
--     PatchManager counts a patch once per part it **runs on**, not once per part it **changes**.
--     So the report reads `CommNextRedux:PodRanges` 426 times and
--     `CommNextRedux:LeftoverModulators` 426 times, while the effect is 28 parts each -
--     9 named antennas + 426 + 426 = **861 APPLIED** in L6 run 2. A reader who expects 11 (one
--     per registered name) sees 861 and concludes the patches over-applied. They did not:
--     the after-state proves exactly 28 parts were reset and exactly 37 carry a modulator.
--     `Deploy/obj/p4-after-audit.txt` and `p4-after-ranges.txt` are that proof.
--
--   An earlier revision of this comment described a `:parts .Module_DataTransmitter
--   .Module_Command` filter that was never in the code; the invocation count is the tell.
--
-- EVERY PART ID BELOW IS RESOLVED, NOT REMEMBERED
--   All five ids were checked against `$KSP2_ROOT/pm_cache/inventory.json` (426 ids). The one
--   divergence from the legacy source is `antenna_1v_dish_hg55`: the legacy wrote
--   `antenna_1v_dish_hg55s`, which is not in the inventory and never has been, so that rule
--   never matched and its range was never applied. See `Deploy/obj/divergences.md` (D2).
--
-- MODULE AND DATA TYPE NAMES ARE RESOLVED TOO
--   `Module_NextModulator` and `Data_NextModulator` are the short names of
--   `CommNextRedux.Modules.Modulator.Module_NextModulator` and `...Data_NextModulator`, which
--   this mod declares. The patch manager accepts a component module's short name with or
--   without the game's `PartComponent` prefix - `Module_ScienceExperiment` resolves to
--   `PartComponentModule_ScienceExperiment` in the live OrbitalSurvey patch, whose result is
--   visible in the same cache. Both halves are asserted after launch: a name that resolves to
--   nothing does not fail, it no-ops, and the report in `$KSP2_ROOT/pm_summary.log` plus the
--   part data are what catch it.

-- ============================================================================
-- Constants - the legacy `$commnext-*` names, kept for grep-ability
-- ============================================================================

-- $commnext-RANGE-POD - stock pods had 200000000.0 (200M)
local RANGE_POD = 5000.0

-- The specific antennas from `configs/antennas.patch`, in the legacy's own order.
-- `$commnext-RANGE-C16` / `-C16S` / `-HG55` / `-DTS-M1` / `-C88`.
local ANTENNAS = {
    { id = "antenna_0v_16",               range = 500000.0,       kind = "MonoBand" },
    { id = "antenna_0v_16s",              range = 500000.0,       kind = "MonoBand" },
    { id = "antenna_1v_dish_hg55",        range = 15000000000.0,  kind = "DualBand" },
    { id = "antenna_1v_parabolic_dts-m1", range = 2000000000.0,   kind = "DualBand" },
    { id = "antenna_1v_dish_88-88",       range = 100000000000.0, kind = "OmniBand" },
}

-- ============================================================================
-- Mixin translations
-- ============================================================================

-- `@mixin override-range($range)`: set the range on the part's existing transmitter data.
-- `PatchModule` does nothing when the module is absent and `PatchData` does nothing when the
-- data entry is absent, which is exactly the mixin's semantics - no guard is needed, and adding
-- one would only hide a name that failed to resolve.
local function overrideRange(part, range)
    part:PatchModule("Module_DataTransmitter", function(module)
        module:PatchData("Data_Transmitter", function(data)
            data.CommunicationRange = range
        end)
    end)
end

-- A fresh part-action-menu visuals override. Built per use, like the OrbitalSurvey patch that
-- this shape is copied from: the entry is JSON-backed and must not be shared between parts.
local function makeVisualsOverride()
    return {
        PartComponentModuleName = "PartComponentModule_NextModulator",
        ModuleDisplayName = "PartModules/NextModulator/Name",
        ShowHeader = true,
        ShowFooter = true,
    }
end

-- `@mixin add-next-modulator($kind)`: add the modulator module, set its kind, and give it a row
-- in the part action menu. `ModulatorKind` is an enum-typed field, so the value is the member
-- name - see `Code/Modules/Modulator/ModulatorKind.cs` for the measurement behind that.
local function addModulator(part, kind)
    part:AddModule("Module_NextModulator", function(module)
        module:AddData("Data_NextModulator", function(data)
            data.ModulatorKind = kind
        end)
    end)

    if part.PAMModuleVisualsOverride ~= nil then
        part.PAMModuleVisualsOverride:Append(makeVisualsOverride())
    else
        part.PAMModuleVisualsOverride = { makeVisualsOverride() }
    end
end

-- ============================================================================
-- Job A - reset every crewed command part's range (legacy: first rule of `setup-relays`)
-- ============================================================================

PM.Parts:Patch("PodRanges")
        :First()
        :Do(function(part)
            if part:HasModule("Module_DataTransmitter") and part:HasModule("Module_Command") then
                overrideRange(part, RANGE_POD)
            end
        end)

-- ============================================================================
-- Job B - the five named antennas: range + modulator (legacy: `setup-relays`)
-- ============================================================================

for _, antenna in ipairs(ANTENNAS) do
    PM.Parts:Patch("Antenna_" .. antenna.id)
            :Named(antenna.id)
            :Before("OrbitalSurvey")
            :Do(function(part)
                overrideRange(part, antenna.range)
                addModulator(part, antenna.kind)
            end)
end

-- ============================================================================
-- Job D - a modulator on every remaining transmitter (legacy: `cleanup-relays`)
-- ============================================================================

-- Transmitters that no earlier job gave a modulator to: the body guard is
-- `HasModule(Module_DataTransmitter) and not HasModule(Module_NextModulator)`, evaluated per
-- part. No `:parts` filter - see "THE BLANKET SET IS A CONJUNCTION ..." above for why that
-- makes the report read 426 invocations for 28 effects. 28 of the 37 transmitters are in that
-- state at this point (the 9 patched antennas already have one), and they are the parts whose
-- only CommNext surface is the range reset above. Verified end state: 37 of 37 transmitters
-- carry a modulator, 0 non-transmitter parts carry one.
PM.Parts:Patch("LeftoverModulators")
        :Last()
        :Do(function(part)
            if part:HasModule("Module_DataTransmitter") and not part:HasModule("Module_NextModulator") then
                addModulator(part, "MonoBand")
            end
        end)
