// CommNextRedux - the one place a localization KEY becomes text.
//
// WHY THIS FILE EXISTS  (F63, D42)
//   `LocalizedStrings` holds `const string` KEYS, not display text - see that file's header. The
//   L10 launch measured what happens when a key reaches the screen untranslated:
//
//     ui-tooltip: first tooltip of this session shown ('CommNext/UI/ConnectionsDisplayModeLines')
//
//   Two files were correct (the constant and its CSV row) and the defect was purely in the display
//   path. The fix was a private `Translate` helper on `MapToolbarWindowController`; the vessel
//   report is the second screen-bound surface, so the helper moves here and the toolbar's becomes a
//   one-line delegation to it. One owner per fact: two implementations of "look up, fall back to
//   the key" would drift, and the fallback is the *interesting* half - it is a deliberate choice,
//   not an accident (see below).
//
// THE FALLBACK IS DELIBERATE
//   A missing row renders the KEY rather than an empty string. `LocalizationManager.GetTranslation`
//   returns null (or an empty string) for a term it cannot resolve, and an empty label is
//   indistinguishable from "the window failed to bind" - whereas `CommNext/UI/FilterLabel` on
//   screen is unmistakably "this row is missing from the CSV". A wrong-but-visible string is a
//   reportable defect; an empty one looks like a UI bug.
//
// TWO OVERLOADS, BOTH MEASURED ON THIS PIN
//   `I2.Loc.LocalizationManager.GetTranslation(string Term, [opt] bool FixForRTL, ...)` is the
//   one-argument call (mlist 11886 of `Assembly-CSharp.dll`, every parameter after `Term`
//   optional), and `GetTranslation(string Term, object[] Params)` (mlist 11887) is the substitution
//   form the legacy used for `Distance {0}` / `Range {0}`. A `string[]` binds to the second by
//   normal array covariance, which is what the legacy relied on and what the report's two
//   substituted rows need.
//
// WHAT THIS FILE DOES NOT DO
//   It does not cache, and it does not touch `LocalizedStrings`. I2.Loc's own lookup is a dictionary
//   hit in the current language's table; the report calls this a handful of times per refresh (and
//   only for row text that is actually being written), which is not a reason to add a cache that
//   would then have to be invalidated on a language change the game cannot perform mid-session.

using I2.Loc;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Resolves a localization key to display text, with the key itself as the visible fallback.
    /// </summary>
    public static class Localize
    {
        /// <summary>Resolves a key with no substitutions.</summary>
        /// <param name="key">The key, from <see cref="LocalizedStrings"/>.</param>
        /// <returns>The translated text, or the key when the table has no row for it.</returns>
        public static string Text(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string text = LocalizationManager.GetTranslation(key);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        /// <summary>
        /// Resolves a key whose row carries <c>{0}</c>-style placeholders.
        /// </summary>
        /// <param name="key">The key, from <see cref="LocalizedStrings"/>.</param>
        /// <param name="arguments">The values, already formatted and already rich-text coloured where
        /// the row's caller wants them coloured.</param>
        /// <returns>The substituted text, or the key when the table has no row for it.</returns>
        /// <remarks>
        /// The substituted values are passed in pre-rendered rather than being resolved here: the
        /// report's two call sites wrap a number in a `<color=#E7CA76>` tag, and that is the caller's
        /// business, not the translator's. Note that this mirrors the legacy's behaviour exactly -
        /// the legacy's own `DistanceLabel` and `RangeLabel` rows were formatted this way.
        /// </remarks>
        public static string Text(string key, params object[] arguments)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string text = arguments == null || arguments.Length == 0
                ? LocalizationManager.GetTranslation(key)
                : LocalizationManager.GetTranslation(key, arguments);

            return string.IsNullOrEmpty(text) ? key : text;
        }
    }
}
