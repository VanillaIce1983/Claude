using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Instances of a pattern as placements seed → instance.
    ///
    /// Original instance i: x ↦ Tᵢ(x). The mirrored part contains R(Tᵢ(x)), its seed is R(x),
    /// so the rebuilt pattern must place its instance at  Tᵢ' = R ∘ Tᵢ ∘ R  (R is its own
    /// inverse). A translation by d becomes a translation by R(d); a rotation by φ about the
    /// axis a becomes a rotation by −φ about R(a) – the sense of rotation flips, which is why
    /// the direction flag of a circular pattern cannot simply be copied.
    ///
    /// Patterns are compared as sets: the numbering of the instances (especially with two
    /// directions) is not documented, and with equal spacing over 360° both senses of rotation
    /// give the same set and are therefore both correct.
    /// </summary>
    public static class PatternInstances
    {
        public static Affine Mirror(Affine t, Reflection r)
        {
            Affine m = r.ToAffine();
            return m.Compose(t).Compose(m);
        }

        public static List<Affine> Mirror(IEnumerable<Affine> ts, Reflection r)
        {
            var result = new List<Affine>();
            foreach (Affine t in ts) result.Add(Mirror(t, r));
            return result;
        }

        /// <summary>
        /// Placements of a sketch driven pattern: the translations that move the reference
        /// point onto each sketch point. Mirrored (<see cref="Mirror(Affine, Reflection)"/>),
        /// they are the translations from the mirrored reference point to the mirrored points.
        /// </summary>
        public static List<Affine> Translations(Vec3 reference, IEnumerable<Vec3> points)
        {
            var result = new List<Affine>();
            foreach (Vec3 p in points) result.Add(new Affine(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, p - reference));
            return result;
        }

        /// <summary>
        /// Number of wanted placements that have no counterpart among the actual ones, each
        /// actual placement used at most once. Different counts always leave something unmatched.
        /// </summary>
        public static int Unmatched(IReadOnlyList<Affine> wanted, IReadOnlyList<Affine> actual, double lengthTol = 1e-6, double angleTol = 1e-6)
        {
            var used = new bool[actual.Count];
            int missing = 0;
            foreach (Affine w in wanted)
            {
                int hit = -1;
                for (int i = 0; i < actual.Count && hit < 0; i++)
                    if (!used[i] && w.AlmostEquals(actual[i], lengthTol, angleTol)) hit = i;
                if (hit < 0) missing++;
                else used[hit] = true;
            }
            int extra = 0;
            foreach (bool u in used)
                if (!u) extra++;
            return missing > extra ? missing : extra;
        }
    }
}
