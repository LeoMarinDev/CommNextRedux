// CommNextRedux - the D-L21-1 override: the game's OWN comm-line drawer is silenced.
//
// THE DEFECT THIS FIXES (rank-1 evidence in Deploy/obj/u06d-commlines.md)
//   The pin ships its own CommNet line drawer, KSP.Map.CommNetLineRenderer (banked at rank 2 as
//   `T:KSP.Map.CommNetLineRenderer`). It subscribes to MapInitializedMessage and
//   ConnectionGraphUpdatedMessage, fills a CommandBuffer named "Commnet Line Drawer" from
//   CommNetManager.GetConnections(), and draws every edge in HARDCODED GREEN: the draw body calls
//   UnityEngine.Color::get_green() directly into Hslr.Path::WriteLine(Vector3, Vector3, Color).
//   There is no band colour, no per-link colour and no occlusion styling - one flat green for the
//   whole graph. And it defaults ON: PersistentSettings..ctor writes ldc.i4.1 into ShowCommNetLines.
//
//   Because this mod replaces ConnectionGraph.RebuildConnectionGraph, the game's drawer renders
//   THIS MOD'S OWN graph: the same tree is drawn twice, once in the mod's band colours and once in
//   flat green on top of it. That is why the player's "Connections mode" change reads as having no
//   effect - the mod's lines do change, but the green overlay never leaves.
//
// WHY THE GETTER IS THE SEAM (and NOT DrawCommNetLines itself)
//   DrawCommNetLines(Camera) opens with `IL_0031: callvirt CommandBuffer::Clear()` and only then
//   evaluates `IL_0036: call bool PersistentProfileManager::get_ShowCommNetLines()`, returning at
//   `IL_003d` when it is false. A prefix that skipped the whole method would therefore also skip the
//   Clear, leaving the previous frame's line geometry baked into the command buffer. The static
//   getter is the one point where the game's own early-out does the work with the buffer already
//   cleared: force the result false and the method keeps its own contract.
//
// WHY A POSTFIX THAT WRITES __result IS DELIBERATE - THE ONE SANCTIONED DIVERGENCE
//   The repo's standing doctrine is capture-only: a non-gameplay mod uses postfixes that take no
//   __result and no ref/out, so the patch is structurally incapable of changing the game. This patch
//   is the deliberate exception, approved by the user as the D-L21-1 fix: it exists to change what
//   the game's drawer draws, and `__result` is the only seam that does it without breaking the
//   Clear-before-gate order above. It is NOT a capture-only hook and must NOT be "corrected" back
//   into one: a postfix with no __result here is a patch that does nothing at all.
//
// WHAT IT NEVER DOES
//   It never writes PersistentSettings.ShowCommNetLines and never calls set_ShowCommNetLines. The
//   player's real value stays in the generated config/settings untouched, so removing this mod
//   restores stock behaviour exactly. The override is a read-path interception only.
//
// THE HONEST SIDE EFFECT, RECORDED SO A READER IS NOT SURPRISED
//   The game's own Settings -> Gameplay -> "Show CommNet Lines" toggle reads this same getter
//   (UitkGameplaySettingsManager). While the override is active that toggle renders as OFF and
//   flipping it has no visible effect on the map - the persisted value is still written by the
//   setter and comes back the moment this mod stops overriding (or the override is turned off in
//   Settings -> Mods). Rank-1 measurement: the getter has exactly two call sites in
//   Assembly-CSharp - this drawer's gate and that settings read-back; the setter's callers are
//   unaffected.
//
// CHEAP, ALLOCATION-FREE AND SAFE BEFORE BOOT
//   The postfix reads one static bool and, only when the game's own value is true, writes it false.
//   No allocation, no logging, no exception path, no dereference of anything the loader assigns. The
//   cached decision is armed by the plugin from the config before Harmony is installed and refreshed
//   once per config change, so the draw path never touches the config object; until it is armed the
//   field is false and the game's own setting is honoured (the safe, no-behaviour-change default).

using HarmonyLib;
using KSP.Game;

