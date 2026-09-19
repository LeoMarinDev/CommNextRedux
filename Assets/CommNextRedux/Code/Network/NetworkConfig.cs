// CommNextRedux - the network core's configuration surface.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Utils/PluginSettings.cs bound these keys through
//   BepInEx's ConfigEntry. BepInEx does not exist on Redux 0.2.8.5, so every entry here is
//   re-bound through ReduxLib.Configuration exactly as the sibling port does
//   (mods/CommLinesRedux/Assets/CommLinesRedux/Code/Utilities/ModSettings.cs).
//
// KEYS PRESERVED FROM THE LEGACY (same section, same key):
//   Network / "Best path mode"     -> BestPathMode.NearestRelay
//   Network / "KSC range"          -> KscRangeMode.G2
//   Network / "Occlusion radius"   -> 0.98
//   Network / "Relays require power" -> true        (bound by Phase 5, which owns the feature)
//   Debug / "Prints in Player.log the time it takes to compute the network"
//                                  -> **false, where the legacy shipped true** (D25). The key and
//                                     its section are the legacy's; only the default differs, and
//                                     the reason is this port's standing rule that a diagnostic is
//                                     opt-in. It is a genuine second job, not a duplicate of
//                                     "Network probe": that logs a structural block per rebuild,
//                                     this logs the pass *duration*. Node *naming* - the legacy's
//                                     other use of the same flag - is gated on the probe instead
//                                     (see NetworkEngine's `Name = probe ? body.bodyName : null`),
//                                     because the probe is what needs the names.
//
// KEYS NEW IN THE PORT
//   Network / "Enable CommNext network" - the master off-switch, user decision D1. It has no
//                                       legacy counterpart by design: it exists so a user can
//                                       hand the whole graph back to the game without uninstalling.
//   Debug / "Network probe"           - the D9 diagnostic probe.
//   Map / "Map toolbar X"             - half of the map toolbar's saved position (P9/D53). The
//   Map / "Map toolbar Y"               legacy persisted nothing of the sort - it had no
//                                       persistence path at all - so neither key has a
//                                       counterpart to preserve. They are written by the toolbar
//                                       itself when a drag is followed by leaving the map view,
//                                       and read back when the map next opens.
//
// A KEY REMOVED FROM THE CODE IS REMOVED FROM THE FILE on the next run - `JsonConfigSection.WriteTo`
// walks its own bound-entry dictionary, so a stale key left by an older build is dropped after one
// session. No migration script is needed; verify the key is gone *after* a session, not before.

using CommNextRedux.Network.Bands;
using CommNextRedux.Rendering;
using ReduxLib.Configuration;
using UnityEngine;

namespace CommNextRedux.Network
{
    /// <summary>
    /// How the cost of a path is measured. Bound by name, so the enum member names are the
    /// user-visible values and must not be renamed.
    /// </summary>
    /// <remarks>
    /// Legacy: <c>PluginSettings.BestPathMode</c>, same two members in the same order. The legacy
    /// carried <c>[Description]</c> attributes, which were BepInEx's dropdown labels; ReduxLib's
    /// settings UI renders the member name instead, so the names are chosen to read as labels.
    /// </remarks>
    public enum BestPathMode
    {
        /// <summary>The best path is the one with the lowest accumulated cost to the KSC.</summary>
        /// <remarks>
        /// This is the mode the GAME ITSELF computes: <c>GetConnectedNodesJob.UpdateGraph</c> relaxes
        /// on <c>cheapestCosts[source] + distancesq(source, target)</c> and writes that same
        /// accumulated value into <c>cheapestCosts[target]</c> (IL proof: <c>IL_01b3</c>-<c>IL_01cb</c>
        /// of that method). Selecting it with occlusion disabled is therefore the configuration in
        /// which this port's own tabulation must match the game's job exactly - which is what the
        /// probe's oracle block measures.
        /// </remarks>
        ShortestKSC = 0,

        /// <summary>The best path is the one with the minimum distance between adjacent nodes.</summary>
        /// <remarks>
        /// The legacy's default. It is a <b>non-monotone</b> cost - a longer hop from the KSC can
        /// produce a smaller optimum - so the greedy "never revisit a processed node" relaxation
        /// makes the result depend on visit order. The game never computes this mode, so the
        /// oracle cannot vouch for it; it can only vouch for <see cref="ShortestKSC"/>.
        /// </remarks>
        NearestRelay = 1,
    }

    /// <summary>
    /// The KSC's maximum range. Bound by name, so the enum member names are the user-visible values.
    /// </summary>
    /// <remarks>
    /// Legacy: <c>PluginSettings.KSCRangeMode</c>, same three members in the same order, same
    /// default. The three ranges are the legacy's own constants (below), not re-derived.
    /// </remarks>
    public enum KscRangeMode
    {
        /// <summary>2 Gm - the legacy's default.</summary>
        G2 = 0,

        /// <summary>10 Gm.</summary>
        G10 = 1,

        /// <summary>50 Gm.</summary>
        G50 = 2,
    }

    /// <summary>
    /// A constraint that accepts exactly the two <see cref="BestPathMode"/> members.
    /// </summary>
    /// <remarks>
    /// <see cref="ListConstraint{T}"/>'s measured constraint is <c>where T : IEquatable&lt;T&gt;</c>,
    /// which no C# enum satisfies, so the public <see cref="ValueConstraint{T}"/> base is subclassed
    /// instead - the same route the sibling port took and proved in game. The value is also not
    /// *validated* anywhere that matters: ReduxLib's config reader falls back to the default when a
    /// saved string cannot be converted, and this engine's own default case treats an unhandled
    /// member as <see cref="BestPathMode.NearestRelay"/>.
    /// </remarks>
    public sealed class BestPathModeConstraint : ValueConstraint<BestPathMode>
    {
        /// <inheritdoc/>
        public override bool IsValid(BestPathMode o) =>
            o == BestPathMode.ShortestKSC || o == BestPathMode.NearestRelay;

        /// <inheritdoc/>
        public override string ConstraintDescription => "Accepts: ShortestKSC, NearestRelay";
    }

    /// <summary>
    /// A constraint that accepts exactly the three <see cref="KscRangeMode"/> members.
    /// </summary>
    public sealed class KscRangeModeConstraint : ValueConstraint<KscRangeMode>
    {
        /// <inheritdoc/>
        public override bool IsValid(KscRangeMode o) =>
            o == KscRangeMode.G2 || o == KscRangeMode.G10 || o == KscRangeMode.G50;

        /// <inheritdoc/>
        public override string ConstraintDescription => "Accepts: G2, G10, G50";
    }

    /// <summary>
    /// A constraint that accepts exactly the three <see cref="RulersDisplayMode"/> members.
    /// </summary>
    /// <remarks>
    /// Phase 7's addition, and the reason it exists at all is the same one as the two above: no C#
    /// enum satisfies <c>ListConstraint&lt;T&gt;</c>'s <c>IEquatable&lt;T&gt;</c> constraint, so the
    /// constraint is written by hand. The member names are what the settings UI shows and what the
    /// config file stores (ReduxLib round-trips enums by NAME through its <c>StringEnumConverter</c>),
    /// so they are user-facing text - the enum's own remark owns that contract.
    /// </remarks>
    public sealed class RulersDisplayModeConstraint : ValueConstraint<RulersDisplayMode>
    {
        /// <inheritdoc/>
        public override bool IsValid(RulersDisplayMode o) =>
            o == RulersDisplayMode.None || o == RulersDisplayMode.Relays || o == RulersDisplayMode.All;

        /// <inheritdoc/>
        public override string ConstraintDescription => "Accepts: None, Relays, All";
    }

    /// <summary>
    /// A constraint that accepts exactly the three <see cref="ConnectionsDisplayMode"/> members.
    /// </summary>
    /// <remarks>
    /// Phase 8a's addition, the twin of <see cref="RulersDisplayModeConstraint"/> and for the same
    /// measured reason: no C# enum satisfies <c>ListConstraint&lt;T&gt;</c>'s
    /// <c>IEquatable&lt;T&gt;</c> constraint, so the constraint is written by hand. The member names
    /// are what the settings UI shows and what the config file stores (ReduxLib round-trips enums by
    /// NAME through its <c>StringEnumConverter</c>), so they are user-facing text - the enum's own
    /// remark owns that contract, and the legacy's own display names are the tooltip strings in
    /// <c>LocalizedStrings</c>, not these.
    /// </remarks>
    public sealed class ConnectionsDisplayModeConstraint : ValueConstraint<ConnectionsDisplayMode>
    {
        /// <inheritdoc/>
        public override bool IsValid(ConnectionsDisplayMode o) =>
            o == ConnectionsDisplayMode.None || o == ConnectionsDisplayMode.Lines
            || o == ConnectionsDisplayMode.Active;

        /// <inheritdoc/>
        public override string ConstraintDescription => "Accepts: None, Lines, Active";
    }

