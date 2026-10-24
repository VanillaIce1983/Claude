using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Build;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.AssemblyContext
{
    /// <summary>
    /// Assembly context: a component is mirrored about a plane of the assembly.
    ///
    /// The part itself is rebuilt as an opposite-hand part about one of its own standard
    /// planes (whichever is closest to the assembly plane, seen from the part); the
    /// placement T' = R_asm ∘ T ∘ R_part puts it exactly where the mirror image belongs
    /// (see ComponentPlacement). The new part is saved next to the original and inserted as
    /// a component with that transform. It has no external references and no mates.
    /// </summary>
    internal sealed class AssemblyMirror
    {
        private readonly SldWorks _sw;
        private readonly SwMath _math;

        public AssemblyMirror(SldWorks sw)
        {
            _sw = sw;
            _math = new SwMath(sw);
        }

        public MirrorJobResult Run(AssemblyDoc asm, Component2 component, Plane3 assemblyPlane, MirrorOptions options)
        {
            var asmDoc = (ModelDoc2)asm;
            if (component.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed)
                throw new InvalidOperationException($"Komponente \"{component.Name2}\" ist unterdrückt.");
            if (component.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentFullyResolved)
                component.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);

            if (!(component.GetModelDoc2() is PartDoc) || !(component.GetModelDoc2() is ModelDoc2 part))
                throw new InvalidOperationException("Nur Teile können gespiegelt werden, keine Unterbaugruppen.");
            string partPath = component.GetPathName();
            if (component.IsVirtual)
                throw new InvalidOperationException("Virtuelle Komponenten bitte zuerst extern speichern.");

            Affine placement = _math.ToAffine(component.Transform2);
            Plane3 partPlane = ComponentPlacement.ChoosePartMirrorPlane(placement, assemblyPlane);

            // Rollback and AccessSelections need the part as a document of its own.
            int err = 0, warn = 0;
            bool wasVisible = part.Visible;
            ModelDoc2 opened = _sw.OpenDoc6(partPath, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn) ?? part;

            options.Target = MirrorTarget.NewPart;
            var request = new MirrorRequest(opened, partPlane, options);
            MirrorJobResult result = new MirrorJob(_sw).Run(request);
            ModelDoc2? target = result.Target;
            if (target == null) return result;

            string newPath = UniquePath(partPath, options.NameSuffix);
            if (!target.Extension.SaveAs(newPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref err, ref warn))
            {
                result.Report.General.Add($"Neues Teil konnte nicht gespeichert werden (Fehler {err}); es bleibt offen und wird nicht eingefügt.");
                return result;
            }
            result.Report.General.Add($"Gespeichert: {newPath}");
            result.Report.General.Add($"Teil gespiegelt an seiner Ebene {partPlane}, Platzierung gleicht das aus.");

            if (!wasVisible) _sw.CloseDoc(opened.GetTitle());
            _sw.ActivateDoc3(asmDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);

            if (options.InsertIntoAssembly)
            {
                Component2 inserted = asm.AddComponent5(newPath, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", false, "", 0, 0, 0);
                if (inserted == null)
                {
                    result.Report.General.Add("Komponente konnte nicht in die Baugruppe eingefügt werden.");
                }
                else
                {
                    Affine mirrored = ComponentPlacement.MirroredPlacement(placement, new Reflection(assemblyPlane), new Reflection(partPlane));
                    inserted.Transform2 = _math.ToMathTransform(mirrored);
                    asmDoc.EditRebuild3();
                    result.Report.General.Add($"Eingefügt als \"{inserted.Name2}\" (frei beweglich, ohne Verknüpfungen).");
                }
            }
            _sw.CloseDoc(target.GetTitle());
            return result;
        }

        /// <summary>"Halter.SLDPRT" → "Halter_gespiegelt.SLDPRT", with a number if that exists.</summary>
        private static string UniquePath(string original, string suffix)
        {
            string dir = Path.GetDirectoryName(original) ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string name = Path.GetFileNameWithoutExtension(original) + suffix;
            string path = Path.Combine(dir, name + ".SLDPRT");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}_{i}.SLDPRT");
            return path;
        }
    }
}