namespace CommNextRedux.Patches
{
    /// <summary>
    /// Forces <c>PersistentProfileManager.get_ShowCommNetLines()</c> to report <c>false</c> while the
    /// config asks for the game's own CommNet lines to be hidden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The target is a STATIC property getter.</b> Rank-1 shape (the orchestrator's dump of the
    /// shipped <c>Assembly-CSharp</c>, reused rather than re-dumped):
    /// </para>
    /// <code>
    /// .method public static hidebysig specialname default bool get_ShowCommNetLines () cil managed
    /// IL_0000:  call class KSP.Game.PersistentSettings class KSP.Game.PersistentProfileManager::get_Settings()
    /// IL_0005:  ldfld bool KSP.Game.PersistentSettings::ShowCommNetLines
    /// IL_000a:  ret
    /// </code>
    /// <para>
    /// A static getter means the postfix takes no <c>__instance</c> - there is none - and the
    /// annotation derives the accessor from the property's own name with <see cref="MethodType.Getter"/>,
    /// so a rename upstream breaks the build rather than silently turning this into a patch that does
    /// not apply.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(PersistentProfileManager), nameof(PersistentProfileManager.ShowCommNetLines),
        MethodType.Getter)]
    public static class StockCommNetLinesPatch
    {
        /// <summary>
        /// The greppable prefix of every line this patch writes, so a launch can prove it armed.
        /// </summary>
        /// <remarks>
        /// One owner: the plugin never writes an override line itself, it calls
        /// <see cref="Apply"/>, and the shape is fixed here. The log is written only on boot and on
        /// a real change - never from the postfix, which runs on the game's draw path.
        /// </remarks>
        public const string LogPrefix = "stock-comm-lines: ";

        /// <summary>
        /// The cached decision the draw path reads. <c>false</c> until the plugin arms it.
        /// </summary>
        /// <remarks>
        /// <c>volatile</c> because it is written from the config-change callback and read from the
        /// game's draw/frame path; the read is a single field load on the cheap branch.
        /// </remarks>
        private static volatile bool _hidden;

        /// <summary>Whether <see cref="Apply"/> has ever run, so the boot line is written exactly once.</summary>
        private static bool _armed;

        /// <summary>
        /// Whether the game's own lines are currently being forced off - for diagnostics and for the
        /// boot line; the postfix uses the field directly.
        /// </summary>
        public static bool Hidden => _hidden;

        /// <summary>
        /// Sets the cached decision and writes one line when it changes.
        /// </summary>
        /// <param name="hidden"><c>true</c> to force the game's own lines off.</param>
        /// <param name="context">Where the decision came from, for the log line.</param>
        /// <remarks>
        /// <para>
        /// Called by the plugin from <c>EnsureConfiguration</c> (before Harmony is installed, so the
        /// cache is set before the postfix can ever run) and from its config-change callback. A call
        /// that does not change the value logs nothing, so the boot line appears once and a runtime
        /// flip appears once per flip.
        /// </para>
        /// <para>
        /// The logger is reached through <c>CommNextReduxPlugin.Instance</c> and the plugin's own
        /// <c>LogLine</c> drops the message rather than throwing while there is no logger yet - which
        /// is what keeps this callable from the config path.
        /// </para>
        /// </remarks>
        public static void Apply(bool hidden, string context)
        {
            if (_armed && hidden == _hidden)
            {
                return;
            }

            _hidden = hidden;
            _armed = true;

            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                return;
            }

            if (hidden)
            {
                plugin.LogLine(LogPrefix + "override ACTIVE - the game's own CommNet line drawer is "
                    + "forced OFF (it draws every edge of the graph in flat green, on top of this "
                    + "mod's band-coloured lines). The game's own Show CommNet Lines setting is never "
                    + "written, so its value survives untouched; its Gameplay settings toggle will "
                    + "read as OFF while this is active. [" + context + "]");
                return;
            }

            plugin.LogLine(LogPrefix + "override INACTIVE - the game's own CommNet line drawer is "
                + "drawn from the game's own Show CommNet Lines setting again. [" + context + "]");
        }

        /// <summary>
        /// Harmony postfix on the static getter: reports the game's lines as off while the override
        /// is active.
        /// </summary>
        /// <param name="__result">The game's own <c>ShowCommNetLines</c> value; overwritten to
        /// <c>false</c> when the override is armed.</param>
        /// <remarks>
        /// The <c>__result &amp;&amp;</c> guard makes the common case (the game's setting already
        /// off) a single field read of the value the game computed, with no access to this class at
        /// all. No allocation, no logging, no exception path: this runs inside the map's draw pass.
        /// </remarks>
        // ReSharper disable once InconsistentNaming
        public static void Postfix(ref bool __result)
        {
            if (__result && _hidden)
            {
                __result = false;
            }
        }
    }
}