    /// <summary>
    /// Every setting the network core reads, bound to <see cref="ReduxLib.Configuration"/> through
    /// <see cref="CommNextReduxPlugin.SWConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bind-time literal types are load-bearing.</b> <c>IConfigFile.Bind&lt;T&gt;</c> infers
    /// <c>T</c> from the default argument's compile-time type, and
    /// <c>new ConfigValue&lt;T&gt;(entry)</c> throws <see cref="System.ArgumentException"/> when the
    /// two do not match exactly - a trap the FlightPlan port lost a launch to. Every default below is
    /// therefore a <c>const</c> of the intended type, never a bare literal.
    /// </para>
    /// <para>
    /// <b>Every accessor falls back to its <c>*Default</c> constant when unbound.</b> The patches
    /// read these values on the game's own graph-rebuild path, which can in principle run before
    /// <see cref="Initialize"/> has bound anything; a <c>null</c> dereference there would surface as
    /// "the mod is dead". <see cref="Initialize"/> runs from
    /// <see cref="CommNextReduxPlugin.OnPreInitialized"/> <i>before</i> Harmony is installed, so the
    /// fallback is a belt-and-braces path, not an expected one - and the plugin logs the resolved
    /// values so a mis-bind is visible instead of silent.
    /// </para>
    /// </remarks>
    public static class NetworkConfig
    {
        // -------------------------------------------------------------------------------------
        // Section and key names. The literals are the user-facing file layout; do not re-case.
        // -------------------------------------------------------------------------------------

        /// <summary>The section holding the network behaviour settings.</summary>
        public const string NetworkSection = "Network";

        /// <summary>The section holding the diagnostics.</summary>
        public const string DebugSection = "Debug";

        /// <summary>
        /// The section holding the map-display settings.
        /// </summary>
        /// <remarks>
        /// Phase 7's addition. A new section rather than an entry under <c>Network</c> because these
        /// settings change nothing about the network itself - they change what the map draws - and
        /// because the legacy had no config entry for the ruler mode at all (its switch was an AppBar
        /// button), so there is no legacy key to inherit.
        /// </remarks>
        public const string MapSection = "Map";

        /// <summary>
        /// The section holding the map connection lines' appearance (U6g).
        /// </summary>
        /// <remarks>
        /// A new section rather than eight more entries under <see cref="MapSection"/>, and for the
        /// same reason <see cref="MapSection"/> is itself separate from <see cref="NetworkSection"/>:
        /// every key here changes only what the map draws, and the eight of them read as one group in
        /// the settings page - "the line colours, then how solid the lines are". <c>Map</c> already
        /// answers *which* lines are drawn (the two display modes, the game's-own-lines override);
        /// this section answers *what they look like*, and mixing the two would put the colour fields
        /// between the mode dropdowns and the toolbar position.
        /// </remarks>
        public const string LinesSection = "Lines";

        /// <summary>Key of <see cref="Enable"/>, in <see cref="NetworkSection"/>.</summary>
        public const string EnableKey = "Enable CommNext network";

        /// <summary>Key of <see cref="BestPath"/>, in <see cref="NetworkSection"/>. Legacy key.</summary>
        public const string BestPathKey = "Best path mode";

        /// <summary>Key of <see cref="KscRange"/>, in <see cref="NetworkSection"/>. Legacy key.</summary>
        public const string KscRangeKey = "KSC range";

        /// <summary>Key of <see cref="OcclusionRadius"/>, in <see cref="NetworkSection"/>. Legacy key.</summary>
        public const string OcclusionRadiusKey = "Occlusion radius";

        /// <summary>
        /// Key of <see cref="RelaysRequirePower"/>, in <see cref="NetworkSection"/>. Legacy key.
        /// </summary>
        public const string RelaysRequirePowerKey = "Relays require power";

        /// <summary>Key of <see cref="Probe"/>, in <see cref="DebugSection"/>.</summary>
        public const string ProbeKey = "Network probe";

        /// <summary>
        /// Key of <see cref="EnableProfileLogs"/>, in <see cref="DebugSection"/>. Legacy key - the
        /// literal is the legacy's own sentence, kept verbatim so an existing config file's key is
        /// still recognised.
        /// </summary>
        public const string ProfileLogsKey =
            "Prints in Player.log the time it takes to compute the network";

        /// <summary>Key of <see cref="Rulers"/>, in <see cref="MapSection"/>. Phase 7's key.</summary>
        public const string RulersModeKey = "Rulers mode";

        /// <summary>
        /// Key of <see cref="Connections"/>, in <see cref="MapSection"/>. Phase 8a's key.
        /// </summary>
        /// <remarks>
        /// Under the same <see cref="MapSection"/> as the ruler mode because it is the same kind of
        /// setting - what the map draws - and because until P8a the connection mode had no config
        /// entry at all: its only writer was the renderer's own property, which nothing in a shipped
        /// build could reach. This entry is what the toolbar's lines button now writes (D39).
        /// </remarks>
        public const string ConnectionsModeKey = "Connections mode";

        /// <summary>
        /// Key of <see cref="HideGameCommLines"/>, in <see cref="MapSection"/>. The D-L21-1 key.
        /// </summary>
        /// <remarks>
        /// Under the same <see cref="MapSection"/> as the two display modes because it is the same
        /// kind of setting - what the map draws - even though its mechanism is nothing like theirs:
        /// it is a Harmony postfix on the game's own getter
        /// (<c>CommNextRedux.Patches.StockCommNetLinesPatch</c>) rather than a renderer property.
        /// The literal deliberately uses the game's own wording ("CommNet lines", the term its
        /// Gameplay settings label uses) so a player recognises what is being hidden, and it names
        /// the lines as the game's own so the setting cannot be mistaken for the mod's own drawing.
        /// </remarks>
        public const string HideGameCommLinesKey = "Hide the game's own CommNet lines";

        /// <summary>
        /// Key of <see cref="MapToolbarX"/>, in <see cref="MapSection"/>. P9's key.
        /// </summary>
        /// <remarks>
        /// The pair <see cref="MapToolbarXKey"/>/<see cref="MapToolbarYKey"/> is the port's whole
        /// persistence story for the toolbar's position, and it lives here rather than in a save
        /// file for the reason D53 records: the two map display modes already persist as config
        /// values - verified round-tripping through <c>Settings -&gt; Mods</c> at L10 and L11 - so a
        /// second persistence path for the same window would be two sources of truth.
        /// </remarks>
        public const string MapToolbarXKey = "Map toolbar X";

        /// <summary>
        /// Key of <see cref="MapToolbarY"/>, in <see cref="MapSection"/>. P9's key - the twin of
        /// <see cref="MapToolbarXKey"/>, and written by the same call.
        /// </summary>
        public const string MapToolbarYKey = "Map toolbar Y";

        // -------------------------------------------------------------------------------------
        // U6g's line-appearance keys, in LinesSection. One colour per band plus the two fallbacks
        // the renderer already has, and the opacity multiplier. Spellings are American ("color"),
        // matching every other key in this file; the section name is the group heading the player
        // sees above them.
        // -------------------------------------------------------------------------------------

        /// <summary>Key of <see cref="XBandColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string XBandColorKey = "X band color";

        /// <summary>Key of <see cref="SBandColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string SBandColorKey = "S band color";

        /// <summary>Key of <see cref="KBandColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string KBandColorKey = "K band color";

        /// <summary>Key of <see cref="KaBandColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string KaBandColorKey = "Ka band color";

        /// <summary>Key of <see cref="VBandColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string VBandColorKey = "V band color";

        /// <summary>Key of <see cref="RelayHopColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string RelayHopColorKey = "Relay hop color";

        /// <summary>Key of <see cref="OtherLinksColor"/>, in <see cref="LinesSection"/>.</summary>
        public const string OtherLinksColorKey = "Other links color";

        /// <summary>Key of <see cref="LineOpacity"/>, in <see cref="LinesSection"/>.</summary>
        public const string LineOpacityKey = "Line opacity";

        // -------------------------------------------------------------------------------------
        // Defaults. `const` of the exact bound type - see the class remarks.
        // -------------------------------------------------------------------------------------

        /// <summary>Default of <see cref="Enable"/>: the mod's network replaces the game's.</summary>
        public const bool EnableDefault = true;

        /// <summary>Default of <see cref="BestPath"/>, matching the legacy.</summary>
        public const BestPathMode BestPathDefault = BestPathMode.NearestRelay;

        /// <summary>Default of <see cref="KscRange"/>, matching the legacy.</summary>
        public const KscRangeMode KscRangeDefault = KscRangeMode.G2;

        /// <summary>Default of <see cref="OcclusionRadius"/>, matching the legacy.</summary>
        public const double OcclusionRadiusDefault = 0.98;

        /// <summary>
        /// Default of <see cref="RelaysRequirePower"/>, matching the legacy.
        /// </summary>
        /// <remarks>
        /// <c>true</c> is the legacy's default and the feature's whole point: a relay that costs
        /// nothing to run is a relay that cannot be starved, which is the mechanic being restored.
        /// This port reads the entry per tick and per pass - see <see cref="RelaysRequirePower"/> -
        /// so unlike the legacy's, a change needs no reload.
        /// </remarks>
        public const bool RelaysRequirePowerDefault = true;

        /// <summary>
        /// Default of <see cref="EnableProfileLogs"/> - <b><c>false</c>, where the legacy shipped
        /// <c>true</c></b> (divergence D25).
        /// </summary>
        /// <remarks>
        /// The legacy's own description ("If true, you can help me profiling this thing.") says
        /// what it is: the author collecting timings from users. This port's standing rule is that
        /// a diagnostic ships off, so the default is inverted and the divergence is recorded rather
        /// than quietly inherited.
        /// </remarks>
        public const bool ProfileLogsDefault = false;

