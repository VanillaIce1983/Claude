using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Forming tools (A4 (2) §5, P10, P11, blech-plan S9). The tool part is not mirrored: it must be mirror-symmetric
    /// (S_α(G) = G about a plane through its punch axis at angle α in tool coordinates), which the tools of the design
    /// library are. Then R ∘ I ∘ S_α = I′ ∘ Rz(θ′) with θ′ = β − θ − 2α (<see cref="MirroredRotation"/>): θ the original's
    /// rotation, β the angle from the x-axis of the copy's placement sketch to R(x of the original's). The flip stays,
    /// outward normals stay outward. θ and α are not readable before 2025, so the copy's rotation is measured: the copy
    /// is first placed at angle 0 (the probe), the mirrored points on the original's instances (<see cref="PickSamples"/>)
    /// rotated back about each instance's insertion axis must lie on the probe's faces (<see cref="Register3D"/>, first
    /// guesses from the orientation sketches, <see cref="Register2D"/>), and the copy is placed again at the angle found
    /// (<see cref="FinalAngle"/>, the rotation sense of the call measured once per run) and checked once more. A tool
    /// that fits no rotation is another file, another configuration or an asymmetric tool (<see cref="NoFit"/>). Which
    /// side the instances stand on decides the flip (<see cref="ProtrusionSide"/>).
    /// </summary>
    public static class FormToolPlacement
    {
        /// <summary>swFeatureNameID_e.swFmFormToolInstance.</summary>
        public const int FormToolInstanceId = 73;

        /// <summary>How far a mirrored sample may lie from the copy's faces (10 µm, as the face samples).</summary>
        public const double CheckTolerance = 1e-5;

        /// <summary>The coarse grid of the rotation search: 0.5° (A4 §2).</summary>
        public const double GridStep = Math.PI / 360;

        /// <summary>Samples per tool (A4 (2) §5), more only to give every instance one.</summary>
        public const int MaxSamples = 24;

        /// <summary>At most this many samples, one per instance, for a tool with many instances.</summary>
        public const int MaxInstanceSamples = 64;

        /// <summary>A sample closer to its insertion axis than this (0.1 mm) carries no rotation.</summary>
        public const double MinAxisDistance = 1e-4;

        /// <summary>… nor one closer than this share of the instance's largest distance from its axis.</summary>
        public const double AxisShare = 0.1;

        /// <summary>At most this many configurations of the tool file are tried.</summary>
        public const int MaxConfigurations = 8;

        /// <summary>An angle in (−π, π].</summary>
        public static double Normalize(double angle)
        {
            double a = Math.IEEERemainder(angle, 2 * Math.PI);
            return a <= -Math.PI ? a + 2 * Math.PI : a;
        }

        /// <summary>The copy's rotation θ′ = β − θ − 2α (A4 §2; checks.py #1).</summary>
        public static double MirroredRotation(double theta, double alpha, double beta) => Normalize(beta - theta - 2 * alpha);

        /// <summary>A point rotated about a line by an angle (right hand about its direction).</summary>
        public static Vec3 Rotate(Vec3 p, Line3 axis, double angle)
        {
            Vec3 k = axis.Direction;
            Vec3 v = p - axis.Point;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return axis.Point + v * c + k.Cross(v) * s + k * (k.Dot(v) * (1 - c));
        }

        /// <summary>The instance whose insertion point is nearest; −1 without instances.</summary>
        public static int NearestInstance(Vec3 p, IReadOnlyList<Vec3> centres)
        {
            int best = -1;
            double d = double.PositiveInfinity;
            for (int i = 0; i < centres.Count; i++)
            {
                double di = centres[i].DistanceTo(p);
                if (di < d)
                {
                    d = di;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// The samples of a tool's shape: each point goes to the instance with the nearest insertion point; points near
        /// the insertion axis are dropped (<see cref="MinAxisDistance"/>, <see cref="AxisShare"/>) – they carry no
        /// rotation; per instance the farthest first, spread over the instance (each further one at least that share of
        /// its largest distance away from the ones taken), the rest after them; taken in turn over the instances, so
        /// every instance gets one before any gets two. At most <paramref name="max"/>, by default
        /// <see cref="MaxSamples"/> or one per instance up to <see cref="MaxInstanceSamples"/>.
        /// </summary>
        public static List<(Vec3 point, int instance)> PickSamples(IEnumerable<Vec3> points, IReadOnlyList<Vec3> centres, Vec3 normal, int? max = null)
        {
            var result = new List<(Vec3, int)>();
            if (centres.Count == 0) return result;
            int cap = max ?? Math.Max(MaxSamples, Math.Min(MaxInstanceSamples, centres.Count));
            var distinct = new List<Vec3>();
            foreach (Vec3 p in points)
                if (!distinct.Any(q => q.AlmostEquals(p)))
                    distinct.Add(p);
            List<(Vec3 p, int instance)> assigned = distinct.Select(p => (p, NearestInstance(p, centres))).ToList();
            var queues = new List<Queue<Vec3>>();
            for (int i = 0; i < centres.Count; i++)
            {
                var axis = new Line3(centres[i], normal);
                List<(Vec3 p, double r)> own = assigned.Where(x => x.instance == i).Select(x => (x.p, axis.DistanceTo(x.p))).ToList();
                double rmax = own.Count == 0 ? 0 : own.Max(x => x.r);
                double near = Math.Max(MinAxisDistance, AxisShare * rmax);
                List<Vec3> kept = own.Where(x => x.r >= near).OrderByDescending(x => x.r).Select(x => x.p).ToList();
                var spread = new List<Vec3>();
                var rest = new List<Vec3>();
                foreach (Vec3 p in kept)
                {
                    if (spread.All(q => q.DistanceTo(p) >= near)) spread.Add(p);
                    else rest.Add(p);
                }
                queues.Add(new Queue<Vec3>(spread.Concat(rest)));
            }
            while (result.Count < cap && queues.Any(q => q.Count > 0))
                for (int i = 0; i < queues.Count && result.Count < cap; i++)
                    if (queues[i].Count > 0)
                        result.Add((queues[i].Dequeue(), i));
            return result;
        }

        /// <summary>
        /// Which side of its placement face a tool's instances stand on: +1 out of the face (along its outward normal),
        /// −1 through the sheet beyond its back face (thickness <paramref name="thickness"/>, 0 if unknown), 0 if neither
        /// reaches beyond the sheet or both alike. The sheet itself – between the face and its back – says nothing;
        /// what reaches farthest beyond it decides (a dimple pushed out has its inner face partly inside the sheet).
        /// </summary>
        public static int ProtrusionSide(IEnumerable<Vec3> points, Plane3 face, double thickness, double tol = CheckTolerance)
        {
            double up = 0, down = 0;
            foreach (Vec3 p in points)
            {
                double s = face.SignedDistance(p);
                up = Math.Max(up, s);
                down = Math.Max(down, -s - thickness);
            }
            if (up <= tol && down <= tol) return 0;
            if (up > down + tol) return 1;
            return down > up + tol ? -1 : 0;
        }

        /// <summary>
        /// The angles ψ about <paramref name="axis"/> that turn <paramref name="actual"/> (the points of the copy's
        /// orientation sketch, model coordinates) onto <paramref name="wanted"/> (the mirrored points of the original's)
        /// as point sets, smallest |ψ| first; none if the sets differ in size or no rotation fits (an asymmetric outline
        /// mirrored is no rotation of itself). First guesses for <see cref="Register3D"/> only (A4 P10).
        /// </summary>
        public static List<double> Register2D(IReadOnlyList<Vec3> wanted, IReadOnlyList<Vec3> actual, Line3 axis, double tol = CheckTolerance)
        {
            var result = new List<double>();
            if (wanted.Count == 0 || wanted.Count != actual.Count) return result;
            Vec3 k = axis.Direction;
            double Axial(Vec3 p) => (p - axis.Point).Dot(k);
            Vec3 Radial(Vec3 p)
            {
                Vec3 v = p - axis.Point;
                return v - k * v.Dot(k);
            }
            Vec3 reference = actual.OrderByDescending(p => Radial(p).Length).First();
            Vec3 ra = Radial(reference);
            IEnumerable<double> candidates = ra.Length <= tol
                ? new[] { 0.0 }
                : wanted.Where(b => Math.Abs(Radial(b).Length - ra.Length) <= tol && Math.Abs(Axial(b) - Axial(reference)) <= tol)
                    .Select(b => Math.Atan2(k.Dot(ra.Cross(Radial(b))), ra.Dot(Radial(b))));
            foreach (double psi in candidates.Select(Normalize))
            {
                if (result.Any(x => Math.Abs(Normalize(x - psi)) <= 1e-9)) continue;
                if (SameSets(actual.Select(p => Rotate(p, axis, psi)).ToList(), wanted, tol)) result.Add(psi);
            }
            return result.OrderBy(Math.Abs).ToList();
        }

        /// <summary>Every point of <paramref name="a"/> has its own point of <paramref name="b"/> within the tolerance (same count).</summary>
        private static bool SameSets(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b, double tol)
        {
            if (a.Count != b.Count) return false;
            var used = new bool[b.Count];
            foreach (Vec3 p in a)
            {
                int match = -1;
                for (int j = 0; j < b.Count; j++)
                    if (!used[j] && b[j].DistanceTo(p) <= tol && (match < 0 || b[j].DistanceTo(p) < b[match].DistanceTo(p)))
                        match = j;
                if (match < 0) return false;
                used[match] = true;
            }
            return true;
        }

        /// <summary>
        /// The rotation ψ about each sample's insertion axis that carries the copy's instances onto the mirrored original's:
        /// every sample (mirrored, on the original's faces) turned back by −ψ about its axis lies on the copy's faces
        /// (<paramref name="distance"/>, by the kernel) within <paramref name="tol"/>. The guesses are refined first; then a
        /// grid of <see cref="GridStep"/>, smallest |ψ| first: a grid angle is a candidate if the samples farthest from
        /// their axes, then all of them, lie within half a grid step of arc of the faces (where the true angle would put
        /// them), and is refined to the minimum of the largest distance. The first that fits wins – for a tool with a
        /// rotational symmetry every fitting ψ gives the same part. Null if none fits (another tool or configuration, or a
        /// tool that is not mirror-symmetric, A4 P11).
        /// </summary>
        public static double? Register3D(IReadOnlyList<Vec3> samples, IReadOnlyList<Line3> axes, Func<Vec3, double> distance,
            IEnumerable<double>? guesses = null, double tol = CheckTolerance)
        {
            if (samples.Count == 0 || samples.Count != axes.Count) return null;
            double[] radius = samples.Select((s, j) => axes[j].DistanceTo(s)).ToArray();
            double rmax = radius.Max();
            List<int> byRadius = Enumerable.Range(0, samples.Count).OrderByDescending(j => radius[j]).ToList();
            List<int> coarse = SpreadAboutAxis(samples, axes, byRadius, 3);

            double DistanceAt(int j, double psi) => distance(Rotate(samples[j], axes[j], -psi));
            double Largest(double psi)
            {
                double m = 0;
                for (int j = 0; j < samples.Count; j++) m = Math.Max(m, DistanceAt(j, psi));
                return m;
            }
            bool Near(double psi, IEnumerable<int> which) => which.All(j => DistanceAt(j, psi) <= radius[j] * GridStep / 2 + tol);
            double? Refine(double centre)
            {
                // Golden section on the largest distance within half a step either side … and a bit (the guess may be off by one).
                double a = centre - GridStep, b = centre + GridStep;
                const double g = 0.6180339887498949;
                double c = b - g * (b - a), d = a + g * (b - a);
                double fc = Largest(c), fd = Largest(d);
                double bestPsi = centre, best = Largest(centre);
                for (int i = 0; i < 60 && (b - a) * Math.Max(rmax, tol) > tol / 10; i++)
                {
                    if (fc < best)
                    {
                        best = fc;
                        bestPsi = c;
                    }
                    if (fd < best)
                    {
                        best = fd;
                        bestPsi = d;
                    }
                    if (fc <= fd)
                    {
                        b = d;
                        d = c;
                        fd = fc;
                        c = b - g * (b - a);
                        fc = Largest(c);
                    }
                    else
                    {
                        a = c;
                        c = d;
                        fc = fd;
                        d = a + g * (b - a);
                        fd = Largest(d);
                    }
                }
                if (fc < best)
                {
                    best = fc;
                    bestPsi = c;
                }
                if (fd < best)
                {
                    best = fd;
                    bestPsi = d;
                }
                return best <= tol ? Normalize(bestPsi) : (double?)null;
            }

            foreach (double guess in guesses ?? Enumerable.Empty<double>())
                if (Refine(Normalize(guess)) is double found)
                    return found;
            int steps = (int)Math.Round(Math.PI / GridStep);
            for (int m = 0; m <= steps; m++)
                foreach (int sign in m == 0 ? new[] { 0 } : m == steps ? new[] { 1 } : new[] { 1, -1 })
                {
                    double psi = sign * m * GridStep;
                    if (!Near(psi, coarse) || !Near(psi, byRadius)) continue;
                    if (Refine(psi) is double found) return found;
                }
            return null;
        }

        /// <summary>Up to <paramref name="count"/> samples far from their axes and apart in angle about them (30° at least, if there are such).</summary>
        private static List<int> SpreadAboutAxis(IReadOnlyList<Vec3> samples, IReadOnlyList<Line3> axes, List<int> byRadius, int count)
        {
            Vec3 k = axes[0].Direction;
            Vec3 u = k.AnyPerpendicular();
            Vec3 v = k.Cross(u);
            double AngleOf(int j)
            {
                Vec3 d = samples[j] - axes[j].Point;
                return Math.Atan2(d.Dot(v), d.Dot(u));
            }
            var picked = new List<int>();
            foreach (int j in byRadius)
            {
                if (picked.Count >= count) break;
                if (picked.All(i => Math.Abs(Normalize(AngleOf(i) - AngleOf(j))) >= Math.PI / 6)) picked.Add(j);
            }
            foreach (int j in byRadius)
            {
                if (picked.Count >= count) break;
                if (!picked.Contains(j)) picked.Add(j);
            }
            return picked;
        }

        /// <summary>Every sample lies on the faces (the final check: the copy placed at the angle found, ψ = 0).</summary>
        public static bool Fits(IEnumerable<Vec3> samples, Func<Vec3, double> distance, double tol = CheckTolerance) =>
            samples.All(s => distance(s) <= tol);

        /// <summary>The largest distance of the samples from the faces (for the report when the final check fails).</summary>
        public static double Deviation(IEnumerable<Vec3> samples, Func<Vec3, double> distance) =>
            samples.Select(distance).DefaultIfEmpty(0).Max();

        /// <summary>The angle the copy gets: the probe's turned by ψ in the call's rotation sense (+1: right hand about the outward normal).</summary>
        public static double FinalAngle(double probe, double psi, int sense) => Normalize(probe + sense * psi);

        /// <summary>The rotation senses to try: the one found earlier in the run first (B68), otherwise +1, then −1.</summary>
        public static IReadOnlyList<int> Senses(int? known) => known == -1 ? new[] { -1, 1 } : new[] { 1, -1 };

        /// <summary>
        /// The configurations of the tool file to try (A4 P11): the original's, if readable (2025 SP2 and later), then the
        /// default (""), then the file's others in their order – at most <see cref="MaxConfigurations"/>. The place check
        /// decides; before 2025 the tool's configuration is not readable (ListExternalFileReferences2 names the referencing
        /// configuration, not the tool's).
        /// </summary>
        public static List<string> ConfigurationCandidates(string? recorded, IEnumerable<string?>? names)
        {
            var list = new List<string>();
            if (recorded != null) list.Add(recorded);
            if (!list.Contains("")) list.Add("");
            foreach (string? n in names ?? Enumerable.Empty<string?>())
                if (n != null && !list.Contains(n))
                    list.Add(n);
            return list.Take(MaxConfigurations).ToList();
        }

        /// <summary>The tool's name a feature carries: its name without trailing digits („louver1“ → „louver“); null if nothing is left.</summary>
        public static string? ToolName(string featureName)
        {
            string name = featureName.TrimEnd().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimEnd();
            return name.Length == 0 ? null : name;
        }

        /// <summary>The file names the design library is searched for: the tool's name as .sldftp, then as .sldprt.</summary>
        public static IReadOnlyList<string> LibraryFileNames(string featureName) =>
            ToolName(featureName) is string name ? new[] { name + ".sldftp", name + ".sldprt" } : Array.Empty<string>();

        /// <summary>The file name of a path, Windows or not (Path would not split „C:\…“ off Windows).</summary>
        public static string FileName(string path)
        {
            int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        /// <summary>The file name without its extension.</summary>
        public static string ToolOf(string path)
        {
            string file = FileName(path);
            int dot = file.LastIndexOf('.');
            return dot <= 0 ? file : file.Substring(0, dot);
        }

        /// <summary>
        /// The tool among a feature's external references: a forming tool (.sldftp) named like the feature, any forming tool,
        /// a part (.sldprt, older tools) named like it, any part; null if none.
        /// </summary>
        public static string? PickToolPath(IEnumerable<string?> paths, string featureName)
        {
            List<string> files = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string? name = ToolName(featureName);
            bool Ext(string p, string e) => p.EndsWith(e, StringComparison.OrdinalIgnoreCase);
            bool Named(string p) => name != null && string.Equals(ToolOf(p), name, StringComparison.OrdinalIgnoreCase);
            return files.FirstOrDefault(p => Ext(p, ".sldftp") && Named(p)) ?? files.FirstOrDefault(p => Ext(p, ".sldftp")) ??
                   files.FirstOrDefault(p => Ext(p, ".sldprt") && Named(p)) ?? files.FirstOrDefault(p => Ext(p, ".sldprt"));
        }

        // ---- texts

        public const string NoPositionSketch = "Positionsskizze des Umformwerkzeugs nicht gefunden.";
        public const string NoPoints = "Positionsskizze des Umformwerkzeugs ohne Punkte.";
        public const string PositionSketch3D = "Umformwerkzeug mit 3D-Positionsskizze wird nicht unterstützt.";
        public const string PlacementUnreadable = "Platzierungsfläche des Umformwerkzeugs nicht lesbar (oder nicht eben).";

        /// <summary>Capture: no path from the references, the data or the design library (a fallback trigger from S7a).</summary>
        public const string ToolNotFound = "Werkzeugdatei des Umformwerkzeugs nicht gefunden (weder als Bezug des Features noch in der Konstruktionsbibliothek).";

        /// <summary>Capture: no points on its faces – side and rotation of the copy could not be checked.</summary>
        public const string ShapeUnreadable = "Form des Umformwerkzeugs im Original nicht lesbar – Seite und Drehung der Kopie ließen sich nicht prüfen.";

        public const string FaceNotFound = "Platzierungsfläche des Umformwerkzeugs im Ziel nicht gefunden (eine Ersatzebene genügt nicht).";
        public const string FaceNotOwn = "Platzierungsfläche des Umformwerkzeugs liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";
        public const string NotCreated = "SOLIDWORKS hat das Umformwerkzeug nicht eingefügt.";

        /// <summary>No rotation fits with any configuration (A4 P11).</summary>
        public const string NoFit = "Werkzeug passt nicht (andere Datei oder Konfiguration, oder unsymmetrisches Werkzeug).";

        /// <summary>The tool file has gone between capture and build.</summary>
        public static string ToolMissing(string path) => $"Werkzeugdatei „{path}“ nicht vorhanden.";

        /// <summary>Info: the copy is linked to the tool file as the original is.</summary>
        public static string Linked(string path) => $"Mit der Werkzeugdatei „{path}“ verknüpft wie das Original (externer Bezug).";

        /// <summary>Info: the file was found in the design library by the feature's name, not read from the original.</summary>
        public static string FoundInLibrary(string path) =>
            $"Werkzeugdatei über die Konstruktionsbibliothek gefunden („{path}“); das Original nennt sie nicht als Bezug. Die Lageprüfung hat die Form bestätigt.";

        /// <summary>Capture note: the referenced file is gone, one of the same name was found in the design library.</summary>
        public static string Moved(string referenced, string found) =>
            $"Die Werkzeugdatei „{referenced}“ des Originals ist nicht vorhanden; verwendet wird „{found}“ aus der Konstruktionsbibliothek.";

        /// <summary>Info: the flat-pattern display of the original is not readable before 2025.</summary>
        public const string DisplayDefaults =
            "Anzeige in der Abwicklung (Stempel, Profil, Mittelpunkt): Einstellung des Dokuments – die eigene des Originals ist vor SOLIDWORKS 2025 nicht lesbar.";

        /// <summary>Info: the selected sketch did not place the tool (B67); it was placed on the face and its sketch written afterwards.</summary>
        public const string SketchWrittenAfter =
            "Mit der gewählten Positionsskizze ließ sich das Werkzeug nicht einfügen; es wurde auf der Fläche abgesetzt und seine Positionsskizze nachträglich geschrieben.";

        /// <summary>Info: a configuration other than the default fitted.</summary>
        public static string Configuration(string name) => $"Konfiguration „{name}“ des Werkzeugs (über die Lage bestimmt).";

        /// <summary>The copy's position sketch holds another number of points than the original's.</summary>
        public static string CountOff(int got, int want) => $"Die Positionsskizze der Kopie hat {got} statt {want} Punkte.";

        /// <summary>The final check: a sample lies too far from the copy's faces.</summary>
        public static string Off(double deviation, IFormatProvider? culture = null) =>
            $"Form weicht um {SheetMetalCompare.Mm(deviation, culture)} von der gespiegelten ab.";

        /// <summary>One configuration and flip that was tried and did not fit.</summary>
        public static string Tried(string configuration, bool flip, string why) =>
            $"{(configuration.Length == 0 ? "Standardkonfiguration" : $"Konfiguration „{configuration}“")}, {(flip ? "umgekehrt" : "nicht umgekehrt")}: {why}";

        public const string NotInserted = "nicht eingefügt.";
        public const string OtherSide = "Form auf der anderen Seite.";
        public const string NoRotation = "passt in keiner Drehung.";

        /// <summary>The copy turned by the angle found did not fit (the rotation sense, B68).</summary>
        public static string Turned(double angle, double deviation, IFormatProvider? culture = null) =>
            $"um {SheetMetalCompare.Deg(angle, culture)} gedreht: Form weicht um {SheetMetalCompare.Mm(deviation, culture)} ab.";

        /// <summary>Info: what the local check confirmed.</summary>
        public static string Checked(int samples, int instances) =>
            $"Seite und Drehung geprüft ({samples} Punkte der Form an {instances} {(instances == 1 ? "Stelle" : "Stellen")}).";

        /// <summary>Info: the copy needed the other flip to stand where the original does.</summary>
        public const string Flipped = "Mit umgekehrter Richtung eingefügt: so steht die Form auf derselben Seite wie im Original.";

        public static string Describe(FormToolRecord r, double angle, IFormatProvider? culture = null)
        {
            int n = r.Points.Count;
            return $"Umformwerkzeug „{ToolOf(r.ToolPath)}“ an {n} {(n == 1 ? "Stelle" : "Stellen")}, Drehung {SheetMetalCompare.Deg(angle, culture)} (gemessen)";
        }
    }
}
