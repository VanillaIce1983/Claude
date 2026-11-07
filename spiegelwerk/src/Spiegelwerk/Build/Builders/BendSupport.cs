using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>The direction flags of a sketched bend or jog in one variant of the search.</summary>
    internal sealed class BendState
    {
        public BendState(bool reverse, bool reverseOffset, string description)
        {
            Reverse = reverse;
            ReverseOffset = reverseOffset;
            Description = description;
        }

        /// <summary>ReverseDirection.</summary>
        public bool Reverse { get; }

        /// <summary>ReverseOffset (jog only).</summary>
        public bool ReverseOffset { get; }

        /// <summary>For the report, e.g. „Richtung nach Regel“.</summary>
        public string Description { get; }

        public override string ToString() => Description;
    }

    /// <summary>A variant of a bend or jog the witnesses measured: which pick point, which flags, what it missed.</summary>
    internal sealed class BendOutcome<TState>
    {
        public BendOutcome(int pick, TState state, int missed, bool error)
        {
            Pick = pick;
            State = state;
            Missed = missed;
            Error = error;
        }

        public int Pick { get; }
        public TState State { get; }
        public int Missed { get; }
        public bool Error { get; }
        public bool Passes => !Error && Missed == 0;
    }

    /// <summary>
    /// What sketched bend and jog share (blech-plan S4, K8/K10): the fixed face before anything is made
    /// (separate body: on a mirrored body), the pick points one per side, and the search – every pick
    /// point with every direction state, measured by the witnesses, always on; the first that misses
    /// none is kept, otherwise the best is made again (A3 P1/P2, A2 P6).
    /// </summary>
    internal static class BendSupport
    {
        /// <summary>At most this many pick points (regions) are tried.</summary>
        public const int MaxPicks = 4;

        /// <summary>
        /// The mirrored fixed face in the target: found, planar and – separate body – on a mirrored body
        /// (sheet metal has no feature scope: the bend would act on the original). Null and why otherwise;
        /// no plane stands in for it.
        /// </summary>
        public static Face2? FixedFace(BuildContext ctx, FaceSignature original, string what, List<string> notes, out FaceSignature? actual, out string? why)
        {
            actual = null;
            why = null;
            Face2? face = ctx.Topology.FindFace(original.Mirror(ctx.Mirror), notes);
            if (face == null)
            {
                why = $"Feste Fläche {what} im Ziel nicht gefunden.";
                return null;
            }
            if (ctx.SeparateBody && !(FlangeSupport.BodyOf(face) is Body2 body && ctx.IsOwnBody(body)))
            {
                why = $"Feste Fläche {what} liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";
                return null;
            }
            actual = TopologyCapture.Face(face);
            if (actual.Kind != SurfaceKind.Plane)
            {
                why = $"Feste Fläche {what} ist im Ziel nicht eben.";
                return null;
            }
            return face;
        }

        /// <summary>How far the face reaches from its sample point: half the diagonal of its box.</summary>
        public static double Reach(Face2 face)
        {
            try
            {
                if ((object)face.GetBox() is double[] box && box.Length >= 6)
                    return new Vec3(box[0], box[1], box[2]).DistanceTo(new Vec3(box[3], box[4], box[5])) / 2;
            }
            catch (Exception)
            {
                // Without a box: a tenth of a metre, the grid is coarse then but the face test still holds.
            }
            return 0.1;
        }

        /// <summary>The point lies on the face (kernel distance, 10 µm).</summary>
        public static bool OnFace(Face2 face, Vec3 p)
        {
            try
            {
                return TopologyCapture.DistanceToFace(face, p) <= BendSide.OnFaceTolerance;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// σ of the sheet the face belongs to (K11): known only once B39 settles the convention – until then
        /// null, and the first guess takes σ of the sketch.
        /// </summary>
        public static int? SigmaSheet(BuildContext ctx, Face2 face)
        {
            if (!(FlangeSupport.BodyOf(face) is Body2 body) || !(body.GetFeatures() is object[] fs)) return null;
            var names = new HashSet<string>(fs.OfType<Feature>().Select(f => f.Name), StringComparer.Ordinal);
            return ctx.SheetMetalBodies.Values.FirstOrDefault(b => b.SheetMetal != null && names.Contains(b.SheetMetal.Name))?.SigmaSheet;
        }

        /// <summary>The feature shows a rebuild error (a warning is none).</summary>
        public static bool InError(Feature f)
        {
            try
            {
                bool warning = false;
                return f.GetErrorCode2(out warning) != 0 && !warning;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>A rebuilt sketch as it is now (looked up by name: an attempt that absorbed it and went may have left the old object stale).</summary>
        public static Feature SketchNow(BuildContext ctx, SketchMap map) => FlangeSupport.Live(ctx.Doc, map.Feature) ?? map.Feature;

        /// <summary>
        /// The search (A3 §5.1): for each pick point (at most <see cref="MaxPicks"/>) one feature is made
        /// (<paramref name="make"/>; null: none came of it), then put into each state in turn
        /// (<paramref name="apply"/>; false: the edit did not take) and measured; the first state that
        /// misses no witness and has no rebuild error is kept. Otherwise the feature goes again
        /// (everything since <paramref name="before"/>) and the next pick point follows. Returns the kept
        /// feature, or null with the best outcome (<paramref name="best"/>; null if nothing could be made).
        /// </summary>
        public static Feature? Search<TState>(BuildContext ctx, IReadOnlyList<Vec3> picks, Func<int, Feature?> make, IReadOnlyList<TState> states,
            Func<Feature, TState, bool> apply, Func<Feature, int> missed, HashSet<string> before, List<string> tried,
            out BendOutcome<TState>? best, out BendOutcome<TState>? chosen)
        {
            best = null;
            chosen = null;
            for (int i = 0; i < Math.Min(picks.Count, MaxPicks); i++)
            {
                Feature? f = make(i);
                if (f == null) continue;
                foreach (TState state in states)
                {
                    if (!apply(f, state))
                    {
                        tried.Add($"Punkt {i + 1}: {state} ließ sich nicht setzen.");
                        continue;
                    }
                    var outcome = new BendOutcome<TState>(i, state, missed(f), InError(f));
                    if (best == null || SheetMetalBendRules.CompareVariants(outcome.Missed, outcome.Error, best.Missed, best.Error) < 0) best = outcome;
                    if (outcome.Passes)
                    {
                        chosen = outcome;
                        return f;
                    }
                }
                List<string> left = FlangeSupport.RemoveMade(ctx, before);
                if (left.Count > 0)
                {
                    // What cannot be removed would distort every later attempt: stop here.
                    tried.Add(FlangeSupport.LeftBehind(left));
                    break;
                }
            }
            return null;
        }
    }
}