        /// <summary>
        /// Smallest accepted <see cref="OcclusionRadius"/>. <c>0</c> is a real off-switch here -
        /// see the remarks on <see cref="OcclusionRadius"/>.
        /// </summary>
        public const double OcclusionRadiusMinimum = 0.0;

        /// <summary>Largest accepted <see cref="OcclusionRadius"/>.</summary>
        public const double OcclusionRadiusMaximum = 1.0;

        /// <summary>
        /// Default of <see cref="Probe"/>: off, the port's standing rule for every diagnostic.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Flipped from <c>true</c> at the 0.2.9.0.104521 update (F93).</b> The <c>true</c> was a
        /// Phase 3 convenience - that phase's whole deliverable was diagnostic evidence and its L3
        /// launch had to produce it without an extra setup step - and it was recorded as the port's
        /// only ever on-by-default diagnostic, to be turned off before release. It is off now: a
        /// diagnostic is opt-in, and a shipped config must not carry it on.
        /// </para>
        /// <para>
        /// <b>An existing config's <c>true</c> is still honoured.</b> The default only decides a key
        /// the generated file does not carry: <see cref="ProbeEnabled"/> returns
        /// <c>value == null ? ProbeDefault : value.Value</c>, and <see cref="Probe"/> binds
        /// <see cref="ProbeKey"/> under the file's own <c>Debug</c> section. A player whose
        /// <c>CommNextRedux-config.json</c> already carries <c>"Network probe": true</c> keeps the
        /// probe exactly as it was; only a fresh file (or a player who never turned it on) starts
        /// quiet. The key, its section and the whole probe-on behaviour are unchanged.
        /// </para>
        /// </remarks>
        public const bool ProbeDefault = false;

        /// <summary>
        /// Default of <see cref="Rulers"/>: the legacy's own default - relays only.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why the port ships this ON, where Phase 6 deliberately shipped the mode off.</b> P6
        /// carried <see cref="RulersDisplayMode"/> as state with no geometry behind it, and defaulting
        /// to <c>Relays</c> then would have been a mode that claimed to be on with nothing drawing it.
        /// P7 gives the mode its geometry, so that reason is gone - and the two reasons to restore the
        /// legacy's default are that it is the legacy's own shipped behaviour, and that a mode which
        /// defaults to <c>None</c> is a feature no player can reach (there is no window and no key
        /// binding in this build that sets it). The settings UI now owns the switch: this entry is
        /// bound, it is live, and <c>None</c> prunes every ruler already drawn.
        /// </para>
        /// <para>
        /// <b>The cost of the choice, stated plainly:</b> a player who has never opened
        /// <c>Settings -&gt; Mods</c> gets a translucent sphere on every relay the moment they enter
        /// the map view. That is what the legacy did, and it is one config edit away from off.
        /// </para>
        /// </remarks>
        public const RulersDisplayMode RulersModeDefault = RulersDisplayMode.Relays;

        /// <summary>
        /// Default of <see cref="Connections"/>: <c>Lines</c> - the renderer's own field default and
        /// the mode every launch before this phase ran in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why <c>Lines</c> and not <c>None</c>.</b> Every log this port has measured - L6, L8,
        /// L9 - reports <c>mode=Lines</c> on every line, because before P8a the renderer's field
        /// default was the only value the mode could have. Making the config's default the same keeps
        /// a player's map and their saved config agreeing on the first run, which is the whole point
        /// of a default: a config file generated with <c>None</c> would silently turn a working
        /// feature off the moment the entry started being applied.
        /// </para>
        /// <para>
        /// <b>The cost, stated plainly:</b> the first time this build runs, a player who does not want
        /// the lines has to turn them off once - in the map (the toolbar button) or in
        /// <c>Settings -&gt; Mods</c>. Both write the same key.
        /// </para>
        /// </remarks>
        public const ConnectionsDisplayMode ConnectionsModeDefault = ConnectionsDisplayMode.Lines;

        /// <summary>
        /// Default of <see cref="HideGameCommLines"/>: <c>true</c> - the game's own lines are hidden,
        /// and CommNext's band-coloured lines are the only ones drawn.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The user's approved decision for D-L21-1, and it is the whole point of the key.</b> The
        /// game ships its own line drawer (<c>KSP.Map.CommNetLineRenderer</c>), it defaults ON, and it
        /// draws every edge of the graph as one hardcoded green line with no band or occlusion
        /// styling. Because this mod replaces the graph the drawer reads, the player sees the mod's
        /// own tree twice: once in band colours and once in flat green on top of it - which is why
        /// the "Connections mode" setting read as having no effect before this key existed.
        /// </para>
        /// <para>
        /// <b>What the override does and does not do.</b> While <c>true</c>, a postfix on the game's
        /// static <c>PersistentProfileManager.get_ShowCommNetLines()</c> reports the game's own value
        /// as off, so the game's drawer takes its own early-out after it has cleared its command
        /// buffer. The persisted <c>PersistentSettings.ShowCommNetLines</c> value is never written:
        /// the player's saved choice survives untouched and returns when this mod is removed or this
        /// key is turned off. The override is independent of <see cref="Enable"/> - the master switch
        /// changes whose graph is used, while this entry only stops the game's drawer from painting
        /// over whatever is there.
        /// </para>
        /// <para>
        /// <b>The side effect, stated plainly.</b> The game's own Settings -&gt; Gameplay -&gt; "Show
        /// CommNet Lines" toggle reads the same getter, so while this is <c>true</c> that toggle
        /// renders as OFF and flipping it has no visible effect on the map; the value it writes is
        /// still stored and takes effect the moment this override is off. Setting this entry to
        /// <c>false</c> restores the game's stock behaviour exactly, toggle and all.
        /// </para>
        /// </remarks>
        public const bool HideGameCommLinesDefault = true;

        /// <summary>
        /// The value that means "the player has not moved the toolbar yet".
        /// </summary>
        /// <remarks>
        /// <para>
        /// A negative position is not a position: <c>left</c>/<c>top</c> are measured from the
        /// panel's top-left corner, and the game's own drag clamps them into the panel
        /// (<c>MoveOptions.CheckScreenBounds = true</c>, IL-proven - see
        /// <see cref="MapToolbarX"/>), so a real saved value is never negative. That makes
        /// <c>-1</c> a free sentinel, and it keeps the pair readable in the generated file, where a
        /// bare <c>0</c> would be indistinguishable from "the toolbar sits in the corner".
        /// </para>
        /// <para>
        /// It is also why neither entry carries a <see cref="RangeConstraint{T}"/>: that class's
        /// <c>IsValid</c> is <c>Minimum &lt;= o &amp;&amp; o &lt;= Maximum</c> (pinned source,
        /// <c>Runtime/ReduxLib/Configuration/RangeConstraint.cs</c> at commit <c>54bdefc</c>), so a
        /// <c>0</c>-based slider range would reject this sentinel and put a freshly generated
        /// config file's own default outside its own constraint. D53 records the route that
        /// shipped.
        /// </para>
        /// </remarks>
        public const double MapToolbarPositionUnset = -1.0;

        /// <summary>Default of <see cref="MapToolbarX"/>: no position has been saved yet.</summary>
        public const double MapToolbarXDefault = MapToolbarPositionUnset;

        /// <summary>Default of <see cref="MapToolbarY"/>: no position has been saved yet.</summary>
        public const double MapToolbarYDefault = MapToolbarPositionUnset;

        /// <summary>
        /// Default of every one of the seven colour entries (U6g): <b>the empty string, meaning
        /// "use the colour the code already draws"</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Empty is the only default that can be proven not to change today's rendering: there is no
        /// string-to-colour round trip at all on the default path, so the colour the renderer draws
        /// is the very <c>Color</c> the band table (or the relay/link fallback) already carries -
        /// bit-for-bit, not "equal to within a rounding error". A default of
        /// <c>"#2CC8C6"</c> would have re-derived the X band's colour through
        /// <c>#RRGGBB</c>, which quantises to 8 bits per channel and would have silently changed
        /// every colour the mod has drawn since Phase 6.
        /// </para>
        /// <para>
        /// It is also the friendliest possible default to read: the settings field is empty until
        /// the player types something, and an empty field visibly means "not overridden". Each
        /// key's description names the built-in colour it falls back to, so the value is one glance
        /// away - and the boot line lists all seven.
        /// </para>
        /// </remarks>
        public const string LineColorDefault = "";

        /// <summary>
        /// Default of <see cref="LineOpacity"/>: <c>1.0</c> - fully solid, i.e. exactly the alpha
        /// every built-in colour already carries.
        /// </summary>
        /// <remarks>
        /// The label and the number must agree, and they do: <c>1.00</c> is opaque, <c>0</c> is
        /// invisible. That is the opposite reading from <c>"Occlusion radius"</c>'s (<c>0</c> = no
        /// occlusion, <c>1</c> = the full body radius), which is exactly the trap the user fell into
        /// in L23 and the reason this entry's description spells the direction out instead of
        /// naming the field only "Transparency".
        /// </remarks>
        public const double LineOpacityDefault = 1.0;

