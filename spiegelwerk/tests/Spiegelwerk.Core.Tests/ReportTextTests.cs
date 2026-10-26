using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Report texts: plane in mm and the culture's format, unsupported types, body materials, assembly reports.</summary>
    public class ReportTextTests
    {
        private static readonly NumberFormatInfo Comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" };

        // ------------------------------------------------------------ plane

        [Fact]
        public void Plane_is_written_in_mm_with_the_given_decimal_separator()
        {
            var plane = new Plane3(new Vec3(0.0525, 0, 0), Vec3.UnitX);
            Assert.Equal("Ebene durch (52,5|0|0) mm, Normale (1|0|0)", ReportText.Plane(plane, Comma));
            Assert.Equal("Ebene durch (52.5|0|0) mm, Normale (1|0|0)", ReportText.Plane(plane, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Plane_goes_through_its_point_closest_to_the_origin()
        {
            // A face's plane comes with some point of the face.
            var plane = new Plane3(new Vec3(0.05, 0.02, -0.03), Vec3.UnitX);
            Assert.Equal("Ebene durch (50|0|0) mm, Normale (1|0|0)", ReportText.Plane(plane, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Plane_shows_no_negative_zero()
        {
            var plane = new Plane3(new Vec3(-1e-9, 0.01, 0), new Vec3(-1e-9, 0, -1));
            Assert.Equal("Ebene durch (0|0|0) mm, Normale (0|0|-1)", ReportText.Plane(plane, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Plane_normal_keeps_four_decimals()
        {
            var plane = new Plane3(Vec3.Zero, new Vec3(1, 1, 0));
            Assert.Equal("Ebene durch (0|0|0) mm, Normale (0,7071|0,7071|0)", ReportText.Plane(plane, Comma));
        }

        // ------------------------------------------------------------ unsupported types

        [Theory]
        [InlineData("MirrorPattern", "Spiegel-Feature im Original")]
        [InlineData("MirrorSolid", "Körper spiegeln im Original")]
        [InlineData("MirrorStock", "Teil spiegeln im Original")]
        public void Mirror_features_have_their_own_message(string type, string start)
        {
            string reason = UnsupportedFeatures.Reason(type);
            Assert.StartsWith(start, reason);
            Assert.Contains("Spiegel-API", reason);
            Assert.Contains("Abhilfe", reason);
        }

        [Theory]
        [InlineData("SheetMetal")]
        [InlineData("SMBaseFlange")]
        [InlineData("EdgeFlange")]
        [InlineData("SMMiteredFlange")]
        [InlineData("SketchBend")]
        [InlineData("FlattenBends")]
        [InlineData("ProcessBends")]
        [InlineData("CrossBreak")]
        [InlineData("FlatPattern")]
        [InlineData("TemplateSheetMetal")]
        public void Sheet_metal_features_get_one_clear_message(string type)
        {
            Assert.True(UnsupportedFeatures.IsSheetMetal(type));
            Assert.StartsWith("Blech wird nicht unterstützt", UnsupportedFeatures.Reason(type));
        }

        [Fact]
        public void Other_types_keep_the_general_message()
        {
            Assert.False(UnsupportedFeatures.IsSheetMetal("Bending"));
            Assert.Equal("Feature-Typ Bending wird nicht unterstützt.", UnsupportedFeatures.Reason("Bending"));
            Assert.Equal("Muster: Feature-Daten nicht lesbar.", UnsupportedFeatures.Reason("FillPattern"));
        }

        // ------------------------------------------------------------ body materials

        [Fact]
        public void Bodies_without_a_material_of_their_own_need_no_note()
        {
            // A body without one may report the part's material (in any case) or nothing.
            Assert.Null(BodyMaterials.Note("AISI 304", new[] { ("Körper1", "AISI 304"), ("Körper2", "aisi 304"), ("Körper3", "") }));
            Assert.Null(BodyMaterials.Note("", new (string, string)[0]));
        }

        [Fact]
        public void Bodies_with_a_material_of_their_own_are_named()
        {
            string? note = BodyMaterials.Note("AISI 304", new[] { ("Körper1", "AISI 304"), ("Körper2", "PA6") });
            Assert.NotNull(note);
            Assert.Contains("\"Körper2\" (PA6)", note);
            Assert.DoesNotContain("Körper1", note);
            Assert.Contains("den Werkstoff des Teils (AISI 304)", note);

            Assert.Contains("keinen Werkstoff", BodyMaterials.Note("", new[] { ("Körper1", "PA6") }));
        }

        [Fact]
        public void Many_bodies_with_own_materials_are_shortened()
        {
            var bodies = Enumerable.Range(1, 8).Select(i => ($"Körper{i}", "PA6")).ToList();
            string? note = BodyMaterials.Note("", bodies);
            Assert.Contains("\"Körper5\"", note);
            Assert.DoesNotContain("\"Körper6\"", note);
            Assert.Contains("und 3 weitere", note);
        }

        // ------------------------------------------------------------ assembly report

        [Fact]
        public void Assembly_report_keeps_its_own_name_and_lists_every_saved_part()
        {
            var asm = new MirrorReport { TargetName = "Baugruppe1.SLDASM" };
            var a = new MirrorReport { TargetName = "Halter_gespiegelt.SLDPRT" };
            a.Add(new UnsupportedRecord("Skizze1", "ProfileFeature", "", false), ReportStatus.Ok, "");
            var b = new MirrorReport { TargetName = "Lasche_gespiegelt.SLDPRT" };

            asm.Append(a, "Halter-1");
            asm.Append(b, "Lasche-1");

            Assert.Equal("Baugruppe1.SLDASM", asm.TargetName);
            Assert.Contains(asm.General, g => g.StartsWith("— Halter-1 → Halter_gespiegelt.SLDPRT: 1 in Ordnung"));
            Assert.Contains(asm.General, g => g.StartsWith("— Lasche-1 → Lasche_gespiegelt.SLDPRT: "));
            Assert.Equal("Halter-1 › Skizze1", asm.Entries.Single().Feature);
            Assert.StartsWith("Spiegelwerk – Ergebnis: Baugruppe1.SLDASM", asm.ToText());
        }

        [Fact]
        public void Assembly_report_carries_over_flags_entries_and_checks()
        {
            var a = new MirrorReport { TargetName = "Halter_gespiegelt.SLDPRT" };
            a.General.Add("Spiegelebene: Rechts");
            a.Add(new UnsupportedRecord("Austragung1", "Extrusion", "", true), ReportStatus.Ok, "Blind 10 mm").Verification = "Volumen und Schwerpunkt stimmen";
            a.Add(new UnsupportedRecord("Ebene1", "RefPlane", "", false), ReportStatus.Skipped, "liegt gespiegelt auf Ebene Rechts").HasEquivalent = true;

            var b = new MirrorReport { TargetName = "Lasche_gespiegelt.SLDPRT", Aborted = true, Cancelled = true };
            var cut = new UnsupportedRecord("Schnitt1", "Cut", "", true);
            cut.Notes.Add("Bezug: Fläche 3");
            b.Add(cut, ReportStatus.Failed, "Bezug der Endbedingung im Ziel nicht gefunden.");
            b.Add(new UnsupportedRecord("Fase1", "Chamfer", "", true), ReportStatus.Warning, "Kante mehrdeutig");

            var asm = new MirrorReport { TargetName = "Baugruppe1.SLDASM" };
            asm.Append(a, "Halter-1");
            // The skipped plane keeps its equivalent: nothing blocks yet.
            Assert.True(asm.AllMirrored);
            Assert.False(asm.HasProblems);

            asm.Append(b, "Lasche-1");
            Assert.True(asm.Aborted);
            Assert.True(asm.Cancelled);
            Assert.False(asm.AllMirrored);
            Assert.Equal(new[] { "Lasche-1 › Schnitt1", "Lasche-1 › Fase1" }, asm.Blocking.Select(e => e.Feature));
            Assert.Equal(new[] { "Lasche-1 › Schnitt1" }, asm.NotMirrored.Select(e => e.Feature));
            Assert.Equal("1 in Ordnung, 1 mit Hinweis, 1 fehlgeschlagen, 1 übersprungen – abgebrochen", asm.Summary);

            string[] lines = asm.ToText().Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.None);
            Assert.Equal(new[]
            {
                "Spiegelwerk – Ergebnis: Baugruppe1.SLDASM",
                "1 in Ordnung, 1 mit Hinweis, 1 fehlgeschlagen, 1 übersprungen – abgebrochen",
                "  — Halter-1 → Halter_gespiegelt.SLDPRT: 1 in Ordnung, 0 mit Hinweis, 0 fehlgeschlagen, 1 übersprungen",
                "     Spiegelebene: Rechts",
                "  — Lasche-1 → Lasche_gespiegelt.SLDPRT: 0 in Ordnung, 1 mit Hinweis, 1 fehlgeschlagen, 0 übersprungen – abgebrochen",
                "",
                "[OK] Halter-1 › Austragung1 (Extrusion): Blind 10 mm",
                "    Prüfung: Volumen und Schwerpunkt stimmen",
                "[Übersprungen] Halter-1 › Ebene1 (RefPlane): liegt gespiegelt auf Ebene Rechts",
                "[Fehler] Lasche-1 › Schnitt1 (Cut): Bezug der Endbedingung im Ziel nicht gefunden.",
                "    Bezug: Fläche 3",
                "[Hinweis] Lasche-1 › Fase1 (Chamfer): Kante mehrdeutig",
                "",
            }, lines);
        }
    }
}
