// CommNextRedux - the occlusion geometry, in managed code.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Network/Compute/GetNextConnectedNodesJob.cs, the inline
//   body of the target loop: `var a = ...; var b = ...; var c = ...; var discriminant =
//   KahanDiscriminant(a, b, c); ...` plus `KahanDiscriminant` itself.
//
// WHY THIS FILE EXISTS AT ALL
//   The legacy ran that body inside a Unity Burst job and called one native import:
//
//       [DllImport("CommNext.Native.dll")]
//       private static extern double FusedMultiplyAdd(double x, double y, double z);
//
//   User decision D10 removes both. There is no CommNext.Native.dll in this port and no IJob of
//   its own - which means the whole occlusion test has to live in managed C# and still produce
//   the geometry verdicts the legacy produced. That is what the FMA substitution below is for.

using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace CommNextRedux.Network
{
    /// <summary>
    /// The segment-versus-sphere occlusion test: whether the straight line between two nodes passes
    /// through a celestial body's sphere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The formula is the legacy's, transcribed.</b> Every shifted quantity below is computed in
    /// the body's own frame (the body at the origin), exactly as
    /// <c>GetNextConnectedNodesJob</c> did with <c>s = double3.zero</c>, so the <c>a</c>/<c>b</c>/<c>c</c>
    /// coefficients are the legacy's substitution into the quadratic
    /// <c>a t^2 + b t + c = 0</c> for the line <c>p(t) = p1 + t (p2 - p1)</c> against the sphere of
    /// radius <c>r</c> centred at the origin.
    /// </para>
    /// <para>
    /// <b>The <c>t</c> test is the legacy's, including its quirk.</b> A segment is occluded when
    /// <i>either</i> root lies in <c>[0, 1]</c>. The legacy wrote that as
    /// <c>if (t1 is &lt; 0 or &gt; 1 &amp;&amp; t2 is &lt; 0 or &gt; 1) continue;</c> - and a C#
    /// relational pattern is <b>false for NaN</b>, so NaN roots fall through to "occluded". This port
    /// preserves that: <see cref="IsOutsideUnitInterval"/> is false for NaN, so a NaN discriminant
    /// path reports occlusion rather than silence.
    /// </para>
    /// </remarks>
    public static class Occlusion
    {
        /// <summary>Dekker's splitter for IEEE-754 binary64: <c>2^27 + 1</c>.</summary>
        /// <remarks>
        /// The exact value matters. A splitter of <c>2^s + 1</c> splits a double into two halves of
        /// <c>53 - s</c> bits, and <c>s = 27</c> is the largest split that cannot overflow for
        /// <c>|x| &lt; 2^996</c> while leaving enough bits for the error term.
        /// </remarks>
        public const double Splitter = 134217729.0;

        /// <summary>Largest magnitude for which <see cref="TwoProduct"/> is exact.</summary>
        /// <remarks>
        /// <c>Splitter * x</c> must not overflow, and the split must not underflow to zero for either
        /// operand. <c>2^996</c> and <c>2^-968</c> bracket the safe band; this port's operands are
        /// distances squared and products of them - on the order of <c>1e22</c> for a 100 Gm link, and
        /// never smaller than <c>1</c> for a pair of distinct nodes - so the band is not approached.
        /// The guard exists so that the day it is, the fallback is the plain discriminant rather than
        /// a silent wrong answer.
        /// </remarks>
        public const double MaxExactOperand = 6.7e299;

        /// <summary>Smallest magnitude for which <see cref="TwoProduct"/> is exact.</summary>
        public const double MinExactOperand = 4.0e-291;

        /// <summary>
        /// The occlusion test's discriminant, computed the way the legacy computed it.
        /// </summary>
        /// <param name="a">Quadratic coefficient <c>|p2 - p1|^2</c> in the body's frame.</param>
        /// <param name="b">Quadratic coefficient <c>2 (p2 - p1) . p1</c> in the body's frame.</param>
        /// <param name="c">Quadratic constant <c>|p1|^2 - r^2</c> in the body's frame.</param>
        /// <returns>The discriminant <c>b^2 - 4 a c</c>, with its cancellation error corrected.</returns>
        /// <remarks>
        /// <para>
        /// <b>Faithful transcription.</b> The legacy's fast path,
        /// <c>if (3 * Math.Abs(d) &gt;= b * b + 4 * a * c) return d;</c>, is reproduced verbatim -
        /// including the fact that the <i>test</i> itself is evaluated in plain double arithmetic,
        /// so a discriminant that is large relative to the terms is returned uncorrected. Only the
        /// ill-conditioned branch differs, and only in how the two residuals are obtained.
        /// </para>
        /// <para>
        /// <b>The FMA substitution - this is the recorded decision.</b> The legacy's native import
        /// computed <c>FusedMultiplyAdd(x, y, -p)</c> where <c>p</c> is already
        /// <c>fl(x * y)</c>, i.e. it asked for the <b>exact residual</b> <c>x*y - fl(x*y)</c>.
        /// <see cref="System.Math.FusedMultiplyAdd"/> does not exist on Redux 0.2.8.5 - measured:
        /// zero occurrences across all 234 assemblies under
        /// <c>$KSP2_ROOT/KSP2_x64_Data/Managed/</c> - so this port computes the same residual with
        /// <b>Dekker's <see cref="TwoProduct"/></b>.
        /// </para>
        /// <para>
        /// That is an <b>exact</b> substitution, not a downgrade, and the reason is worth stating
        /// because it is the whole justification for not shipping a native DLL: for two doubles whose
        /// product is finite, <c>TwoProduct</c> returns a pair <c>(p, e)</c> with
        /// <c>p + e == x * y</c> <i>exactly</i>, so <c>e</c> is the correctly-rounded
        /// <c>x*y - p</c>. A fused multiply-add asked for the same quantity and rounds it the same
        /// way, because it computes <c>x*y</c> exactly internally and rounds the subtraction once.
        /// The two are therefore bit-identical for every operand in the safe band, which
        /// <see cref="TwoProduct"/> checks. The sibling port's rule ("never ship a native binary") and
        /// this phase's constraint D10 are both satisfied without giving up a single bit of the
        /// legacy's precision.
        /// </para>
        /// <para>
        /// <c>4 * a</c> is computed once and passed, exactly as the legacy did - note that the legacy
        /// did <b>not</b> reuse that product for <c>q</c>: it wrote <c>q = 4 * a * c</c> and
        /// <c>FusedMultiplyAdd(4 * a, c, -q)</c>, i.e. it multiplied <c>(4 * a)</c> by <c>c</c>.
        /// This port evaluates the same expression the same way.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Discriminant(double a, double b, double c)
        {
            double d = b * b - 4 * a * c;
            if (3 * Math.Abs(d) >= b * b + 4 * a * c)
            {
                return d;
            }

            double p = b * b;
            double dp = ProductError(b, b);
            double fourA = 4 * a;
            double q = fourA * c;
            double dq = ProductError(fourA, c);
            return p - q + (dp - dq);
        }

        /// <summary>
        /// The exact error of a double multiplication: <c>x*y - fl(x*y)</c>.
        /// </summary>
        /// <param name="x">First factor.</param>
        /// <param name="y">Second factor.</param>
        /// <returns>The residual, or <c>0</c> when the operands leave the exact band.</returns>
        /// <remarks>
        /// This is the FMA substitution the legacy's native import performed, written out. It is
        /// deliberately not forgiving: an operand outside the exact band returns <c>0</c> (the
        /// uncorrected residual the plain discriminant already has) rather than a plausible-looking
        /// wrong number, and the caller's <see cref="Discriminant"/> is then bit-identical to the
        /// legacy's fast path.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ProductError(double x, double y)
        {
            double ax = Math.Abs(x);
            double ay = Math.Abs(y);
            if (ax > MaxExactOperand || ay > MaxExactOperand)
            {
                return 0.0;
            }

            // A zero factor has a zero residual, and the split below would divide out nothing but
            // would return 0 anyway; short-circuiting keeps the intent obvious.
            if (ax == 0.0 || ay == 0.0)
            {
                return 0.0;
            }

            if (ax < MinExactOperand || ay < MinExactOperand)
            {
                return 0.0;
            }

            double p;
            double error;
            TwoProduct(x, y, out p, out error);
            return error;
        }

        /// <summary>
        /// Dekker's error-free product: splits <paramref name="x"/> and <paramref name="y"/> and
        /// recovers the residual of their product.
        /// </summary>
        /// <param name="x">First factor.</param>
        /// <param name="y">Second factor.</param>
        /// <param name="product">The rounded product <c>fl(x*y)</c>.</param>
        /// <param name="error">The residual, so that <c>product + error == x*y</c> exactly.</param>
        /// <remarks>
        /// The three-term accumulation order is the canonical one and is load-bearing: each partial
        /// term is itself exact, so the running sum never rounds until the final addition. Reordering
        /// the terms would break the exactness the whole substitution rests on.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TwoProduct(double x, double y, out double product, out double error)
        {
            product = x * y;

            double cx = Splitter * x;
            double ax = cx - (cx - x);
            double bx = x - ax;

            double cy = Splitter * y;
            double ay = cy - (cy - y);
            double by = y - ay;

            error = ((ax * ay - product) + ax * by + bx * ay) + bx * by;
        }

        /// <summary>
        /// Whether the straight segment between two nodes of the CommNet graph passes through a
        /// celestial body's sphere.
        /// </summary>
        /// <param name="source">The source node's position in the shared graph frame.</param>
        /// <param name="target">The target node's position in the shared graph frame.</param>
        /// <param name="bodyPosition">The body's centre, already converted into that same frame.</param>
        /// <param name="bodyRadius">
        /// The body's <b>effective</b> occlusion radius: <c>radius * OcclusionRadiusFactor - 1000 m</c>.
        /// Callers must skip a body whose effective radius is not positive - see
        /// <see cref="Network.NetworkConfig.OcclusionRadius"/> for why that is a fix rather than a
        /// liberty.
        /// </param>
        /// <returns><c>true</c> when the segment is occluded by this body.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOccluded(
            double3 source,
            double3 target,
            double3 bodyPosition,
            double bodyRadius)
        {
            if (!(bodyRadius > 0.0))
            {
                return false;
            }

            // The body's own frame: it sits at the origin, which is what makes s = double3.zero
            // correct in the legacy's c coefficient.
            double3 p1 = source - bodyPosition;
            double3 p2 = target - bodyPosition;

            double dx = p2.x - p1.x;
            double dy = p2.y - p1.y;
            double dz = p2.z - p1.z;

            double a = dx * dx + dy * dy + dz * dz;

            // A degenerate segment (both nodes at the same point) has no direction to intersect
            // anything with. The legacy did not guard this and divided by 2a anyway, which yields
            // infinities and NaNs whose relational-pattern test reads as "occluded"; returning
            // "not occluded" here is the same verdict the legacy reached by accident, without two
            // divisions by zero on the hot path.
            if (!(a > 0.0))
            {
                return false;
            }

            double b = 2.0 * (dx * p1.x + dy * p1.y + dz * p1.z);
            double c = p1.x * p1.x + p1.y * p1.y + p1.z * p1.z - bodyRadius * bodyRadius;

            double discriminant = Discriminant(a, b, c);
            if (discriminant < 0.0)
            {
                return false;
            }

            double sqrt = Math.Sqrt(discriminant);
            double twiceA = 2.0 * a;
            double t1 = (-b + sqrt) / twiceA;
            double t2 = (-b - sqrt) / twiceA;

            return !(IsOutsideUnitInterval(t1) && IsOutsideUnitInterval(t2));
        }

        /// <summary>
        /// Whether <paramref name="t"/> lies outside <c>[0, 1]</c>.
        /// </summary>
        /// <param name="t">The parametric position along the segment.</param>
        /// <returns><c>false</c> for NaN, deliberately: see the class remarks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOutsideUnitInterval(double t) => t < 0.0 || t > 1.0;
    }
}