        /// <summary>Smallest accepted <see cref="LineOpacity"/>: invisible.</summary>
        public const double LineOpacityMinimum = 0.0;

        /// <summary>Largest accepted <see cref="LineOpacity"/>: fully solid.</summary>
        public const double LineOpacityMaximum = 1.0;

        // -------------------------------------------------------------------------------------
        // The two legacy range constants the KSC override selects between.
        // -------------------------------------------------------------------------------------

        /// <summary>2 Gm in metres - <see cref="KscRangeMode.G2"/>.</summary>
        public const double KscRangeG2 = 2_000_000_000.0;

        /// <summary>10 Gm in metres - <see cref="KscRangeMode.G10"/>.</summary>
        public const double KscRangeG10 = 10_000_000_000.0;

        /// <summary>50 Gm in metres - <see cref="KscRangeMode.G50"/>.</summary>
        public const double KscRangeG50 = 50_000_000_000.0;

        /// <summary>The 0..1 slider for <see cref="OcclusionRadius"/>.</summary>
        public static readonly RangeConstraint<double> OcclusionRadiusConstraint =
            new RangeConstraint<double>(
                OcclusionRadiusMinimum, OcclusionRadiusMaximum, 100, "{0:F2}");

        /// <summary>
        /// The 0..1 slider for <see cref="LineOpacity"/> (U6g), built exactly like
        /// <see cref="OcclusionRadiusConstraint"/>.
        /// </summary>
        /// <remarks>
        /// A <see cref="RangeConstraint{T}"/> and not a <see cref="ListConstraint{T}"/>: the settings
        /// builder tests for a dropdown FIRST, so a list constraint here would produce a dropdown
        /// whose only entries were whatever strings happened to be listed, and the numeric slider
        /// would never be built. The pinned source's <c>IsValid</c> is
        /// <c>Minimum &lt;= o &amp;&amp; o &lt;= Maximum</c>, so both <c>0</c> and <c>1</c> are
        /// accepted and the default sits inside its own constraint.
        /// </remarks>
        public static readonly RangeConstraint<double> LineOpacityConstraint =
            new RangeConstraint<double>(
                LineOpacityMinimum, LineOpacityMaximum, 100, "{0:F2}");

        /// <summary>
        /// The master off-switch. <c>false</c> makes this mod hand every part of the CommNet graph
        /// back to the game unmodified.
        /// </summary>
        /// <remarks>
        /// <para>
        /// User decision D1. When <c>false</c> the three behaviours that make this a port rather than
        /// an observer all stand down:
        /// </para>
        /// <list type="bullet">
        /// <item><description>the <c>RebuildConnectionGraph</c> prefix returns <c>true</c>, so the
        /// game's own method runs its own job and the graph is vanilla;</description></item>
        /// <item><description>the <c>SetSourceNode</c> postfix returns without touching
        /// <c>newSourceNode.MaxRange</c> and without moving the CommNet origin's
        /// position;</description></item>
        /// <item><description>no relay-power work runs (Phase 5's, absent in this phase
        /// anyway).</description></item>
        /// </list>
        /// <para>
        /// The diagnostic probe deliberately keeps running in both states - that is what makes the
        /// hand-back <i>provable</i> from one launch: the same probe reports the vanilla tree when the
        /// switch is off and this port's tree when it is on, under the same save.
        /// </para>
        /// </remarks>
        public static ConfigValue<bool> Enable { get; private set; }

        /// <summary>Which cost metric selects the best path.</summary>
        public static ConfigValue<BestPathMode> BestPath { get; private set; }

        /// <summary>The KSC's maximum range.</summary>
        public static ConfigValue<KscRangeMode> KscRange { get; private set; }

        /// <summary>
        /// The occlusion radius multiplier applied to every celestial body's radius.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A body occludes when the segment between two nodes passes within
        /// <c>body.radius * this - 1000 m</c> of its centre. <c>0</c> means no occlusion.
        /// </para>
        /// <para>
        /// <b><c>0</c> is a genuine off-switch here, and it was not in the legacy.</b> The legacy's
        /// effective radius was <c>radius * factor - 1000</c> <i>unconditionally</i>, so at factor
        /// <c>0</c> every body became a phantom sphere of radius -1000, whose square is
        /// <c>+1e6</c> - i.e. a body still occluded anything passing within 1 km of its centre,
        /// contradicting the setting's own description. This port skips a body whose effective radius
        /// is not positive. The divergence is a fix, it is recorded in
        /// <c>Deploy/obj/divergences.md</c>, and it is what makes the factor-<c>0</c> experiment the
        /// probe's oracle block offers meaningful.
        /// </para>
        /// </remarks>
        public static ConfigValue<double> OcclusionRadius { get; private set; }

        /// <summary>Whether the D9 diagnostic probe logs one block per graph rebuild.</summary>
        public static ConfigValue<bool> Probe { get; private set; }

        /// <summary>
        /// Whether an enabled relay has to be able to pay for its own operation to stay in the
        /// network.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What it gates.</b> The relay draws its EC through its own resource request
        /// (<c>Data_NextRelay.SetupResourceRequest</c>, driven per tick by
        /// <c>PartComponentModule_NextRelay</c>), and this entry decides what happens when that
        /// request cannot be paid. With it <c>true</c> a failed tick leaves the relay's own
        /// <c>HasResourcesToOperate == false</c>; <c>NetworkEngine.CollectNodeStates</c> reads that
        /// field into the node's <c>HasEnoughResources</c>; and the band gate then refuses every edge
        /// that node would have formed. A starved relay goes dark instead of relaying for free. With
        /// it <c>false</c> the relay stands its request down and always reports resources, so it costs
        /// nothing and can never be gated - the no-cost mode.
        /// </para>
        /// <para>
        /// <b>It is a live setting.</b> Both the request's activation and this read happen per tick, so
        /// a toggle takes effect on the next tick and the next graph rebuild. The legacy could only
        /// honour it on a reload, because it gated its
        /// <c>RegisterModuleForBackgroundResourceProcessing</c> call on this same key and a module
        /// registration cannot be unwound. This port registers that module unconditionally at boot
        /// (<c>CommNextReduxPlugin.EnsureBackgroundResourceProcessing</c>) - which is what keeps the
        /// tick coming for relays on vessels that are not the controlled one at all, so the per-tick
        /// read below is reached in both states, and with the key off a stale <c>false</c> from an
        /// earlier state is cleared rather than stranded.
        /// </para>
        /// <para>
        /// The campaign's <c>InfinitePower</c> difficulty option is honoured too: both this port's gate
        /// and the relay's own loop ask before they enforce anything, so a sandbox save with infinite
        /// electricity cannot be starved by a setting the player cannot see the effect of.
        /// </para>
        /// </remarks>
        public static ConfigValue<bool> RelaysRequirePower { get; private set; }

        /// <summary>Whether the network pass logs how long it took, every few seconds.</summary>
        public static ConfigValue<bool> EnableProfileLogs { get; private set; }

        /// <summary>
        /// Which range rulers the map draws.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The one entry in this file that drives renderer state rather than being read where it is
        /// used.</b> Every other setting is consulted at its point of use, so a change lands on the next
        /// pass with no cache to clear; this one cannot work that way, because
        /// <see cref="ConnectionsRenderer.RulersDisplayMode"/> is the live state AND the owner of two
        /// behaviours a config read cannot provide: destroying every ruler already drawn on the
        /// transition into <c>None</c>, and forcing a pass on the transition out of it. So the plugin
        /// applies this entry to the renderer at boot and again on every change - see
        /// <c>CommNextReduxPlugin.EnsureRenderer</c> and <c>EnsureConfiguration</c>.
        /// </para>
        /// <para>
        /// The type is <see cref="RulersDisplayMode"/>, from the rendering layer: a deliberate
        /// type-only dependency, because the alternative - a second enum in this namespace mirroring the
        /// first - would be two sources of truth for a three-member set. It round-trips by NAME
        /// (<c>None</c>, <c>Relays</c>, <c>All</c>) through ReduxLib's <c>StringEnumConverter</c>, which
        /// is why the enum's member names are user-facing text and must not be renamed.
        /// </para>
        /// </remarks>
        public static ConfigValue<RulersDisplayMode> Rulers { get; private set; }

        /// <summary>
        /// Which connection lines the map draws.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The second entry in this file that drives renderer state rather than being read where it
        /// is used</b> - the twin of <see cref="Rulers"/> and for the same reason.
        /// <see cref="ConnectionsRenderer.ConnectionsDisplayMode"/> is the live state and the owner of
        /// a behaviour a config read cannot provide: destroying every line already drawn on the
        /// transition into <c>None</c>. So the plugin applies this entry to the renderer at boot and
        /// again on every change, exactly as it does for the ruler mode.
        /// </para>
        /// <para>
        /// <b>Phase 8a's answer to a P7 carried note.</b> P7's hand-off says a UI control must write
        /// the config entry and never the renderer's property, or <c>Settings -&gt; Mods</c> and the map
        /// disagree and the change is not saved. Until this entry existed there was nothing for the
        /// toolbar's lines button to write, so this is the entry that makes that instruction possible
        /// (D39). The type is <see cref="ConnectionsDisplayMode"/>, a deliberate type-only dependency
        /// on the rendering layer for the reason <see cref="Rulers"/> gives.
        /// </para>
        /// </remarks>
        public static ConfigValue<ConnectionsDisplayMode> Connections { get; private set; }

