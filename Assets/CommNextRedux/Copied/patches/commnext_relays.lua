-- CommNextRedux - the four dish antennas that act as relays.
--
-- LEGACY PROVENANCE
--   mods-outdated/CommNext/plugin_template/patches/configs/relay_module.patch
--   mods-outdated/CommNext/plugin_template/patches/libraries/_constants.patch    (ranges + EC rates)
--   mods-outdated/CommNext/plugin_template/patches/libraries/_relay_mixins.patch (`override-range`
--                                                                                 and `add-next-relay`)
--   mods-outdated/CommNext/plugin_template/patches/stages.patch                  (the ordering)
--
-- The `override-range` and `add-next-modulator` halves of the mixin library are translated in
-- `commnext_antennas.lua`; this file translates `add-next-relay` and uses both of the others.
-- Splitting them this way follows the legacy's own two config files rather than inventing a
-- third arrangement: `antennas.patch` is everything that is not a relay, `relay_module.patch` is
-- the four relays, and every rule in both ran in the same `setup-relays` stage.
--
-- ORDERING
--   Same as job B in `commnext_antennas.lua`: default pass and bucket, `:Before("OrbitalSurvey")`,
--   for the reason the legacy's `stages.patch` gives - the relay rows must be on the part before
--   OrbitalSurvey appends its rows. `:Before` is a mod-level constraint and applies to every
--   patch OrbitalSurvey registers, which is what the legacy's `@before "falki.orbital_survey"`
--   meant. Do not add `:First()` here: a cross-bucket relative order is silently ignored, so
--   combining the two would drop the constraint without failing.
--
-- THE EC RATE IS WRITTEN ONCE, INTO THE RELAY'S OWN MODULE DATA (Phase 5)
--   `Data_NextRelay.RequiredResource` is the ONE place this file puts the relay's rate. That field is
--   what `Data_NextRelay.SetupResourceRequest` builds the relay's own resource request from, and what
--   `PartComponentModule_NextRelay` posts as the request's `FlowUnits` every tick - so the relay draws
--   exactly what this table declares, through the game's own broker. `PartComponentModule_NextRelay`
--   also reads the rate back into its `relay-module` log marker, so a patch that matched nothing shows
--   up as a missing line rather than as a free relay.
--
--   THE STOCK TRANSMITTER'S LIST IS DELIBERATELY NOT TOUCHED. An earlier revision of this phase wrote
--   the relay rate into `Data_Transmitter.RequiredResources` and read
--   `PartComponentModule_DataTransmitter.IsTransmitterActive()` back as the power signal. Both halves
--   are refuted by the shipped IL: `IsTransmitterActive()` answers "is this dish deployed"
--   (`if (!_requiresDeployment) return true; return _dataDeployable.IsExtended;`), and
--   `PartComponentModule_DataTransmitter.OnUpdate` skips its own resource block unless `IsTransmitting`
--   - that list is the SCIENCE-transmission draw, so a relay would have cost nothing while relaying and
--   the write would have repriced the antenna's science use to the relay rate (ra-15: 25.0 -> 1.0 EC/s).
--   Do not write it again. The relay's cost belongs to the relay's module, and the full refutation is in
--   `Data_NextRelay.cs`'s header.
--
--   The after-launch check for these four parts is therefore: the relay module is present, the rate in
--   it is the table's rate, the request goes active while the relay runs, and a cleared EC store moves
--   the probe's `powerless=` off zero.

-- ============================================================================
-- Constants - the legacy `$commnext-RANGE-*` / `$commnext-EC-*` names
-- ============================================================================

-- The four relays, with the one EC rate each. `ec` is the SINGLE SOURCE of that part's rate: it is
-- written to `Data_NextRelay.RequiredResource`, which is both this mod's declaration and the rate the
-- relay's own resource request is built and posted from.
local RELAYS = {
    -- id, range, EC rate, modulator kind. Order is the legacy's.
    -- $commnext-RANGE-HG5   / $commnext-EC-HG5   - stock 200000000.0 (200M) -> 5M
    { id = "antenna_1v_dish_hg5",    range = 5000000.0,         ec = 0.2, kind = "DualBand" },
    -- $commnext-RANGE-RA2   / $commnext-EC-RA2   - stock 36000000000.0 (36G) -> 2G
    { id = "antenna_0v_dish_ra-2",   range = 2000000000.0,      ec = 0.5, kind = "OmniBand" },
    -- $commnext-RANGE-RA15  / $commnext-EC-RA15  - stock 86000000000.0 (86G) -> 15G
    { id = "antenna_0v_dish_ra-15",  range = 15000000000.0,     ec = 1.0, kind = "OmniBand" },
    -- $commnext-RANGE-RA100 / $commnext-EC-RA100 - stock 130000000000.0 (130G) -> 100G
    { id = "antenna_1v_dish_ra-100", range = 100000000000.0,    ec = 2.0, kind = "OmniBand" },
}

