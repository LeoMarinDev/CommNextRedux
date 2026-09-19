// CommNextRedux - the RF band model.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Network/Bands/NetworkBands.cs
//   mods-outdated/CommNext/src/CommNext/Network/Bands/NetworkBand.cs
//
//   Carried over: the five bands with their codes and display names, `DefaultBand`, the
//   code -> index lookup with -1 for an unknown code, and the count. The bit index of a band is
//   its position in `All` (X = bit 0, S = bit 1, K = bit 2, Ka = bit 3, V = bit 4), which is the
//   order the legacy used for its `BandsFlags` mask and the order this port reads back out of it,
//   so the two agree bit for bit.
//
//   RESHAPED, not dropped: the legacy's singleton (`Instance`, two lazily built dictionaries and
//   an `AllBandsCache`) is a static readonly table here. The lookup tables are immutable data and
//   never dirty - the legacy's own `// TODO When bands will be editable, we need to dirty this
//   cache` never happened - so the dictionary is replaced by a linear scan over five entries,
//   which cannot allocate and cannot go stale.
//
//   STILL NOT carried over: `GetIconSprite` (which built a 16x16 procedural `Texture2D` per
//   band). That belongs to the band UI - the window phase - and a texture that the game never
//   loads is exactly the kind of asset this port does not ship until it is drawn. The display
//   names stay, because the log lines and the probe use them.
//
//   RESTORED IN PHASE 6 - the `Color` on each band. P5 left it out on the grounds that it was the
//   band UI's, and that was wrong: the map connection lines are also a consumer. The legacy
//   coloured each drawn line from `SelectedBand`'s own colour
//   (`MapConnectionComponent.cs:193`: `Band?.Color ?? (_isRelay ? RelayColor : LinkColor)`), so
//   without it every line would have been the same green. The values below are the legacy's table
//   verbatim, RGB for RGB.
//
//   ONE DELIBERATE CHANGE TO THAT TABLE: the legacy's S and Ka entries passed no alpha to
//   `new Color(r, g, b)` and therefore carried `a = 0` - fully transparent. On the legacy's own
//   ShaderGraph material that may or may not have shown; on this port's route (`Sprites/Default`,
//   built at runtime, which honours alpha) it would have drawn nothing at all, and "band S lines
//   are invisible" is indistinguishable from a broken renderer. The RGB is the legacy's; the alpha
//   is 1 on all five. Recorded in Deploy/obj/divergences.md.
//
// WHY THIS IS THE PORT'S OWN DATA AND NOT THE GAME'S
//   KSP 2 has no band concept. `KSP.Sim.ConnectionGraphNodeFlags` is
//   `{ None, IsActive, IsControlSource }` (monodis --fields on Assembly-CSharp.dll,
//   flist 30184-30187) and there is no `IsRelay` and no `HasEnoughResources` on a node anywhere
//   in the runtime - both are CommNext inventions. So a band, like a relay flag, lives in a
//   side table this mod owns and indexes by node; nothing here is a wrapper around a game type.
//
// WHAT CONSUMES IT
//   `NetworkEngine`'s node-state pass, which turns each part's modulator selection into a
//   `BandsFlags` mask, and the band-match gate on the edge set. The probe prints the codes.
//   Only `NetworkBands.GetBandIndex` and `NetworkBands.Count` are on that hot path - the colours
//   added in Phase 6 are read only by the map renderer, once per line per refresh.

using UnityEngine;

namespace CommNextRedux.Network.Bands
{
    /// <summary>
    /// One radio band: the code the band is keyed by, the name a window would show, and the colour
    /// a map line is drawn in when this band is the one the gate selected.
    /// </summary>
    /// <remarks>
    /// Three immutable fields. <c>Code</c> was marked in the legacy as "the encoded name which
    /// will be saved to the save file", and that is still not this port's business - it writes no
    /// band to a save. <c>Color</c> is, since Phase 6: the map connection lines are coloured from
    /// it. See the file header for where the table comes from and where its alpha differs from the
    /// legacy's.
    /// </remarks>
    public sealed class NetworkBand
    {
        /// <summary>Creates a band.</summary>
        /// <param name="code">The short code, e.g. <c>"X"</c>. Unique within the set.</param>
        /// <param name="displayName">The name shown to a player, e.g. <c>"X Band"</c>.</param>
        /// <param name="color">The colour this band's connections are drawn in.</param>
        public NetworkBand(string code, string displayName, Color color)
        {
            Code = code;
            DisplayName = displayName;
            Color = color;
        }

        /// <summary>The short code, e.g. <c>"X"</c>. This is what a modulator selects.</summary>
        public string Code { get; }

        /// <summary>The name shown to a player, e.g. <c>"X Band"</c>.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// The colour a connection is drawn in when this is the band the gate selected for it.
        /// </summary>
        /// <remarks>
        /// Read once per line per refresh by the map renderer (<c>MapConnectionComponent</c>), which
        /// assigns it to its own material instance. Never null - a band without a colour would be a
        /// band whose lines silently take the fallback.
        /// </remarks>
        public Color Color { get; }
    }