        /// <summary>
        /// Whether the game's <b>own</b> CommNet lines are hidden while this mod runs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The third entry in this file whose effect is applied rather than read at its point of use,
        /// but not like <see cref="Rulers"/> or <see cref="Connections"/>: those drive renderer state,
        /// while this one drives a cached flag on the Harmony patch that intercepts the game's own
        /// getter (<c>CommNextRedux.Patches.StockCommNetLinesPatch</c>). The plugin arms that flag
        /// from this entry in <c>EnsureConfiguration</c> - before Harmony is installed - and refreshes
        /// it from the change callback, and the patch logs the decision once per change with the
        /// <c>stock-comm-lines:</c> prefix.
        /// </para>
        /// <para>
        /// <b>Why a cache at all.</b> The patched getter runs on the map's draw path, once per frame
        /// per drawer pass, so it reads a static bool rather than the config object; the value is
        /// recomputed only when the player changes it. See <see cref="HideGameCommLinesDefault"/> for
        /// what the override does, what it never writes and the honest side effect on the game's own
        /// settings toggle.
        /// </para>
        /// </remarks>
        public static ConfigValue<bool> HideGameCommLines { get; private set; }

        /// <summary>
        /// The horizontal half of the map toolbar's saved position, in panel pixels from the
        /// panel's top-left corner. <see cref="MapToolbarPositionUnset"/> until the player moves
        /// the window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Which field carries a move - measured, not assumed (F69).</b> The movement is the
        /// game's own manipulator (<c>MoveOptions.IsMovingEnabled</c>), so this port cannot assume
        /// where the new position lands. <c>DragManipulator.OnPointerMove</c> ends with exactly
        /// four writes, read out of <c>UitkForKsp2.dll</c>:
        /// <c>style.position = Absolute</c> · <c>style.left = WorldToLocal(parent, point).x</c> ·
        /// <c>style.top = ... .y</c> · <c>transform.position = Vector3.zero</c>. The INLINE
        /// <c>style.left</c>/<c>style.top</c> therefore carry the move, and the CSS translate is
        /// zeroed - which is why the read path uses those and never <c>transform.position</c>
        /// (AGENTS.md 9). <c>UIStyleSheetProof</c> logs all three plus <c>worldBound</c> at entry
        /// and exit, so a launch proves the field rather than inheriting this paragraph.
        /// </para>
        /// <para>
        /// <b>The clamp is the game's own, copied rather than invented.</b> With
        /// <c>MoveOptions.CheckScreenBounds = true</c> the library registers the manipulator as
        /// <c>MakeDraggable(handle, root, checkScreenBounds: true)</c>, which constructs
        /// <c>DragManipulator(allowDraggingOffScreen: !checkScreenBounds)</c> - i.e. the drag is
        /// already bounded. Its bound is <c>Mathf.Clamp(p, rect.xMin, rect.xMin + Mathf.Max(0,
        /// rect.width - size.x))</c> per axis, with <c>rect = panel.visualTree.contentRect</c> and
        /// <c>size = element.worldBound.size</c>. This port applies the same formula when it
        /// restores a saved position, so a value saved at one resolution cannot strand the window
        /// at another, and it reads the same rect the drag reads.
        /// </para>
        /// <para>
        /// <b>Who writes it.</b> Only <c>MapToolbarWindowController</c>, only on leaving the map
        /// view, and only when the position actually changed - never during a drag, which the port
        /// does not own and which would write the file on every pointer move. The <c>Settings -&gt;
        /// Mods</c> page also shows both keys, because they are ordinary config entries; a value
        /// typed there is clamped like any other on the next apply.
        /// </para>
        /// </remarks>
        public static ConfigValue<double> MapToolbarX { get; private set; }

        /// <summary>
        /// The vertical half of the map toolbar's saved position - the twin of
        /// <see cref="MapToolbarX"/>, which owns the meaning, the field evidence and the clamp.
        /// </summary>
        public static ConfigValue<double> MapToolbarY { get; private set; }

        // -------------------------------------------------------------------------------------
        // U6g's line appearance. Seven colour overrides and one opacity multiplier, all in
        // LinesSection. They are read as a SET by `LineAppearance` (the rendering layer's cache),
        // not one at a time, which is why there is no per-key accessor below - the reader is
        // `LineAppearance.Refresh` and it applies the same "unbound or empty means the built-in
        // colour" rule to all seven.
        //
        // WHY THE COLOURS ARE `string` ENTRIES: the settings builder's own type dispatch decides
        // what a Settings -> Mods row looks like. On the shipped `UitkForKsp2.dll` the builder's
        // cctor registers `SettingsSubMenuBuilder::AddTextInput<string>` (IL 676355) beside
        // `AddToggle<bool>` and the numeric `AddTextInput` overloads, and its `BuildFor`
        // (IL ~676950-677162) falls through dropdown -> numeric -> enum and, for a type it has no
        // control for, logs "Could not create setting for {0} due to it's type {1}" and builds
        // NOTHING. A string key therefore gets an editable text row; a `Color` key would get the
        // "Could not create setting" line and no row at all, and a `ListConstraint<string>` on
        // these keys would turn them into dropdowns (the dropdown test runs first), so none is
        // attached.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The X band's line colour as the player typed it, or <see cref="LineColorDefault"/> for
        /// the built-in colour (U6g).
        /// </summary>
        /// <remarks>
        /// Accepts whatever the game's own colour notation accepts - <c>#RRGGBB</c>,
        /// <c>#RRGGBBAA</c> and Unity's named colours - because the parser is the game's own
        /// <c>UnityEngine.ColorUtility.TryParseHtmlString</c>. That is deliberately the same call
        /// the band icons already go through: <c>BandIcon</c>'s <c>color</c> attribute is a
        /// <c>UxmlColorAttributeDescription</c>, whose <c>GetValueFromBag</c> routes the markup
        /// string through that same parser, so a line and its icon cannot disagree about what
        /// <c>#B325D4FF</c> means. An unparseable value is a warning (once per key) and the
        /// built-in colour, never an exception and never a blank line.
        /// </remarks>
        public static ConfigValue<string> XBandColor { get; private set; }

        /// <summary>The S band's line colour override - the twin of <see cref="XBandColor"/>.</summary>
        public static ConfigValue<string> SBandColor { get; private set; }

        /// <summary>The K band's line colour override - the twin of <see cref="XBandColor"/>.</summary>
        public static ConfigValue<string> KBandColor { get; private set; }

        /// <summary>The Ka band's line colour override - the twin of <see cref="XBandColor"/>.</summary>
        public static ConfigValue<string> KaBandColor { get; private set; }

        /// <summary>The V band's line colour override - the twin of <see cref="XBandColor"/>.</summary>
        public static ConfigValue<string> VBandColor { get; private set; }

        /// <summary>
        /// The colour of a relay-to-relay hop that the band gate put no band on - the renderer's
        /// <c>MapConnectionComponent.RelayColor</c>, overridable.
        /// </summary>
        /// <remarks>
        /// The second of the renderer's two fallbacks, and a separate key from
        /// <see cref="OtherLinksColor"/> because they are separate colours in the renderer: this one
        /// is drawn when BOTH endpoints are relays and no band was selected.
        /// </remarks>
        public static ConfigValue<string> RelayHopColor { get; private set; }

        /// <summary>
        /// The colour of every other link the band gate put no band on - the renderer's
        /// <c>NetworkBands.NoBandColor</c>, overridable.
        /// </summary>
        /// <remarks>
        /// The renderer's last fallback, and the one a player is most likely to see: it is what a
        /// link is drawn in when neither endpoint resolved to a band, and it is the legacy's own
        /// plain-link green.
        /// </remarks>
        public static ConfigValue<string> OtherLinksColor { get; private set; }

        /// <summary>
        /// How solid the map's connection lines are, as a multiplier on the colour's own alpha
        /// (U6g). <c>1.00</c> is fully solid (the default), <c>0</c> is invisible.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It multiplies whatever alpha the line's colour carries, so a <c>#RRGGBBAA</c> override
        /// whose alpha is already <c>0.5</c> becomes <c>0.5 * this</c>. The renderer's built-in
        /// colours all carry <c>a = 1</c> (the band table's alpha was normalised in Phase 6), so at
        /// the default the multiplier is an exact identity and the returned <c>Color</c> is the one
        /// the code passed in.
        /// </para>
        /// <para>
        /// A <see cref="RangeConstraint{T}"/> like <see cref="OcclusionRadius"/>'s, so the settings
        /// row is a 0..1 slider. See <see cref="LineOpacityDefault"/> for why the label and the
        /// number read the same way.
        /// </para>
        /// </remarks>
        public static ConfigValue<double> LineOpacity { get; private set; }

        /// <summary>Set once <see cref="Initialize"/> has bound every entry.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>Whether the switch is on, falling back to <see cref="EnableDefault"/> when unbound.</summary>
        public static bool NetworkEnabled
        {
            get
            {
                ConfigValue<bool> value = Enable;
                return value == null ? EnableDefault : value.Value;
            }
        }

