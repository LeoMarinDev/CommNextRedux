// CommNextRedux - the map connection lines' configurable appearance (U6g): the per-band colours
// and the line-opacity multiplier behind Settings -> Mods -> CommNextRedux -> "Lines".
//
// THE PROBLEM THIS SOLVES
//   Until U6g every line's colour was hard-coded: the band the gate selected for an edge
//   (`NetworkBands.All[bandIndex].Color`), the relay-to-relay fallback
//   (`MapConnectionComponent.RelayColor`) or the plain-link fallback (`NetworkBands.NoBandColor`).
//   The player asked to be able to change the lines' colour and their transparency, and chose
//   "one colour per band, plus one opacity for all of them".
//
// WHY THE OVERRIDES ARE PARSED ONCE, NOT PER LINE
//   `Apply` runs once per link per renderer pass and must not allocate, so the strings are parsed
//   when they change (boot, and each config change) into a per-slot cache; `Apply` then does one
//   array read, one bool test and - only when the opacity is not 1 - one multiply. With no key
//   overridden and the default opacity the returned `Color` is the very value the caller passed
//   in, which is what makes the defaults provably rendering-neutral.
//
// WHERE THE PARSER COMES FROM
//   `UnityEngine.ColorUtility.TryParseHtmlString` - the same conversion the band icons already use.
//   `BandIcon`'s `color` attribute is a `UxmlColorAttributeDescription` whose `GetValueFromBag`
//   routes the markup string through `UxmlColorAttributeDescription/<>c::<GetValueFromBag>b__3_0`,
//   i.e. the same `ColorUtility` call this file makes (IL read out of the shipped
//   `UnityEngine.UIElementsModule.dll`), so `#B325D4FF` on an icon and `#B325D4FF` on a line are
//   the same colour by construction rather than by review.
//
// WHERE THE BUILT-IN COLOURS COME FROM
//   The three fields `ConnectionsRenderer.ResolveColor` passes in as its fallbacks:
//   `NetworkBands.All[i].Color`, `MapConnectionComponent.RelayColor` and
//   `NetworkBands.NoBandColor`. `BuiltInOf` reads the same three for the boot line's "what is in
//   effect" report; the entry descriptions read them through `NetworkConfig.BuiltInColorHex`.
//
// WHAT THIS FILE DOES NOT DO
//   It does not draw anything (ConnectionsRenderer owns the pass), it does not own the config
//   entries (NetworkConfig binds them), and it does not touch the range rulers - the "Line opacity"
//   setting is deliberately scoped to the connection lines, and `Apply` is called from the line
//   pass only.