    /// <summary>
    /// The band set: what a band is called, which one is the default, and how a code maps to the
    /// bit index a <c>BandsFlags</c> mask uses.
    /// </summary>
    public static class NetworkBands
    {
        /// <summary>
        /// The band a modulator operates on when nothing has selected another one, and the reason
        /// the band-match gate is a no-op on a default install: every patched transmitter ends up
        /// carrying this band, so no pair is ever removed for having nothing in common.
        /// </summary>
        public const string DefaultBand = "X";

        /// <summary>
        /// How many bands there are. Also the width of a <c>BandsFlags</c> mask and of the
        /// per-band range table <see cref="CommNextRedux.Network.NetworkEngine"/> keeps beside it.
        /// </summary>
        public const int Count = 5;

        /// <remarks>
        /// The colours are the legacy's `AllBands` table
        /// (<c>mods-outdated/CommNext/src/CommNext/Network/Bands/NetworkBands.cs:14-18</c>), RGB for
        /// RGB, except that every alpha is 1: the legacy's S and Ka rows used the three-argument
        /// <c>Color</c> constructor and so carried <c>a = 0</c>. See the file header.
        /// </remarks>
        private static readonly NetworkBand[] Bands =
        {
            new NetworkBand(DefaultBand, "X Band", new Color(0.174f, 0.783f, 0.777f, 1.000f)),
            new NetworkBand("S", "S Band", new Color(0.090f, 0.570f, 0.970f, 1.000f)),
            new NetworkBand("K", "K Band", new Color(0.440f, 0.390f, 1.000f, 1.000f)),
            new NetworkBand("Ka", "Ka Band", new Color(0.840f, 0.150f, 0.920f, 1.000f)),
            new NetworkBand("V", "V Band", new Color(0.170f, 0.850f, 0.580f, 1.000f))
        };

        /// <summary>
        /// The colour a connection is drawn in when no band was selected for it, and the colour of
        /// every line whose band is unknown.
        /// </summary>
        /// <remarks>
        /// The legacy's own fallback, and it is not an arbitrary choice: it is exactly
        /// <c>MapConnectionComponent.LinkColor</c> (<c>new Color(0.212f, 0.765f, 0.345f, 1.0f)</c>,
        /// the legacy's line at 32) - the colour of a plain non-relay link - which is what the
        /// legacy drew whenever <c>HasMatchingBand</c> was false
        /// (<c>MapConnectionComponent.cs:193</c>). Kept here rather than in the component so the
        /// renderer's fallback and the band table are read from one place.
        /// </remarks>
        public static readonly Color NoBandColor = new Color(0.212f, 0.765f, 0.345f, 1.0f);

        /// <summary>
        /// Every band, in bit order. Index <c>i</c> in this array is bit <c>i</c> of a
        /// <c>BandsFlags</c> mask.
        /// </summary>
        public static NetworkBand[] All
        {
            get { return Bands; }
        }

        /// <summary>
        /// The bit index of a band code, or <c>-1</c> when the code is unknown, empty or null.
        /// </summary>
        /// <param name="bandCode">
        /// The code to resolve. May be null or empty: a modulator's <c>SecondaryBand</c> defaults
        /// to the empty string, and "no second band" has to be representable.
        /// </param>
        /// <returns>
        /// The zero-based index of the band (its mask bit), or <c>-1</c>. The legacy returned -1
        /// from the same lookup and its caller relied on that, so the sentinel is part of the
        /// carried-over contract - callers must test for it before touching a mask.
        /// </returns>
        public static int GetBandIndex(string bandCode)
        {
            if (string.IsNullOrEmpty(bandCode)) return -1;

            for (int i = 0; i < Bands.Length; i++)
            {
                if (Bands[i].Code == bandCode) return i;
            }

            return -1;
        }

        /// <summary>The code of a band by index, or the empty string when the index is out of range.</summary>
        /// <param name="bandIndex">A mask bit position.</param>
        public static string GetCode(int bandIndex)
        {
            return bandIndex >= 0 && bandIndex < Bands.Length ? Bands[bandIndex].Code : string.Empty;
        }

        /// <summary>A mask with only <paramref name="bandIndex"/> set, or 0 when the index is out of range.</summary>
        /// <param name="bandIndex">A mask bit position.</param>
        /// <remarks>
        /// The single place a bit is computed, so the gate, the node-state pass and any future
        /// band UI cannot disagree about which bit a band is.
        /// </remarks>
        public static int MaskOf(int bandIndex)
        {
            return bandIndex >= 0 && bandIndex < Bands.Length ? 1 << bandIndex : 0;
        }

        /// <summary>Every band set, as a mask. The value a transmitter with no modulator is credited.</summary>
        public static int AllBandsMask
        {
            get { return (1 << Bands.Length) - 1; }
        }

        /// <summary>
        /// Renders a mask as the codes it contains, e.g. <c>"X+Ka"</c>, or <c>"-"</c> for an empty
        /// mask. Used by the diagnostics only; it allocates, so it is never on the pass path.
        /// </summary>
        /// <param name="bandsFlags">The mask to render.</param>
        public static string DescribeMask(int bandsFlags)
        {
            if (bandsFlags == 0) return "-";

            string result = string.Empty;
            for (int i = 0; i < Bands.Length; i++)
            {
                if ((bandsFlags & (1 << i)) == 0) continue;
                result = result.Length == 0 ? Bands[i].Code : result + "+" + Bands[i].Code;
            }

            return result.Length == 0 ? "-" : result;
        }
    }
}