        /// <summary>The selected best-path mode, falling back to <see cref="BestPathDefault"/>.</summary>
        public static BestPathMode Mode
        {
            get
            {
                ConfigValue<BestPathMode> value = BestPath;
                return value == null ? BestPathDefault : value.Value;
            }
        }

        /// <summary>The selected KSC range, falling back to <see cref="KscRangeDefault"/>.</summary>
        public static KscRangeMode Ksc
        {
            get
            {
                ConfigValue<KscRangeMode> value = KscRange;
                return value == null ? KscRangeDefault : value.Value;
            }
        }

        /// <summary>The selected occlusion factor, falling back to <see cref="OcclusionRadiusDefault"/>.</summary>
        public static double OcclusionFactor
        {
            get
            {
                ConfigValue<double> value = OcclusionRadius;
                return value == null ? OcclusionRadiusDefault : value.Value;
            }
        }

        /// <summary>Whether the probe is on, falling back to <see cref="ProbeDefault"/> when unbound.</summary>
        public static bool ProbeEnabled
        {
            get
            {
                ConfigValue<bool> value = Probe;
                return value == null ? ProbeDefault : value.Value;
            }
        }

        /// <summary>
        /// Whether relays must be able to operate, falling back to
        /// <see cref="RelaysRequirePowerDefault"/>.
        /// </summary>
        /// <remarks>
        /// Read once per pass by <c>NetworkEngine.CollectNodeStates</c>, on the game's own
        /// graph-rebuild path, so an unbound config must not throw there. There is no per-tick read
        /// and no local snapshot to keep in step: the value is consulted where it is used, which is
        /// also why the change callback has nothing to invalidate.
        /// </remarks>
        public static bool RelaysRequirePowerEnabled
        {
            get
            {
                ConfigValue<bool> value = RelaysRequirePower;
                return value == null ? RelaysRequirePowerDefault : value.Value;
            }
        }

        /// <summary>Whether the pass-duration log is on, falling back to <see cref="ProfileLogsDefault"/>.</summary>
        public static bool ProfileLogsEnabled
        {
            get
            {
                ConfigValue<bool> value = EnableProfileLogs;
                return value == null ? ProfileLogsDefault : value.Value;
            }
        }

        /// <summary>
        /// The rulers' mode the config asks for, falling back to <see cref="RulersModeDefault"/>.
        /// </summary>
        /// <remarks>
        /// Read once at boot by the plugin and applied to
        /// <see cref="ConnectionsRenderer.RulersDisplayMode"/>; after boot the renderer's own property
        /// is the live value and the change callback keeps it in step. An unbound config therefore
        /// behaves exactly as a fresh one: the legacy's <c>Relays</c>.
        /// </remarks>
        public static RulersDisplayMode RulersMode
        {
            get
            {
                ConfigValue<RulersDisplayMode> value = Rulers;
                return value == null ? RulersModeDefault : value.Value;
            }
        }

        /// <summary>
        /// The connection mode the config asks for, falling back to
        /// <see cref="ConnectionsModeDefault"/>.
        /// </summary>
        /// <remarks>
        /// Read once at boot by the plugin and applied to
        /// <see cref="ConnectionsRenderer.ConnectionsDisplayMode"/>; after boot the renderer's own
        /// property is the live value and the change callback keeps it in step. An unbound config
        /// therefore behaves exactly as a fresh one: <c>Lines</c>, which is what every pre-P8a launch
        /// measured.
        /// </remarks>
        public static ConnectionsDisplayMode ConnectionsMode
        {
            get
            {
                ConfigValue<ConnectionsDisplayMode> value = Connections;
                return value == null ? ConnectionsModeDefault : value.Value;
            }
        }

        /// <summary>
        /// Whether the game's own CommNet lines are hidden, falling back to
        /// <see cref="HideGameCommLinesDefault"/> when unbound.
        /// </summary>
        /// <remarks>
        /// Read once at boot by <c>CommNextReduxPlugin.EnsureStockCommLinesOverride</c> and again on
        /// every change; the patch caches the result and never calls this on the draw path. An
        /// unbound config therefore behaves exactly as a fresh one: the game's own lines are hidden,
        /// which is the shipped default.
        /// </remarks>
        public static bool HideGameCommLinesEnabled
        {
            get
            {
                ConfigValue<bool> value = HideGameCommLines;
                return value == null ? HideGameCommLinesDefault : value.Value;
            }
        }

        /// <summary>
        /// The saved toolbar X, falling back to <see cref="MapToolbarXDefault"/> when unbound.
        /// </summary>
        /// <remarks>
        /// A nil-safe read for the same reason every other accessor here has one: the toolbar can
        /// be bound (and can show) on a launch where the config bind has not completed.
        /// </remarks>
        public static double MapToolbarXValue
        {
            get
            {
                ConfigValue<double> value = MapToolbarX;
                return value == null ? MapToolbarXDefault : value.Value;
            }
        }

        /// <summary>The saved toolbar Y, falling back to <see cref="MapToolbarYDefault"/>.</summary>
        public static double MapToolbarYValue
        {
            get
            {
                ConfigValue<double> value = MapToolbarY;
                return value == null ? MapToolbarYDefault : value.Value;
            }
        }

        /// <summary>
        /// Whether a usable toolbar position has been saved, and what it is.
        /// </summary>
        /// <param name="x">The saved horizontal position, or the sentinel when there is none.</param>
        /// <param name="y">The saved vertical position, or the sentinel when there is none.</param>
        /// <returns><c>true</c> only when both halves are real positions.</returns>
        /// <remarks>
        /// Both halves or neither: a half-written pair - X saved and Y still at the sentinel, which
        /// is what a hand-edited file or a crash between the two writes would leave - is read as
        /// "not saved", because the alternative is a window placed at the right column and the
        /// wrong row. The test is <c>&gt;= 0</c>, which is the sentinel's contract; see
        /// <see cref="MapToolbarPositionUnset"/> for why a real value is never negative.
        /// </remarks>
        public static bool TryGetSavedMapToolbarPosition(out double x, out double y)
        {
            x = MapToolbarXValue;
            y = MapToolbarYValue;
            return x >= 0.0 && y >= 0.0;
        }

        /// <summary>
        /// Writes both halves of the toolbar position, or neither.
        /// </summary>
        /// <param name="x">The horizontal position in panel pixels.</param>
        /// <param name="y">The vertical position in panel pixels.</param>
        /// <returns><c>true</c> when both entries were written.</returns>
        /// <remarks>
        /// The single writer, so a half-written pair cannot come from this port. Two separate
        /// <c>entry.Value =</c> writes are two config-file writes and two change lines, which is
        /// acceptable precisely because the caller has already established that the player moved
        /// the window - the one event this is allowed to be noisy about.
        /// </remarks>
        public static bool SaveMapToolbarPosition(double x, double y)
        {
            ConfigValue<double> entryX = MapToolbarX;
            ConfigValue<double> entryY = MapToolbarY;
            if (entryX == null || entryY == null)
            {
                return false;
            }

            entryX.Value = x;
            entryY.Value = y;
            return true;
        }

        /// <summary>
        /// The line-opacity multiplier the config asks for, falling back to
        /// <see cref="LineOpacityDefault"/> when unbound (U6g).
        /// </summary>
        /// <remarks>
        /// Read by <c>LineAppearance.Refresh</c> at boot and again on every change - never on the
        /// per-line path, which reads the parsed float cache. Clamped to
        /// <see cref="LineOpacityMinimum"/>..<see cref="LineOpacityMaximum"/> on the way in, because
        /// a hand-edited file can carry a value outside the slider's range even though the
        /// constraint rejects it in the UI; a negative multiplier would make the colour's alpha
        /// negative, which the renderer would draw as a fully transparent line, i.e. "the lines
        /// disappeared" with nothing in the log to say why.
        /// </remarks>
        public static double LineOpacityFactor
        {
            get
            {
                ConfigValue<double> value = LineOpacity;
                double factor = value == null ? LineOpacityDefault : value.Value;
                if (double.IsNaN(factor) || factor < LineOpacityMinimum)
                {
                    return LineOpacityMinimum;
                }

                return factor > LineOpacityMaximum ? LineOpacityMaximum : factor;
            }
        }