-- The stock resource name the game draws, and the acceptance threshold every stock row uses.
local RESOURCE_NAME = "ElectricCharge"
local RESOURCE_THRESHOLD = 0.1

-- A fresh `PartModuleResourceSetting` row for `Data_NextRelay.RequiredResource`. Built per use rather
-- than shared, the same way the visuals-override entries are: a table handed to the patcher becomes
-- JSON, and sharing one table across parts is how a later edit leaks sideways.
local function resourceSetting(rate)
    return {
        Rate = rate,
        ResourceName = RESOURCE_NAME,
        AcceptanceThreshold = RESOURCE_THRESHOLD,
    }
end

-- ============================================================================
-- Mixin translations (the relay-only halves)
-- ============================================================================

local function overrideRange(part, range)
    part:PatchModule("Module_DataTransmitter", function(module)
        module:PatchData("Data_Transmitter", function(data)
            data.CommunicationRange = range
        end)
    end)
end

-- `@mixin add-next-relay($ec-rate)`: add the relay module with its resource declaration, and
-- give it a row in the part action menu ahead of the modulator's.
--
-- `RequiredResource` is a stock `PartModuleResourceSetting`, written with the same three fields and
-- the same `AcceptanceThreshold` the legacy used and that the live port ships
-- (`pm_cache/parts_data.zip -> antenna_0v_dish_ra-2`: `{"Rate": 0.5, "ResourceName":
-- "ElectricCharge", "AcceptanceThreshold": 0.1}`). It is the relay's cost, full stop: the module's own
-- request is built from it and draws it through the game's broker, so there is no second write.
local function makeVisualsOverride()
    return {
        PartComponentModuleName = "PartComponentModule_NextRelay",
        ModuleDisplayName = "PartModules/NextRelay/Name",
        ShowHeader = true,
        ShowFooter = true,
    }
end

local function addRelay(part, ecRate)
    part:AddModule("Module_NextRelay", function(module)
        module:AddData("Data_NextRelay", function(data)
            data.RequiredResource = resourceSetting(ecRate)
        end)
    end)

    if part.PAMModuleVisualsOverride ~= nil then
        part.PAMModuleVisualsOverride:Append(makeVisualsOverride())
    else
        part.PAMModuleVisualsOverride = { makeVisualsOverride() }
    end
end

-- `@mixin add-next-modulator($kind)` - identical to the one in `commnext_antennas.lua`, repeated
-- because a patch file is a standalone script. The relay jobs need it: the legacy gave each of
-- these four antennas a modulator as well, and always *after* the relay, so the part action menu
-- shows Signal Relay first.
local function addModulator(part, kind)
    part:AddModule("Module_NextModulator", function(module)
        module:AddData("Data_NextModulator", function(data)
            data.ModulatorKind = kind
        end)
    end)

    local visuals = {
        PartComponentModuleName = "PartComponentModule_NextModulator",
        ModuleDisplayName = "PartModules/NextModulator/Name",
        ShowHeader = true,
        ShowFooter = true,
    }

    if part.PAMModuleVisualsOverride ~= nil then
        part.PAMModuleVisualsOverride:Append(visuals)
    else
        part.PAMModuleVisualsOverride = { visuals }
    end
end

-- ============================================================================
-- Job C - the four relays: range, cost, relay and modulator
-- (legacy: `setup-relays`; the cost is the relay's own since Phase 5)
-- ============================================================================

for _, relay in ipairs(RELAYS) do
    PM.Parts:Patch("Relay_" .. relay.id)
            :Named(relay.id)
            :Before("OrbitalSurvey")
            :Do(function(part)
                overrideRange(part, relay.range)
                addRelay(part, relay.ec)
                addModulator(part, relay.kind)
            end)
end