using System;
using System.Globalization;
using CommNextRedux.Network;
using CommNextRedux.Network.Bands;
using ReduxLib.Configuration;
using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// The colour slots the Lines settings can override, in the order the entries are read (U6g).
    /// </summary>
    /// <remarks>
    /// The first five mirror <c>NetworkBands.All</c>'s bit order (X = 0 … V = 4) because a band
    /// index selects a slot directly - see <see cref="LineAppearance.BandSlot"/>, which is the only
    /// place that mapping is made. The last two are the renderer's own fallbacks, which are not
    /// bands at all: a relay-to-relay hop and anything else the band gate left unbanded.
    /// </remarks>
    public enum LineColorSlot
    {
        /// <summary>The X band - the default band, and the one a modulator with no selection reports.</summary>
        XBand = 0,

        /// <summary>The S band.</summary>
        SBand = 1,

        /// <summary>The K band.</summary>
        KBand = 2,

        /// <summary>The Ka band.</summary>
        KaBand = 3,

        /// <summary>The V band.</summary>
        VBand = 4,

        /// <summary>A link between two relays that the band gate put no band on.</summary>
        RelayHop = 5,

        /// <summary>Every other link the band gate put no band on.</summary>
        Other = 6
    }

    /// <summary>
    /// The live line appearance: the parsed colour overrides and the opacity multiplier the line
    /// pass reads, plus the one boot line that reports them.
    /// </summary>
    /// <remarks>
    /// Static, like <see cref="ConnectionsRenderer"/> whose pass it feeds, and wired once by
    /// <c>CommNextReduxPlugin.EnsureLineAppearanceWiring</c> after the renderer exists.
    /// </remarks>
    public static class LineAppearance
    {
        /// <summary>How many colour slots there are - one per entry in <see cref="LineColorSlot"/>.</summary>
        public const int SlotCount = 7;

        /// <summary>The parsed override per slot, valid only where <see cref="_hasOverride"/> is set.</summary>
        private static readonly Color[] _overrides = new Color[SlotCount];

        /// <summary>Whether a slot has a parsed override; <c>false</c> means "draw the built-in".</summary>
        private static readonly bool[] _hasOverride = new bool[SlotCount];

        /// <summary>The raw string each slot's parsed value came from, or <c>null</c> for none.</summary>
        private static readonly string[] _lastRaw = new string[SlotCount];

        /// <summary>Whether this slot's unparseable value has already been reported (once per value).</summary>
        private static readonly bool[] _warned = new bool[SlotCount];

        /// <summary>The opacity multiplier, parsed once per change. 1 = identity.</summary>
        private static float _opacity = 1f;

        private static Action<string> _log;
        private static Action<string> _warn;

        private static bool _wired;

        /// <summary>Slot names for the log lines, in <see cref="LineColorSlot"/> order.</summary>
        private static readonly string[] SlotNames =
        {
            "X band", "S band", "K band", "Ka band", "V band", "relay hop", "other links"
        };

        /// <summary>The config key each slot's override comes from, in the same order.</summary>
        private static readonly string[] SlotKeys =
        {
            NetworkConfig.XBandColorKey,
            NetworkConfig.SBandColorKey,
            NetworkConfig.KBandColorKey,
            NetworkConfig.KaBandColorKey,
            NetworkConfig.VBandColorKey,
            NetworkConfig.RelayHopColorKey,
            NetworkConfig.OtherLinksColorKey
        };

        /// <summary>
        /// Hands this file the plugin's null-guarded writers. Called before <see cref="Wire"/>.
        /// </summary>
        /// <param name="log">Informational sink; must not throw.</param>
        /// <param name="warn">Warning sink; must not throw.</param>
        public static void Initialize(Action<string> log, Action<string> warn)
        {
            _log = log;
            _warn = warn;
        }

        /// <summary>
        /// Parses the eight entries, subscribes to them, and writes the one boot line. Idempotent.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Called once, after the config is bound and the renderer exists</b> (the plugin's
        /// <c>EnsureLineAppearanceWiring</c>, from <c>EnsureRenderer</c>). The flag is set first for
        /// the reason the sibling wiring methods give: a second subscription would accumulate one
        /// delegate per call for the life of the process.
        /// </para>
        /// <para>
        /// <b>The boot line is the positive evidence the phase rests on:</b> it lists all seven
        /// effective colours with their source (built-in or override) and the opacity, so a launch
        /// proves both that the defaults are untouched and that the keys are wired - without it, an
        /// all-defaults run would be indistinguishable from a build that never read the keys at all.
        /// </para>
        /// </remarks>
        public static void Wire()
        {
            if (_wired)
            {
                return;
            }

            _wired = true;

            Refresh();
            Subscribe();
            Write(_log, Describe());
        }

        /// <summary>The slot a band index belongs to - the one place that mapping is made.</summary>
        /// <param name="bandIndex">A <c>NetworkBands</c> bit index (0..4).</param>
        /// <returns>The matching slot; <see cref="LineColorSlot.XBand"/> for an out-of-range index.</returns>
        /// <remarks>
        /// Sits next to the enum so a reordered band table is a one-line change here, and it is the
        /// reason <see cref="LineColorSlot"/>'s first five members are documented as mirroring the
        /// band order rather than merely happening to.
        /// </remarks>
        public static LineColorSlot BandSlot(int bandIndex)
        {
            return bandIndex >= (int)LineColorSlot.XBand && bandIndex <= (int)LineColorSlot.VBand
                ? (LineColorSlot)bandIndex
                : LineColorSlot.XBand;
        }

        /// <summary>
        /// The colour a link is drawn in: the player's override for the slot when there is one, the
        /// slot's built-in colour otherwise, with the line-opacity setting applied.
        /// </summary>
        /// <param name="slot">Which colour slot the link resolved to.</param>
        /// <param name="builtIn">The colour the renderer would have drawn before this setting existed.</param>
        /// <returns>The colour to hand to <c>MapConnectionComponent.SetColor</c>.</returns>
        /// <remarks>
        /// <b>The hot path, and allocation-free by construction</b> (array reads, a bool test and at
        /// most one multiply). With the default opacity and no override the return value is
        /// <paramref name="builtIn"/> unchanged - not a re-derived approximation of it - which is
        /// what makes the shipped defaults byte-for-byte what every earlier phase drew.
        /// </remarks>
        public static Color Apply(LineColorSlot slot, Color builtIn)
        {
            int index = (int)slot;
            if (index < 0 || index >= SlotCount)
            {
                // Unreachable today; a future slot added to the enum without a cache entry must not
                // silently read a neighbour's colour, so it falls back to the built-in instead.
                return builtIn;
            }

            Color color = _hasOverride[index] ? _overrides[index] : builtIn;
            if (_opacity != 1f)
            {
                color.a *= _opacity;
            }

            return color;
        }

        /// <summary>Re-reads all eight entries. Called at boot and never on the per-line path.</summary>
        private static void Refresh()
        {
            _opacity = (float)NetworkConfig.LineOpacityFactor;

            for (int slot = 0; slot < SlotCount; slot++)
            {
                ConfigValue<string> entry = ColorEntry(slot);
                ParseSlot(slot, entry == null ? null : entry.Value);
            }
        }

        /// <summary>Subscribes to the eight entries, so a change lands on the next line pass.</summary>
        /// <remarks>
        /// The callbacks are additive: <c>NetworkConfig.RegisterChangeCallbacks</c> also registers
        /// one per entry, for the change log, and <c>ConfigValue.RegisterCallback</c> forwards to an
        /// event (<c>Callbacks +=</c>), so neither replaces the other. Verified in the pinned
        /// ReduxLib source the sibling wiring methods cite.
        /// </remarks>
        private static void Subscribe()
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                int captured = slot;
                ConfigValue<string> entry = ColorEntry(slot);
                if (entry == null)
                {
                    continue;
                }

                entry.RegisterCallback((from, to) => OnColorChanged(captured, to));
            }

            ConfigValue<double> opacity = NetworkConfig.LineOpacity;
            if (opacity != null)
            {
                opacity.RegisterCallback((from, to) => OnOpacityChanged());
            }
        }

        /// <summary>Re-parses one colour slot and asks the renderer for a pass.</summary>
        /// <param name="slot">The slot whose entry changed.</param>
        /// <param name="raw">The new raw string.</param>
        private static void OnColorChanged(int slot, string raw)
        {
            ParseSlot(slot, raw);

            Write(_log, "lines-appearance: " + SlotNames[slot] + " -> " + SlotKeys[slot] + "=\""
                + (raw == null ? string.Empty : raw) + "\", so it draws "
                + (_hasOverride[slot]
                    ? NetworkConfig.BuiltInColorHex(_overrides[slot])
                    : NetworkConfig.BuiltInColorHex(BuiltInOf(slot)) + " (built-in)")
                + " (opacity " + OpacityText() + "); the next line pass repaints every line already drawn");

            ConnectionsRenderer.MarkAsDirty();
        }

        /// <summary>Re-reads the opacity and asks the renderer for a pass.</summary>
        private static void OnOpacityChanged()
        {
            _opacity = (float)NetworkConfig.LineOpacityFactor;

            Write(_log, "lines-appearance: line opacity -> " + OpacityText() + " ("
                + (_opacity >= 1f ? "fully solid" : _opacity <= 0f ? "invisible" : "partly transparent")
                + "); the next line pass repaints every line already drawn");

            ConnectionsRenderer.MarkAsDirty();
        }

        /// <summary>Parses one raw colour string into a slot's cache.</summary>
        /// <param name="slot">The slot to write.</param>
        /// <param name="raw">The saved string; empty or null clears the override.</param>
        /// <remarks>
        /// Three states, and each is deliberate: empty clears the override (and re-arms the
        /// warning, so a fixed-then-broken value is reported again); an unchanged string returns
        /// without re-parsing, because the callback can fire with the same text and re-parsing it
        /// would be busywork; an unparseable string warns ONCE per value and leaves the built-in
        /// colour in place, so a typo can never blank a line or throw out of a config setter.
        /// </remarks>
        private static void ParseSlot(int slot, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                _hasOverride[slot] = false;
                _warned[slot] = false;
                _lastRaw[slot] = null;
                return;
            }

            if (raw == _lastRaw[slot])
            {
                return;
            }

            _lastRaw[slot] = raw;

            Color parsed;
            if (ColorUtility.TryParseHtmlString(raw, out parsed))
            {
                _overrides[slot] = parsed;
                _hasOverride[slot] = true;
                _warned[slot] = false;
                return;
            }

            _hasOverride[slot] = false;
            if (_warned[slot])
            {
                return;
            }

            _warned[slot] = true;
            Write(_warn, "lines-appearance: \"" + raw + "\" is not a colour this game can read "
                + "(#RRGGBB or #RRGGBBAA, or a colour name such as red), so the slot named \""
                + SlotKeys[slot] + "\" keeps its built-in colour "
                + NetworkConfig.BuiltInColorHex(BuiltInOf(slot)) + " - never a blank line, and this "
                + "warning is written once per value per session");
        }

        /// <summary>The entry behind a slot, or <c>null</c> when the config is not bound.</summary>
        /// <param name="slot">The slot, in <see cref="LineColorSlot"/> order.</param>
        /// <returns>The bound entry, or <c>null</c>.</returns>
        private static ConfigValue<string> ColorEntry(int slot)
        {
            switch (slot)
            {
                case 0: return NetworkConfig.XBandColor;
                case 1: return NetworkConfig.SBandColor;
                case 2: return NetworkConfig.KBandColor;
                case 3: return NetworkConfig.KaBandColor;
                case 4: return NetworkConfig.VBandColor;
                case 5: return NetworkConfig.RelayHopColor;
                case 6: return NetworkConfig.OtherLinksColor;
                default: return null;
            }
        }

        /// <summary>The colour a slot falls back to, read from the renderer's own sources.</summary>
        /// <param name="slot">The slot, in <see cref="LineColorSlot"/> order.</param>
        /// <returns>The built-in colour for that slot.</returns>
        /// <remarks>
        /// Only the boot line and the failure warnings call this; the renderer passes the same value
        /// into <see cref="Apply"/> from its own resolution. The three sources are named once in each
        /// place and are the same three fields - <c>NetworkBands.All</c> for the bands,
        /// <c>MapConnectionComponent.RelayColor</c> and <c>NetworkBands.NoBandColor</c> for the two
        /// fallbacks.
        /// </remarks>
        private static Color BuiltInOf(int slot)
        {
            switch (slot)
            {
                case 0: return NetworkBands.All[0].Color;
                case 1: return NetworkBands.All[1].Color;
                case 2: return NetworkBands.All[2].Color;
                case 3: return NetworkBands.All[3].Color;
                case 4: return NetworkBands.All[4].Color;
                case 5: return MapConnectionComponent.RelayColor;
                default: return NetworkBands.NoBandColor;
            }
        }

        /// <summary>The one bounded boot line: every effective colour, its source, and the opacity.</summary>
        /// <returns>The line, without the log level prefix.</returns>
        private static string Describe()
        {
            string text = "lines-appearance: effective line colours and opacity -";

            string overridden = string.Empty;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                bool isOverride = _hasOverride[slot];
                text += " " + SlotNames[slot] + "="
                    + NetworkConfig.BuiltInColorHex(Apply((LineColorSlot)slot, BuiltInOf(slot)));

                if (isOverride)
                {
                    text += " (override \"" + _lastRaw[slot] + "\")";
                    overridden = overridden.Length == 0
                        ? SlotKeys[slot]
                        : overridden + ", " + SlotKeys[slot];
                }
                else
                {
                    text += " (built-in)";
                }

                text += slot == SlotCount - 1 ? ";" : ",";
            }

            return text
                + " line opacity=" + OpacityText() + " (1.00 = fully solid, 0 = invisible)"
                + " - overridden keys: " + (overridden.Length == 0 ? "none" : overridden)
                + "; the rest are the colours every build before this setting drew."
                + " The keys are Settings -> Mods -> CommNextRedux -> \"Lines\""
                + " (leave a colour empty for the built-in, or type #RRGGBB / #RRGGBBAA);"
                + " this line is written once, at boot.";
        }

        /// <summary>The opacity as the two-decimal text the setting's own label uses.</summary>
        private static string OpacityText()
        {
            return _opacity.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Writes through a callback that is allowed to be absent.</summary>
        /// <param name="write">The callback, or <c>null</c>.</param>
        /// <param name="message">The line.</param>
        private static void Write(Action<string> write, string message)
        {
            if (write != null)
            {
                write(message);
            }
        }
    }
}