        /// <summary>
        /// A built-in colour as <c>#RRGGBB</c>, for the line-appearance descriptions and the boot
        /// line (U6g).
        /// </summary>
        /// <param name="color">The built-in colour, read from the table the renderer draws from.</param>
        /// <returns>The colour in the same notation the player types into the colour keys.</returns>
        /// <remarks>
        /// Computed from the live field, never typed by hand: a description that read
        /// <c>#2CC8C6</c> while the renderer drew something else would be a second source of truth
        /// for one colour. <c>UnityEngine.ColorUtility.ToHtmlStringRGB(Color)</c> is the pinned
        /// round-trip partner of the parser the keys go through; read out of the shipped
        /// <c>UnityEngine.CoreModule.dll</c>, method row 7780 forwards to the <c>in Color</c>
        /// overload (row 7781) whose whole body is managed - <c>Mathf.RoundToInt(channel * 255)</c>
        /// clamped to 0..255, packed into a <c>Color32</c> and printed with
        /// <c>"{0:X2}{1:X2}{2:X2}"</c> - so there is no engine call to trip over on the boot path
        /// and the text is exactly what <c>TryParseHtmlString</c> reads back.
        /// </remarks>
        public static string BuiltInColorHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        /// <summary>
        /// The description text all seven colour keys share (U6g).
        /// </summary>
        /// <param name="builtIn">The colour the renderer draws while the key is empty.</param>
        /// <param name="what">What the key colours, e.g. <c>"the X band's lines"</c>.</param>
        /// <param name="extra">An optional extra sentence for one key, or <c>null</c> for none.</param>
        /// <returns>The description, with the built-in colour's own hex in it.</returns>
        /// <remarks>
        /// One builder rather than seven hand-written paragraphs, so the empty-means-built-in rule,
        /// the accepted notations, the icon-agreement note and the live-change sentence cannot drift
        /// apart key by key. The built-in hex is read from <paramref name="builtIn"/> rather than
        /// written into the text, so a change to the band table changes the description too.
        /// </remarks>
        private static string LineColorDescription(Color builtIn, string what, string extra)
        {
            return "The colour used for " + what + ", as a hex value.\n\n"
                + "Leave empty for the built-in colour (" + BuiltInColorHex(builtIn) + ").\n"
                + "Accepts #RRGGBB, #RRGGBBAA and the colour names Unity accepts (red, ...). An "
                + "empty field is not a colour: it means \"not overridden\".\n\n"
                + "This is the same notation, through the same conversion, as the band icons on the "
                + "report's rows - so a line and its icon agree.\n\n"
                + (extra == null ? string.Empty : extra + ".\n\n")
                + "Live: the next line pass (within half a second, in the map view) repaints every "
                + "line already drawn. A value that cannot be read as a colour is reported in the "
                + "log and the built-in colour is used instead.";
        }

        /// <summary>The metre value for a <see cref="KscRangeMode"/>.</summary>
        /// <param name="mode">The mode to convert.</param>
        /// <returns>The range in metres; <see cref="KscRangeG2"/> for an unhandled member.</returns>
        public static double RangeFor(KscRangeMode mode)
        {
            switch (mode)
            {
                case KscRangeMode.G2: return KscRangeG2;
                case KscRangeMode.G10: return KscRangeG10;
                case KscRangeMode.G50: return KscRangeG50;
                default: return KscRangeG2;
            }
        }

        /// <summary>
        /// Binds every entry to the plugin's loader-assigned config file.
        /// </summary>
        /// <param name="plugin">The live plugin, whose <c>SWConfiguration</c> the entries bind through.</param>
        /// <returns>A one-line summary of the resolved values, for the caller to log.</returns>
        /// <remarks>
        /// Called once from <see cref="CommNextReduxPlugin.OnPreInitialized"/>. Unlike the sibling
        /// port this does not set <see cref="IsInitialized"/> before returning on a repeated call -
        /// it returns early and keeps the first binding, so a second call cannot silently replace a
        /// live <see cref="ConfigValue{T}"/> that a patch already captured.
        /// </remarks>
        public static string Initialize(CommNextReduxPlugin plugin)
        {
            if (IsInitialized)
            {
                return "configuration already bound";
            }

            IConfigFile file = plugin.SWConfiguration;

            Enable = new ConfigValue<bool>(file.Bind(
                NetworkSection,
                EnableKey,
                EnableDefault,
                "The master switch for the CommNext network.\n\n"
                + "true  - CommNext computes the connection graph: signal occlusion by celestial "
                + "bodies, its own best-path metric and the KSC range override below.\n"
                + "false - the game's own connection graph is used unmodified, exactly as if this "
                + "mod were not installed.\n\n"
                + "Takes effect on the next graph rebuild (the game rebuilds roughly every 3 "
                + "seconds)."));

            BestPath = new ConfigValue<BestPathMode>(file.Bind(
                NetworkSection,
                BestPathKey,
                BestPathDefault,
                "How to compute the best path for the network.\n\n"
                + "ShortestKSC  - the best path is the one with the lowest accumulated cost to the "
                + "KSC. This is the metric the game itself uses.\n"
                + "NearestRelay - the best path is the one with the minimum distance between "
                + "adjacent nodes. Only has an effect once relays can forward traffic (Phase 5).",
                new BestPathModeConstraint()));

            KscRange = new ConfigValue<KscRangeMode>(file.Bind(
                NetworkSection,
                KscRangeKey,
                KscRangeDefault,
                "The range of the KSC in the network.\n\n"
                + "The KSC's CommNet origin is also moved onto the space centre itself, so that its "
                + "range is measured from the launch site rather than from the centre of Kerbin.",
                new KscRangeModeConstraint()));

            OcclusionRadius = new ConfigValue<double>(file.Bind(
                NetworkSection,
                OcclusionRadiusKey,
                OcclusionRadiusDefault,
                "The occlusion body radius multiplier.\n\n"
                + "A value of 0 means no occlusion; a value of 1 means the full planet radius is "
                + "considered for occlusion. The default 0.98 means 98% of a planet's radius is "
                + "considered, which keeps terrain and atmosphere from occluding a link that is "
                + "visually clear.",
                OcclusionRadiusConstraint));

            RelaysRequirePower = new ConfigValue<bool>(file.Bind(
                NetworkSection,
                RelaysRequirePowerKey,
                RelaysRequirePowerDefault,
                "If true, an enabled relay must be able to pay for its own operation to stay in the "
                + "network; a relay that runs out of ElectricCharge stops relaying.\n\n"
                + "The relay draws its own EC - the rate this mod patches into the part - through "
                + "the game's resource system. This setting decides what happens when that draw "
                + "cannot be met: true - the relay goes dark, false - it is not charged at all and "
                + "keeps relaying.\n\n"
                + "The rate itself is fixed by the part, not by this setting; and a save with the "
                + "InfinitePower difficulty option on is never gated. Live: takes effect on the next "
                + "tick and the next connection-graph rebuild (roughly every 3 seconds)."));

            Probe = new ConfigValue<bool>(file.Bind(
                DebugSection,
                ProbeKey,
                ProbeDefault,
                "Log one diagnostic block per connection-graph rebuild: the node count, every "
                + "occlusion verdict with the name of the blocking body, the selected path, and a "
                + "cross-check of this mod's tabulation against the game's own "
                + "GetConnectedNodesJob.\n\n"
                + "Off by default: this is a diagnostic, and turning it on makes the log large. An "
                + "existing config that carries true keeps it on."));

            EnableProfileLogs = new ConfigValue<bool>(file.Bind(
                DebugSection,
                ProfileLogsKey,
                ProfileLogsDefault,
                "Log how long each connection-graph pass took, at most once every few seconds.\n\n"
                + "This is a timing log, not a diagnostic dump - for the structural report use "
                + "\"Network probe\" above. It also controls whether network nodes carry vessel "
                + "names, which costs a lookup per node."));

            Rulers = new ConfigValue<RulersDisplayMode>(file.Bind(
                MapSection,
                RulersModeKey,
                RulersModeDefault,
                "Which range rulers the map draws: a translucent sphere on a node, scaled to that "
                + "node's own maximum range.\n\n"
                + "None   - no rulers at all; setting this destroys any that are drawn.\n"
                + "Relays - a sphere on every node that carries an enabled relay. This is the "
                + "default, and the legacy mod's own.\n"
                + "All    - a sphere on every node with a range, relays included.\n\n"
                + "Live: changing this takes effect within half a second, in the map view or on "
                + "entering it, and the spheres are created and pruned as the network changes.",
                new RulersDisplayModeConstraint()));

            Connections = new ConfigValue<ConnectionsDisplayMode>(file.Bind(
                MapSection,
                ConnectionsModeKey,
                ConnectionsModeDefault,
                "Which connection lines the map draws, between every node the network reaches.\n\n"
                + "None   - nothing is drawn, and setting this destroys any lines already on the map.\n"
                + "Lines  - every edge of the connection tree. This is the default.\n"
                + "Active - only the edges on the active vessel's own path to the control source.\n\n"
                + "Live: changing this takes effect immediately, in the map view or on entering it, "
                + "and the lines are created and pruned as the network changes. The map toolbar's "
                + "left button cycles these three in the order above.",
                new ConnectionsDisplayModeConstraint()));

            HideGameCommLines = new ConfigValue<bool>(file.Bind(
                MapSection,
                HideGameCommLinesKey,
                HideGameCommLinesDefault,
                "Hide the game's OWN CommNet lines while this mod is running.\n\n"
                + "The game draws its own connection lines in flat green and, unlike this mod's, they "
                + "carry no band colour and no occlusion styling. Because both sets are drawn at "
                + "once, the game's green lines sit on top of this mod's band-coloured lines and "
                + "hide them - and a change to \"Connections mode\" then looks like it did nothing.\n\n"
                + "true  - the game's own lines are hidden; only CommNext's lines are drawn (default).\n"
                + "false - the game's own lines are drawn as usual, on top of CommNext's.\n\n"
                + "This never changes the game's own \"Show CommNet Lines\" setting - that saved value "
                + "is left exactly as it is, so uninstalling restores the stock behaviour. While this "
                + "is true, the game's Gameplay settings toggle for those lines reads as OFF (it reads "
                + "the same value this overrides) and flipping it has no visible effect.\n\n"
                + "Live: takes effect immediately, and CommNext's own lines are unaffected either way."));

            MapToolbarX = new ConfigValue<double>(file.Bind(
                MapSection,
                MapToolbarXKey,
                MapToolbarXDefault,
                "The map toolbar's horizontal position, in panel pixels from the left edge of the "
                + "screen.\n\n"
                + "This is written by the mod itself: drag the toolbar in the map view and leave "
                + "the map, and this and \"Map toolbar Y\" are saved; the next time the map opens, "
                + "the toolbar returns to where you left it.\n\n"
                + "-1 means \"never moved\", and the toolbar opens at its default top-right spot "
                + "instead. A saved position is clamped onto the screen when it is applied, so a "
                + "value left over from another resolution cannot put the window out of reach."));

            MapToolbarY = new ConfigValue<double>(file.Bind(
                MapSection,
                MapToolbarYKey,
                MapToolbarYDefault,
                "The map toolbar's vertical position, in panel pixels from the top edge of the "
                + "screen - the other half of \"Map toolbar X\", which owns the explanation.\n\n"
                + "-1 means \"never moved\". Both halves are saved and read together: if either is "
                + "still -1, the toolbar opens at its default position."));

            // U6g's eight entries. The descriptions are built from the live built-in colours (see
            // BuiltInColorHex), so "leave empty for #XXXXXX" is always true of the build that is
            // running rather than of the build that wrote the text.
            XBandColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                XBandColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.All[0].Color, "the X band's lines",
                    "Every antenna whose modulator selects no band resolves to X, which is why this "
                    + "is the key to change first")));

            SBandColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                SBandColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.All[1].Color, "the S band's lines", null)));

            KBandColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                KBandColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.All[2].Color, "the K band's lines", null)));

            KaBandColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                KaBandColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.All[3].Color, "the Ka band's lines", null)));

            VBandColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                VBandColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.All[4].Color, "the V band's lines", null)));

            RelayHopColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                RelayHopColorKey,
                LineColorDefault,
                LineColorDescription(MapConnectionComponent.RelayColor,
                    "a relay-to-relay hop that the band gate put no band on", null)));

            OtherLinksColor = new ConfigValue<string>(file.Bind(
                LinesSection,
                OtherLinksColorKey,
                LineColorDefault,
                LineColorDescription(NetworkBands.NoBandColor,
                    "every other link that the band gate put no band on", null)));

            LineOpacity = new ConfigValue<double>(file.Bind(
                LinesSection,
                LineOpacityKey,
                LineOpacityDefault,
                "How solid the map's connection lines are.\n\n"
                + "1.00 - fully solid (the default: exactly what the lines are today).\n"
                + "0.50 - half transparent.\n"
                + "0.00 - invisible (the lines are still there and still updated; you just cannot "
                + "see them).\n\n"
                + "Numbers and the name read the same way: 1 is solid, 0 is invisible. It multiplies "
                + "whatever transparency the line's colour already carries, so a colour given as "
                + "#RRGGBBAA with alpha A ends up at A x this value.\n\n"
                + "This affects the connection lines only. The range rulers keep their own colours "
                + "and are not affected.\n\n"
                + "Live: the next line pass (within half a second, in the map view) repaints every "
                + "line already drawn - no reload, no need to leave and re-enter the map.",
                LineOpacityConstraint));

            RegisterChangeCallbacks();

            IsInitialized = true;

            return "mode=" + Mode
                + " kscRange=" + Ksc + " (" + RangeFor(Ksc) + " m)"
                + " occlusionRadius=" + OcclusionFactor
                + " enabled=" + NetworkEnabled
                + " relaysRequirePower=" + RelaysRequirePowerEnabled
                + " probe=" + ProbeEnabled
                + " profileLogs=" + ProfileLogsEnabled
                + " rulers=" + RulersMode
                + " connections=" + ConnectionsMode
                + " hideGameCommLines=" + HideGameCommLinesEnabled
                + " mapToolbar=" + DescribeMapToolbarPosition();
        }

        /// <summary>One line for the boot summary: the saved toolbar position, or <c>unset</c>.</summary>
        /// <returns>The position in panel pixels, or <c>unset</c>.</returns>
        /// <remarks>
        /// Worth its characters in the boot line: "the position was not restored" and "there was no
        /// position to restore" are different failures, and this is the one place a launch says
        /// which of the two it is - before any map view has opened.
        /// </remarks>
        private static string DescribeMapToolbarPosition()
        {
            return TryGetSavedMapToolbarPosition(out double x, out double y)
                ? x + "," + y
                : "unset";
        }

        /// <summary>
        /// Logs one line whenever the settings UI writes one of these entries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The callback runs on the game's thread, inside the setter.</b> Pinned source, commit
        /// <c>54bdefc</c>, <c>Runtime/ReduxLib/Configuration/ConfigValue.cs</c>:
        /// <c>set =&gt; _entry.Value = value;</c> with <c>RegisterCallback(Action&lt;T,T&gt;)</c>
        /// wrapping <c>_entry.RegisterCallback</c> and invoking <c>callback((T)from, (T)to)</c>. So
        /// the first argument is the value being replaced and the second is the new one, and the
        /// callback is inside the write - it must not do work, allocate a pass, or throw.
        /// </para>
        /// <para>
        /// <b>Nothing is invalidated, deliberately.</b> Every accessor below is read at its point of
        /// use - <see cref="Mode"/> and <see cref="OcclusionFactor"/> per pass in the engine's
        /// <c>Tabulate</c>, <see cref="NetworkEnabled"/> per rebuild in the prefix,
        /// <see cref="ProbeEnabled"/> in the plugin's <c>Update</c>,
        /// <see cref="ProfileLogsEnabled"/> at the top of <c>Prepare</c> - so a change takes effect on
        /// the next graph rebuild with no cache to clear. Do not add an invalidation hook here: there
        /// is no cached copy for it to fix, and a local snapshot would be the thing that then needs
        /// one.
        /// </para>
        /// <para>
        /// A line is worth its cost because a saved setting is otherwise invisible: the generated file
        /// is the record of what was bound, not of what a user changed <i>this</i> session, and
        /// several of these (the master switch, the probe) change what a launch's log means.
        /// </para>
        /// </remarks>
        private static void RegisterChangeCallbacks()
        {
            OnChanged(NetworkSection, EnableKey, Enable);
            OnChanged(NetworkSection, BestPathKey, BestPath);
            OnChanged(NetworkSection, KscRangeKey, KscRange);
            OnChanged(NetworkSection, OcclusionRadiusKey, OcclusionRadius);
            OnChanged(NetworkSection, RelaysRequirePowerKey, RelaysRequirePower);
            OnChanged(DebugSection, ProbeKey, Probe);
            OnChanged(DebugSection, ProfileLogsKey, EnableProfileLogs);
            OnChanged(MapSection, RulersModeKey, Rulers);
            OnChanged(MapSection, ConnectionsModeKey, Connections);
            OnChanged(MapSection, HideGameCommLinesKey, HideGameCommLines);
            OnChanged(MapSection, MapToolbarXKey, MapToolbarX);
            OnChanged(MapSection, MapToolbarYKey, MapToolbarY);

            // U6g's eight. Two callbacks per entry is the documented, additive shape: this one is the
            // change LOG, and LineAppearance registers its own to re-parse the value (see
            // CommNextReduxPlugin.EnsureLineAppearanceWiring).
            OnChanged(LinesSection, XBandColorKey, XBandColor);
            OnChanged(LinesSection, SBandColorKey, SBandColor);
            OnChanged(LinesSection, KBandColorKey, KBandColor);
            OnChanged(LinesSection, KaBandColorKey, KaBandColor);
            OnChanged(LinesSection, VBandColorKey, VBandColor);
            OnChanged(LinesSection, RelayHopColorKey, RelayHopColor);
            OnChanged(LinesSection, OtherLinksColorKey, OtherLinksColor);
            OnChanged(LinesSection, LineOpacityKey, LineOpacity);
        }

        /// <summary>Attaches the one-line change log to a bound entry.</summary>
        /// <typeparam name="T">The entry's value type.</typeparam>
        /// <param name="section">The section the entry lives in, for the message.</param>
        /// <param name="key">The key the entry is bound to, for the message.</param>
        /// <param name="value">The bound entry, or <c>null</c>.</param>
        private static void OnChanged<T>(string section, string key, ConfigValue<T> value)
        {
            if (value == null)
            {
                return;
            }

            value.RegisterCallback((from, to) =>
                Announce(section + "/" + key, from, to));
        }

        /// <summary>
        /// Writes the change line, through the live plugin when there is one.
        /// </summary>
        /// <typeparam name="T">The entry's value type.</typeparam>
        /// <param name="path">The <c>section/key</c> the change belongs to.</param>
        /// <param name="from">The replaced value.</param>
        /// <param name="to">The new value.</param>
        /// <remarks>
        /// The logger is reached through <c>CommNextReduxPlugin.Instance</c> rather than held, because
        /// the loader assigns <c>SWLogger</c> after <c>Initialize</c> can have run - and the plugin's
        /// own <c>LogLine</c> drops the line rather than throwing when there is no logger yet, which is
        /// what keeps this legal inside a config setter.
        /// </remarks>
        private static void Announce<T>(string path, T from, T to)
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.LogLine("config change: " + path + " " + from + " -> " + to);
        }
    }
}
