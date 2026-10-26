# Spiegelwerk installer design: one MSI for desktop SOLIDWORKS and 3DEXPERIENCE SOLIDWORKS

## Summary

- **Tool and output:** one per-machine x64 MSI, built with WiX v7 (`WixToolset.Sdk/7.0.0`) on `windows-latest`.
- **COM registration:** the MSI writes the COM class and the SOLIDWORKS add-in keys itself. RegAsm never runs on the user's machine.
- **No per-variant branch:** the registration does not need a separate branch for desktop SOLIDWORKS or 3DEXPERIENCE SOLIDWORKS.
  - The add-in lives in its own folder.
  - The keys `HKLM\SOFTWARE\SolidWorks\Addins\{GUID}` and `HKCU\Software\SolidWorks\AddInsStartup\{GUID}` contain no version and no variant.
  - The interop types are embedded in Spiegelwerk.dll, so nothing depends on where SOLIDWORKS is installed.
- **Detection only produces a message.** It never blocks the install.
- **Different install paths are not a problem with this design.** The difference that matters is how the interop DLLs are resolved at run time. That is why the interop change in section 6 is the most important part of this design.

Confidence labels:
- **[H]** high, **[M]** medium, **[L]** low.
- **[Inf]** means inference, not stated by a source.
- **[Gen]** means general knowledge I did not re-check in this run.
- **L1 to L4** are local files and my own scratch measurements.
- **S1 to S49** are listed at the end.

---

## 1. Installer technology

**Decision: WiX Toolset v7.0.0 as an MSBuild SDK project, built with `dotnet build`. No other tool on the runner, no manual step.**

- **Runner:** `windows-latest` is the Windows Server 2025 / VS2026 image. It has .NET SDK 8, 9 and 10, Inno Setup 6.7.1 and legacy WiX 3.14 [H, S1]. WiX v7 is downloaded from NuGet through `<Project Sdk="WixToolset.Sdk/7.0.0">`. I checked that `WixToolset.Sdk`, `WixToolset.UI.wixext`, `WixToolset.Util.wixext` and `WixToolset.Netfx.wixext` are on nuget.org at 7.0.0 [H, checked with the nuget.org flat-container API].
- **EULA acceptance:** WiX v7 fails with error WIX7015 unless the EULA is accepted. For CI the documented way is `<AcceptEula>wix7</AcceptEula>` in the .wixproj [H, S3]. The WiX SDK 7.0.0 `tools/wix.targets` contains the AcceptEula and CheckLicenseAcceptance targets [H, read from the nupkg].
- **WiX licence (open):**
  - The source code is MS-RL.
  - The v7 binaries fall under the Open Source Maintenance Fee (OSMF) EULA v1.1. The fee applies only to users who use WiX "as part of revenue-generating activities" and whose own annual gross revenue is at least US$10,000 [H, S2].
  - A private developer below that threshold pays nothing. A company with US$10,000 or more in revenue that builds the MSI as part of its work may owe the fee [M, Inf].
  - The user has to settle this.
  - If the fee would apply and is not wanted, the fallbacks are:
    - WiX 6.0.2 under OSMF v1.0, where the fee applies only to users "that generate revenue by the Software" [H, S2/S4];
    - Inno Setup, which is free including commercial use (commercial users are only "kindly asked" to buy a licence) [H, S5].
- **Why WiX and not Inno Setup:**
  - Registry entries are declarative. Install has rollback, uninstall removes everything the MSI wrote, and MajorUpgrade removes old COM version subkeys [H, S7, S11].
  - No RegAsm at install time, so no interop assembly has to load on the target machine [M, Inf from S8].
  - An MSI can be deployed by GPO or Intune, which matters because SOLIDWORKS users often work in companies.
  - Restart Manager and FilesInUse dialogs come for free.
  - In Inno with `PrivilegesRequired=admin`, a standard user who types admin credentials runs Setup under that admin account, so all HKCU writes go to the wrong profile [H, S6].
  - **Inno fallback if needed:** keep the same keys as `[Registry]` entries with `uninsdeletekey` and no RegAsm, plus `ArchitecturesInstallIn64BitMode=x64compatible`. Do not use `[Run] RegAsm`, because it leaves `InprocServer32\<old version>` subkeys behind on every upgrade [M, Inf].
- **Not used:**
  - Heat was removed in WiX v7 [H, S4]. The WiX v3 heat.exe is x86 and cannot load the x64-only Spiegelwerk.dll [M, Inf].
  - A RegAsm custom action is ruled out: Microsoft strongly advises against self-registration [H, S7]. A deferred, non-impersonated custom action also runs as SYSTEM [H, S19].
- **Restriction:** WiX builds MSIs only on Windows. The .wixproj must not be added to `Spiegelwerk.sln`, because the Linux core job would then try to build it.

## 2. Per-machine install folder

- `C:\Program Files\Spiegelwerk\`, authored as `StandardDirectory Id="ProgramFiles64Folder"` → `Directory Id="INSTALLFOLDER" Name="Spiegelwerk"`. `ProgramFiles64Folder` is a valid StandardDirectory id [H, firegiant/docs standarddirectorytype].
- It sits outside both SOLIDWORKS trees, so 3DEXPERIENCE updates, desktop service packs and SOLIDWORKS uninstalls do not touch it [M, Inf, S20].
- Files with the recommended interop option C (section 6): `Spiegelwerk.dll` and `Spiegelwerk.Core.dll`. The .pdb files are optional.
  - The current net48 output contains no netstandard facades, so nothing else is needed [H, L4: `bin/Release/net48` holds only these 2 DLLs and 2 PDBs].
  - With fallback option B you would add the three SolidWorks.Interop DLLs.
- The folder can be overridden with `msiexec /i … INSTALLFOLDER=D:\…` (public property). The UI (WixUI_Minimal) does not offer a folder choice.
- Nothing needs to be writable in Program Files. IconFactory writes its icons to `%LOCALAPPDATA%\Spiegelwerk\icons` [H, L3].
- COM must be registered per machine. With UAC disabled, an elevated SOLIDWORKS ignores per-user COM registrations, so per-machine is the only safe choice [H, S18].

## 3. How the COM class is registered

**The MSI authors the same registry values that RegAsm /codebase would write.** RegAsm's values are known from RegistrationServices [H, S8]:

- `HKCR\CLSID\{G}` (default) = `Spiegelwerk.Addin.SpiegelwerkAddin`.
- `InprocServer32`:
  - (default) `mscoree.dll`, `ThreadingModel=Both`;
  - `Class`, `Assembly`, `RuntimeVersion=v4.0.30319`, `CodeBase=file:///…`.
- The same four values under `InprocServer32\0.10.0.0`.
- `ProgId` subkey, and `HKCR\Spiegelwerk.Addin\CLSID`.
- The empty key `Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}`.

In WiX:
- `Class Advertise="no" ForeignServer="mscoree.dll"` directly under the Component generates the CLSID, InprocServer32 and ProgId rows [H, S9].
- The remaining values are plain `RegistryKey`/`RegistryValue` entries.
- In a per-machine package, HKCR maps to `HKLM\Software\Classes`, in the 64-bit view for an x64 package [H, S12 and the registryroottype doc].

**Only SpiegelwerkAddin is registered.**
- RegAsm skips MirrorPage because it has no public parameterless constructor [H, S8; L2].
- MirrorPage needs no registration: SOLIDWORKS gets the handler object directly [Inf].

**Does the add-in's own `[ComRegisterFunction]` still matter? Not for the installer.**
- The MSI never runs it. Heat-style or hand-authored data and `RegAsm /regfile` all skip user-defined register functions [H, S50].
- Keep `Register`/`Unregister` only for the developer path, `scripts\install.cmd`.
- The installer must therefore write `HKLM\SOFTWARE\SolidWorks\Addins\{G}` itself.
- Keep Title and Description in sync between `SpiegelwerkAddin.cs` (L2) and `Package.wxs`. A CI grep that compares the two strings is enough.
- Mark `scripts\install.cmd` and `uninstall.cmd` in the README as "Entwicklung" (developer use).

**Stale registrations:** a machine that was registered earlier with `install.cmd` keeps the old RegAsm values. superdex deletes the CLSID tree before re-registering for this reason [L, S40]. The MSI therefore removes the old key before writing (`RemoveRegistryKey Action="removeOnInstall"`, S17) and the whole tree on uninstall.

## 4. Registry keys (identical for desktop and 3DEXPERIENCE SOLIDWORKS)

| Key | Values | Purpose / source |
|---|---|---|
| `HKLM\SOFTWARE\Classes\CLSID\{8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}` and subkeys (section 3) | as RegAsm would write them | COM [H, S8] |
| `HKLM\SOFTWARE\Classes\Spiegelwerk.Addin\CLSID` | `{8A3C…4F57}` | ProgId [H, S8] |
| `HKLM\SOFTWARE\SolidWorks\Addins\{8A3C…4F57}` | (default) DWORD **1**, `Title`=`Spiegelwerk`, `Description`=`Spiegelt Features als eigenständige, unabhängige Features (ohne Spiegel-Feature).` | Required: lists the add-in in Tools > Add-Ins [H, S21]. Default 1 = "loads when the user starts SOLIDWORKS" [M, S22 (search snippet only)]; SolidDNA writes only this key [M, S23] |
| `HKCU\Software\SolidWorks\AddInsStartup\{8A3C…4F57}` | (default) DWORD 1, nothing else | Optional per-user Start Up flag (section 8) [H, S21]; only the default value [L, S30] |

- **Same keys for both variants:**
  - The key paths contain no version and no variant [M, Inf].
  - OpenAxis, developed and tested on 3DEXPERIENCE R2026x, uses exactly these keys [M, S20].
  - I found no separate root, trust list or whitelist for 3DEXPERIENCE [M, Inf, S43].
  - Dassault says SOLIDWORKS connected to the 3DEXPERIENCE platform "shares the same API … and supports custom macros & add-ins" [M, S51].
- **Not written for now:** `DesktopAddinType=2` and `3DExperienceAddinType=2`. These are undocumented and come from a single third-party project [L, S29], while OpenAxis loads without them [M, S20]. They are commented out in Package.wxs and enabled only if checklist item 10 needs them.
- **Not written:** version-specific keys such as `…\SOLIDWORKS 20xx\AddIns`. Their 3DEXPERIENCE name is unknown, and they are only needed for add-in icons [M, S-registration claim from help 2025 Add-in_Icons].

## 5. Detecting the installed SOLIDWORKS variants

**Main signal for both variants.** For NN = 27…40 (year − 1992, so 2019 = 27 and 2026 = 34), check `HKLM\SOFTWARE\Classes\SldWorks.Application.NN\CLSID` in the 64-bit view.
- xCAD and bluePLM list installed versions exactly this way [H, S24, S25].
- 3DEXPERIENCE R2026x registers its own `SldWorks.Application.34` [H, S26].
- Do not use the unversioned `SldWorks.Application`: it points to the version installed or modified last [M, S-paths checked].

**WiX implementation, using AppSearch only and no custom action:**
- Detection: 14 lines of the form
  `<Property Id="SW27"><RegistrySearch Root="HKLM" Key="SOFTWARE\Classes\SldWorks.Application.27\CLSID" Type="raw" Bitness="always64"/></Property>`
  (`Bitness="always64"` [H, S14]).
- Variant hints for the MSI log and the message (a heuristic) [Inf, based on S26 and S48]:
  - `SW3DXEXE`: `DirectorySearch Path="[ProgramFiles64Folder]Dassault Systemes" Depth="2"` + `FileSearch Name="sldworks.exe"`. This covers `SOLIDWORKS 3DEXPERIENCE\SOLIDWORKS`, `…3DEXPERIENCE R2026x\SOLIDWORKS` and the unverified `SOLIDWORKS Apps 20XX\SOLIDWORKS`.
  - `SWDESKTOPEXE`: the same under `[ProgramFiles64Folder]SOLIDWORKS Corp` with `Depth="1"`.
  - Neither is used for registration.
  - DirectorySearch/@Depth and the FileSearch-Id rule are from [H, S15].
- I deliberately do not resolve the second level (CLSID → LocalServer32 → exe path) to classify desktop versus 3DEXPERIENCE precisely. That would need a custom action and gives nothing for registration.
- The installer **never starts SOLIDWORKS**. 3DEXPERIENCE SOLIDWORKS refuses to start through COM or directly from sldworks.exe [M, S45].

**Behaviour:**

| Found | Installer does |
|---|---|
| none (no ProgID 27–40, no exe) | Installs anyway. Exit dialog: "Kein SOLIDWORKS ab 2019 gefunden. Spiegelwerk ist trotzdem installiert und erscheint, sobald SOLIDWORKS (Desktop oder 3DEXPERIENCE) installiert ist." Not a blocker: an MSI may be installed before SOLIDWORKS, the 3DEXPERIENCE on-demand layout is unverified, and nothing in the package depends on SOLIDWORKS. |
| one (desktop or 3DEXPERIENCE) | Same package, same keys. Exit text: "…für alle SOLIDWORKS-Installationen auf diesem Rechner registriert (Desktop und 3DEXPERIENCE). SOLIDWORKS neu starten; Extras › Zusatzanwendungen." |
| both, or several 3DEXPERIENCE versions | Identical. One HKLM entry serves every installed version [M, Inf]. Desktop and 3DEXPERIENCE of the same year cannot coexist [H, S27]. R2026x FD01+ can hold 2025 SP5 and 2026 side by side [M, S28]. |

- The exit text uses `WIXUI_EXITDIALOGOPTIONALTEXT`: a static default Property plus one `SetProperty … After="AppSearch" Sequence="ui"` for the "none" case [H, S13; the ExitDialog.wxs control was checked in wixtoolset/wix].
- A real block, as a Launch condition: .NET Framework 4.8, via `PropertyRef Id="WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED"` from the Netfx extension (present at tag v7.0.0) [H, S16]. 32-bit Windows is rejected by the x64 package itself.

## 6. Interop strategy (csproj changes)

**Current state is not release-ready.**
- `EmbedInteropTypes=false` + `Private=false` loads only where the exact compiled interop version can be resolved [M, Inf, S31, S32]. Binding to a different version fails before `ConnectToSW` (0x80131040).
- Two developers report that the interops are **not** in the GAC on 3DEXPERIENCE R2026x [M, S20].
- No source confirms that a desktop install puts them in the GAC.
- The csproj comment "like every other add-in does" is wrong: xCAD, SolidDNA and the CodeStack WiX template ship their interops [H, S35, S47].

**Decision: option C. Embed the interop types (No-PIA), compile always against API 2019 (27.3.0), and deliberately leave out any `Microsoft.CSharp` reference.**

- **Why C:**
  - It is the only option whose loading depends neither on GAC contents (desktop or 3DEXPERIENCE) nor on the unverified probing order inside SLDWORKS.exe [Inf].
  - No interop DLLs are shipped. That removes both the redistribution question for swpublished (redist.txt reportedly lists only sldworks and swconst [M, S37]) and the question of where the NuGet copies come from (the packages are published by the third party "avidesk" [H, S38]).
  - Projects validated on 3DEXPERIENCE R2026x (OpenAxis), plus ImpressionFEA, the ROS URDF exporter and the InvenTree add-in, all use it [M, S20, S29].
- **Why no Microsoft.CSharp:** with embedded types, COM `object` returns become `dynamic` [H, S33].
  - Without Microsoft.CSharp, every implicit dynamic call site is a compile error (CS0656), fixed with an explicit `(object)` cast.
  - Example: `var d = (ChamferFeatureData2)(object)f.GetDefinition();`.
  - The code then stays statically bound, exactly as today. This removes the main objection raised in the research, the 236 late-bound call sites.
- **Measured on the current working tree [H, L4]:**
  - 109 errors on 106 lines in 33 files: 95 CS0656, 11 CS1977, 1 CS1976, 2 CS8133.
  - Worst files: IFeatureBuilder.cs 19, SketchCapture.cs 14, SwDoc.cs 10, FeatureCapture.Patterns.cs 7, MirrorPage.cs 6, TopologyCapture.cs 6.
  - Full list: `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/nopia2/err.txt`.
  - The count may grow slightly once the first errors are fixed.
- **Remaining risk:** CodeStack recommends `Embed=false` because of rare type-cast problems [H, S34]. Dassault's SolidPractices reportedly says the same and mentions arrays of structures [L]. Spiegelwerk uses no SOLIDWORKS events [H, research]. This needs the functional test (checklist).
- **Fallback, option B, if casts misbehave:** keep `EmbedInteropTypes=false`, set `Private=true`, remove `ExcludeAssets="runtime"`, and add three `File` elements for the 27.3.0.52 DLLs.
  - Risk: it only works if the add-in folder (LoadFrom location hint) is checked before SOLIDWORKS' own folder. Microsoft documents that order [H, S31], and SolidDNA/xCAD practice implies it [M, S35, S36], but it is unconfirmed on 3DEXPERIENCE.

**Spiegelwerk.csproj, replacing the two interop ItemGroups:**
```xml
<!-- Interop types are embedded (No-PIA): Spiegelwerk.dll references no SolidWorks.Interop
     assembly, so the same build loads in desktop SOLIDWORKS 2019+ and 3DEXPERIENCE regardless
     of GAC contents. Always compiled against API 2019 (27.3.0); newer members only via SwLate.
     Deliberately no Microsoft.CSharp reference: every COM 'object' result must be cast to
     (object) explicitly, so no dynamic call site can creep in (error CS0656). -->
<PropertyGroup>
  <SwInteropVersion Condition="'$(SwInteropVersion)' == ''">27.3.0</SwInteropVersion>
  <SwInteropFromNuGet Condition="'$(SwInteropFromNuGet)' == ''">true</SwInteropFromNuGet>
</PropertyGroup>
<ItemGroup Condition="'$(SwInteropFromNuGet)' == 'true'">
  <PackageReference Include="SolidWorks.Interop.sldworks" Version="$(SwInteropVersion)" ExcludeAssets="runtime" />
  <PackageReference Include="SolidWorks.Interop.swconst" Version="$(SwInteropVersion)" ExcludeAssets="runtime" />
  <PackageReference Include="SolidWorks.Interop.swpublished" Version="$(SwInteropVersion)" ExcludeAssets="runtime" />
</ItemGroup>
<ItemGroup Condition="'$(SwInteropFromNuGet)' != 'true'">
  <Reference Include="SolidWorks.Interop.sldworks"><HintPath>$(SolidWorksApiDir)\SolidWorks.Interop.sldworks.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="SolidWorks.Interop.swconst"><HintPath>$(SolidWorksApiDir)\SolidWorks.Interop.swconst.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="SolidWorks.Interop.swpublished"><HintPath>$(SolidWorksApiDir)\SolidWorks.Interop.swpublished.dll</HintPath><Private>false</Private></Reference>
</ItemGroup>
<!-- PackageReference has no EmbedInteropTypes metadata: set it on the resolved references. -->
<Target Name="EmbedSolidWorksInterop" AfterTargets="ResolveAssemblyReferences">
  <ItemGroup>
    <ReferencePath Condition="$([System.String]::Copy('%(Filename)').StartsWith('SolidWorks.Interop.'))">
      <EmbedInteropTypes>true</EmbedInteropTypes>
    </ReferencePath>
  </ItemGroup>
</Target>
```

- **Mechanism tested [H, L4]:** with 27.3.0 and with 32.1.0, the resulting Spiegelwerk.dll has no AssemblyRef to SolidWorks.Interop. Test project: `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/nopia3`.
- **Why 27.3.0 is now the default even for local builds:** compiling with embedding against a newer local api\redist would let code use members newer than 2019 without any error [Inf]. The 2019/2024 CI matrix stays as it is.
- **Also update:**
  - STOLPERSTEINE no. 49 and the csproj header comment;
  - `install.cmd`, which works with option C even where no interop DLLs are present.

## 7. Uninstall and upgrade

- `Package Id="Spiegelwerk.SolidWorksAddin"` (WiX v6+ replacement for the UpgradeCode) **must never change** [H, S10]. Equally fixed:
  - CLSID `8a3c6f2e-5b1d-4e7a-9c3f-6d2e1b0a4f57` and ProgId `Spiegelwerk.Addin` (L2);
  - CommandGroup ID 47110.
  - A new CLSID would show up as a second add-in and leave orphaned keys behind [Inf].
- `<MajorUpgrade Schedule="afterInstallInitialize" AllowSameVersionUpgrades="yes" DowngradeErrorMessage="Eine neuere Version von Spiegelwerk ist bereits installiert."/>`:
  - **afterInstallInitialize:** a failed upgrade rolls back the removal. The default `afterInstallValidate` can leave neither version installed [H, S11].
  - **AllowSameVersionUpgrades:** without it, two CI builds with the same x.y.z install side by side as two products [H, S11].
  - **4th version field:** MSI ignores it, so every release must raise major, minor or patch [H, S11].
- **ProductVersion:** `!(bind.FileVersion.SpiegelwerkDll)`. It equals the assembly version, because both come from `<Version>` [H, L1]. Do not set AssemblyVersion separately.
- **Uninstall removes:**
  - both files;
  - the whole CLSID tree (`RemoveRegistryKey removeOnUninstall`, which also catches RegAsm leftovers);
  - the ProgId and the HKLM Addins key;
  - the uninstalling user's AddInsStartup value.
- **Uninstall leaves behind:** other users' AddInsStartup values, SOLIDWORKS' own per-user UI state and `%LOCALAPPDATA%\Spiegelwerk\icons`. These are probably harmless [Inf].
- **Locked DLL:** if SOLIDWORKS has the DLL loaded, Restart Manager and the FilesInUse dialog in WixUI ask the user to close it. Optionally use `util:CloseApplication Target="SLDWORKS.exe" PromptToContinue="yes"` [H, S44].
- **3DEXPERIENCE clean reinstall:** the reseller clean-uninstall guides delete `HKLM\SOFTWARE\SolidWorks` and `HKCU\Software\SolidWorks` [L, S43]. That also deletes the Spiegelwerk keys. Fix: Settings > Apps > Spiegelwerk > Repair (MSI repair rewrites the registry). Document this in the README.

## 8. The per-user AddInsStartup key

**Decision:**
- HKLM Default=1 carries "load at start-up" for every user.
- The MSI also writes `HKCU\Software\SolidWorks\AddInsStartup\{G}`=1 for the installing user only, in its own component `AddinStartupUser` with the HKCU value as KeyPath. Mixing it with per-machine data triggers ICE57 [H, S41].
- Only the default value is written [L, S30].

Why this way:
- In silent deployments by Intune or SCCM the installing account is SYSTEM. In an interactive MSI the HKCU write lands only in one profile [H, S19, Inf], so HKLM=1 has to do the work for everyone [M, S22, S23].
- I believe, without confirmation [Inf], that with an interactive MSI and over-the-shoulder elevation the HKCU write goes to the user who launched the MSI, not to the admin account. That would be better than Inno, which uses the admin account [H, S6]. Checklist.
- If the test shows that HKLM=1 does not tick Start Up for other users, use Active Setup as a per-user, run-once registry write. It is not included now.

## 9. Code signing (none available)

All of this is general knowledge [Gen, M] and was not researched in this run.

- **Download from GitHub Releases:**
  - Edge may first say "…msi wird nicht häufig heruntergeladen" (choose "Beibehalten").
  - Starting it shows SmartScreen: "Der Computer wurde durch Windows geschützt", then "Weitere Informationen" and "Trotzdem ausführen".
- **UAC prompt:** "Herausgeber: Unbekannt" on a yellow banner.
- **Hard blocks:**
  - Smart App Control on Windows 11 (if enabled) can block an unsigned MSI with no override.
  - AppLocker or WDAC in companies can block it outright.
- **The add-in DLL itself:** SOLIDWORKS does not require a signature [Inf]. Some antivirus heuristics may flag a new unsigned DLL.
- **MSI versus ZIP:** files the MSI extracts carry no Mark of the Web. Spiegelwerk.dll taken from a downloaded ZIP could be refused by .NET Framework when loaded from the CodeBase path (loadFromRemoteSources) [Gen, M]. This is another reason to ship an MSI rather than ZIP + install.cmd.
- **Mitigations:**
  - publish a SHA-256 per release, computed in CI;
  - optionally `actions/attest-build-provenance` (availability for private repos depends on the plan);
  - later a free open-source signing service or a paid certificate.

## 10. Checklist: what only a real machine can settle

Run on one desktop machine (2019+) and one 3DEXPERIENCE machine (R2025x/R2026x), each with a second, clean Windows user.

**Before installing (read only):**
1. `reg query "HKLM\SOFTWARE\Classes\SldWorks.Application.NN\CLSID"` and the CLSID's `LocalServer32`. Does every installed version appear, including 3DEXPERIENCE, and in on-demand mode both .33 and .34? Does the path contain `Dassault Systemes`?
2. Exact 3DEXPERIENCE root folder (R2025x suffix or not; `SOLIDWORKS Apps 20XX` in on-demand mode). Is `sldworks.exe` at depth 2 below `C:\Program Files\Dassault Systemes`?
3. `reg query HKLM\SOFTWARE\SolidWorks /s /f "SolidWorks Folder"` on the 3DEXPERIENCE machine (does it write this key?).
4. Export `HKLM\SOFTWARE\SolidWorks\AddIns` on the 3DEXPERIENCE machine. Do other third-party add-ins carry `DesktopAddinType` / `3DExperienceAddinType`?
5. Only for fallback B: `dir C:\Windows\Microsoft.NET\assembly\GAC_MSIL\*SolidWorks.Interop*` on both machines, and `api\redist\redist.txt` (is swpublished listed?).
6. Machine with an earlier `scripts\install.cmd` registration: do `removeOnInstall` and the uninstall leave no RegAsm leftovers behind?

**Install:**
7. Download the MSI through the browser. Record the exact SmartScreen and UAC texts and whether Smart App Control blocks it.
8. Standard user with admin credentials: in which profile does `HKCU\…\AddInsStartup\{G}` end up?
9. After install: `CodeBase` = `file:///C:\Program Files\Spiegelwerk\Spiegelwerk.dll`, and `HKLM\SOFTWARE\SolidWorks\Addins\{G}` is 1 / Title / Description.

**Loading:**
10. Desktop: Tools > Add-Ins lists "Spiegelwerk" under other add-ins, Active and Start Up ticked, and the CommandManager tab shows in parts and assemblies. 3DEXPERIENCE: the same, started from the platform or the "SOLIDWORKS Design" shortcut. If it is missing there, add the two DWORDs from item 4 and try again.
11. Second, clean user without an HKCU key: does HKLM=1 alone load the add-in at start-up? Test both variants, and also HKLM=1 with HKCU=0 (which wins?).
12. Untick Start Up and restart: is the HKCU value kept? Does R2026x reset (Default) to 0?
13. Only if the add-in does not appear or loads with an error: Fusion log (`HKLM\SOFTWARE\Microsoft\Fusion` EnableLog/ForceLog/LogPath) to see which assembly is missing.
14. Functional test with option C: the first test from STOLPERSTEINE, including the PropertyManager page (handler interface embedded), both command callbacks and every feature type. Watch for InvalidCastException.
15. 3DEXPERIENCE: "In neues Teil" on a part stored on the platform (AssemblyMirror uses the original's local path). Is there a usable path?
16. SOLIDWORKS started "as administrator" loads the add-in.
17. Makers or Student seats: does it load at all? Dassault says third-party add-ins are not supported there [M, S39].

**Upgrade, uninstall, platform updates:**
18. vX → vX+1 with SOLIDWORKS running and the add-in loaded: the Restart Manager dialog appears, only one entry remains in Apps, the CLSID values are the new ones, and no old `InprocServer32\<version>` subkey is left. The same build installed twice is maintenance mode, not a second product.
19. Uninstall: all keys are gone and SOLIDWORKS starts without an error or a dead entry, even where another user's AddInsStartup value is left over.
20. 3DEXPERIENCE FD update or hotfix after install: does the add-in still load? After a 3DEXPERIENCE clean reinstall, does "Repair" restore it?

**CI can settle these (no real machine needed):**
- Does ICE validation run on `windows-latest`? ICE33 warnings are expected [M]; the fallback is `SuppressIces`/`SuppressValidation` [L, S42].
- Install, COM activation without SOLIDWORKS, and uninstall (job below).

---

## Files to create or change

All under `spiegelwerk/` unless noted.

**`installer/Spiegelwerk.Installer.wixproj`**
```xml
<Project Sdk="WixToolset.Sdk/7.0.0">
  <PropertyGroup>
    <AcceptEula>wix7</AcceptEula>
    <InstallerPlatform>x64</InstallerPlatform>
    <Cultures>de-DE</Cultures>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WixToolset.UI.wixext" Version="7.0.0" />
    <PackageReference Include="WixToolset.Netfx.wixext" Version="7.0.0" />
    <ProjectReference Include="..\src\Spiegelwerk\Spiegelwerk.csproj" />
  </ItemGroup>
</Project>
```
- The ProjectReference creates a bind path, so `Source="Spiegelwerk.dll"` resolves [H, S12].
- Do not pass `-p:Platform`, because it would flow into the csproj and Spiegelwerk.Core.
- `Cultures` is supported in `wix.targets` 7.0.0 [H, read from the nupkg].

**`installer/Package.wxs`** (`SW28`…`SW39` abbreviated)
```xml
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <?define Clsid = "{8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}" ?>
  <?define AsmName = "Spiegelwerk, Version=!(bind.FileVersion.SpiegelwerkDll), Culture=neutral, PublicKeyToken=null" ?>
  <Package Id="Spiegelwerk.SolidWorksAddin" Name="Spiegelwerk" Manufacturer="Spiegelwerk"
           Version="!(bind.FileVersion.SpiegelwerkDll)" Scope="perMachine">
    <MajorUpgrade Schedule="afterInstallInitialize" AllowSameVersionUpgrades="yes"
                  DowngradeErrorMessage="Eine neuere Version von Spiegelwerk ist bereits installiert." />
    <MediaTemplate EmbedCab="yes" />
    <PropertyRef Id="WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED" />
    <Launch Condition="Installed OR WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED" Message="Spiegelwerk benötigt .NET Framework 4.8." />

    <Property Id="SW27"><RegistrySearch Root="HKLM" Key="SOFTWARE\Classes\SldWorks.Application.27\CLSID" Type="raw" Bitness="always64" /></Property>
    <!-- … SW28 … SW39 identical … -->
    <Property Id="SW40"><RegistrySearch Root="HKLM" Key="SOFTWARE\Classes\SldWorks.Application.40\CLSID" Type="raw" Bitness="always64" /></Property>
    <Property Id="SW3DXEXE"><DirectorySearch Id="DsyRoot" Path="[ProgramFiles64Folder]Dassault Systemes" Depth="2"><FileSearch Name="sldworks.exe" /></DirectorySearch></Property>
    <Property Id="SWDESKTOPEXE"><DirectorySearch Id="SwCorpRoot" Path="[ProgramFiles64Folder]SOLIDWORKS Corp" Depth="1"><FileSearch Name="sldworks.exe" /></DirectorySearch></Property>

    <Property Id="WIXUI_EXITDIALOGOPTIONALTEXT" Value="Spiegelwerk ist für alle SOLIDWORKS-Installationen auf diesem Rechner registriert (Desktop und 3DEXPERIENCE). SOLIDWORKS neu starten; das Add-In steht unter Extras › Zusatzanwendungen." />
    <SetProperty Id="WIXUI_EXITDIALOGOPTIONALTEXT" After="AppSearch" Sequence="ui"
                 Condition="NOT (SW27 OR SW28 OR SW29 OR SW30 OR SW31 OR SW32 OR SW33 OR SW34 OR SW35 OR SW36 OR SW37 OR SW38 OR SW39 OR SW40 OR SW3DXEXE OR SWDESKTOPEXE)"
                 Value="Kein SOLIDWORKS ab 2019 gefunden. Spiegelwerk ist trotzdem installiert und erscheint, sobald SOLIDWORKS (Desktop oder 3DEXPERIENCE) installiert ist." />

    <StandardDirectory Id="ProgramFiles64Folder"><Directory Id="INSTALLFOLDER" Name="Spiegelwerk" /></StandardDirectory>
    <Feature Id="Main">
      <ComponentGroupRef Id="AddinFiles" />
      <ComponentRef Id="AddinStartupUser" />
    </Feature>
    <ui:WixUI Id="WixUI_Minimal" />
    <WixVariable Id="WixUILicenseRtf" Value="Hinweis.rtf" />
  </Package>

  <Fragment>
    <ComponentGroup Id="AddinFiles" Directory="INSTALLFOLDER">
      <Component><File Source="Spiegelwerk.Core.dll" /></Component>
      <Component>
        <File Id="SpiegelwerkDll" Source="Spiegelwerk.dll" KeyPath="yes" />
        <RemoveRegistryKey Id="ClsidClean" Action="removeOnInstall" Root="HKCR" Key="CLSID\$(Clsid)" />
        <RemoveRegistryKey Id="ClsidRemove" Action="removeOnUninstall" Root="HKCR" Key="CLSID\$(Clsid)" />
        <Class Id="$(Clsid)" Advertise="no" Context="InprocServer32" ThreadingModel="both"
               ForeignServer="mscoree.dll" Description="Spiegelwerk.Addin.SpiegelwerkAddin">
          <ProgId Id="Spiegelwerk.Addin" Description="Spiegelwerk.Addin.SpiegelwerkAddin" />
        </Class>
        <RegistryKey Root="HKCR" Key="CLSID\$(Clsid)\InprocServer32">
          <RegistryValue Name="Class" Value="Spiegelwerk.Addin.SpiegelwerkAddin" Type="string" />
          <RegistryValue Name="Assembly" Value="$(AsmName)" Type="string" />
          <RegistryValue Name="RuntimeVersion" Value="v4.0.30319" Type="string" />
          <RegistryValue Name="CodeBase" Value="file:///[#SpiegelwerkDll]" Type="string" />
        </RegistryKey>
        <RegistryKey Root="HKCR" Key="CLSID\$(Clsid)\InprocServer32\!(bind.FileVersion.SpiegelwerkDll)">
          <RegistryValue Name="Class" Value="Spiegelwerk.Addin.SpiegelwerkAddin" Type="string" />
          <RegistryValue Name="Assembly" Value="$(AsmName)" Type="string" />
          <RegistryValue Name="RuntimeVersion" Value="v4.0.30319" Type="string" />
          <RegistryValue Name="CodeBase" Value="file:///[#SpiegelwerkDll]" Type="string" />
        </RegistryKey>
        <RegistryKey Root="HKCR" Key="CLSID\$(Clsid)\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
                     ForceCreateOnInstall="yes" ForceDeleteOnUninstall="yes" />
        <RegistryKey Root="HKLM" Key="SOFTWARE\SolidWorks\Addins\$(Clsid)">
          <RegistryValue Value="1" Type="integer" />
          <RegistryValue Name="Title" Value="Spiegelwerk" Type="string" />
          <RegistryValue Name="Description" Value="Spiegelt Features als eigenständige, unabhängige Features (ohne Spiegel-Feature)." Type="string" />
          <!-- Only if checklist item 10 requires it:
          <RegistryValue Name="DesktopAddinType" Value="2" Type="integer" />
          <RegistryValue Name="3DExperienceAddinType" Value="2" Type="integer" /> -->
        </RegistryKey>
      </Component>
    </ComponentGroup>
    <Component Id="AddinStartupUser" Directory="INSTALLFOLDER">
      <RegistryValue Root="HKCU" Key="Software\SolidWorks\AddInsStartup\$(Clsid)" Value="1" Type="integer" KeyPath="yes" />
    </Component>
  </Fragment>
</Wix>
```
- `ForceCreateOnInstall` alone does not remove the key on uninstall, hence `ForceDeleteOnUninstall` [M, S46].
- RemoveRegistryValues runs before WriteRegistryValues in the MSI sequence [Gen, M].

**`installer/Hinweis.rtf`:** the WixUI_Minimal licence page shows placeholder text unless this file is supplied [H, S13]. Write simple RTF by hand, not from Word (Word RTF can show as blank) [H, S13]. The repository has no LICENSE file, so the text is the user's decision.

**`.github/workflows/spiegelwerk.yml`:** add `tags: ['spiegelwerk-v*']` under `on.push` and a new job:
```yaml
  installer:
    name: Installer (MSI)
    needs: [core, addin]
    runs-on: windows-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v5
        with: { dotnet-version: '8.0.x' }
      - name: MSI bauen
        run: dotnet build installer/Spiegelwerk.Installer.wixproj -c Release -p:SwInteropFromNuGet=true -p:SwInteropVersion=27.3.0
      - name: Installieren, ohne SOLIDWORKS aktivieren, deinstallieren
        shell: powershell
        run: |
          $ErrorActionPreference = 'Stop'
          $msi = (Get-ChildItem installer\bin -Recurse -Filter *.msi | Select-Object -First 1).FullName
          $clsid = '{8A3C6F2E-5B1D-4E7A-9C3F-6D2E1B0A4F57}'
          $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /l*v install.log" -Wait -PassThru
          if ($p.ExitCode -ne 0) { Get-Content install.log -Tail 80; throw "Installation: $($p.ExitCode)" }
          $ips = Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$clsid\InprocServer32"
          if ($ips.CodeBase -notlike '*Program Files\Spiegelwerk\Spiegelwerk.dll') { throw "CodeBase: $($ips.CodeBase)" }
          if ((Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\Addins\$clsid").'(default)' -ne 1) { throw 'Addins-Schlüssel fehlt' }
          # separate process, so the loaded DLL does not block the uninstall
          powershell -NoProfile -Command "New-Object -ComObject Spiegelwerk.Addin | Out-Null"; if ($LASTEXITCODE) { throw 'COM-Aktivierung' }
          $p = Start-Process msiexec.exe -ArgumentList "/x `"$msi`" /qn /l*v uninstall.log" -Wait -PassThru
          if ($p.ExitCode -ne 0) { throw "Deinstallation: $($p.ExitCode)" }
          if (Test-Path "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$clsid") { throw 'CLSID nicht entfernt' }
          if (Test-Path "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\Addins\$clsid") { throw 'Addins nicht entfernt' }
      - uses: actions/upload-artifact@v7
        with: { name: spiegelwerk-msi, path: spiegelwerk/installer/bin/**/*.msi, if-no-files-found: error }
```
- The COM activation without SOLIDWORKS only succeeds if Spiegelwerk.dll needs no interop DLL, so it also checks option C [Inf].
- Release job: same pattern as pdfwerk (tag must equal `<Version>`), rename to `Spiegelwerk-<Version>-x64.msi`, add a `.sha256` file, `softprops/action-gh-release@v3`.

**Also update:**
- `src/Spiegelwerk/Spiegelwerk.csproj` (section 6);
- 33 .cs files with `(object)` casts (list in `err.txt`);
- README "Bauen und installieren" (MSI for users, scripts for development, the Repair hint, Makers not supported);
- `docs/STOLPERSTEINE.md` no. 48/49.

---

## Sources

- S1 https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md
- S2 https://github.com/wixtoolset/wix/blob/main/OSMFEULA.txt · https://github.com/wixtoolset/wix/blob/v6.0.2/OSMFEULA.txt
- S3 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/osmf.mdx
- S4 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/whatsnew/releasenotes.md
- S5 https://github.com/jrsoftware/issrc/blob/main/license.txt
- S6 https://github.com/jrsoftware/issrc/blob/main/ISHelp/isetup.xml
- S7 https://learn.microsoft.com/en-us/windows/win32/msi/selfreg-table
- S8 https://github.com/microsoft/referencesource/blob/main/mscorlib/system/runtime/interopservices/registrationservices.cs
- S9 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/class.mdx
- S10 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/package.mdx
- S11 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/majorupgrade.mdx
- S12 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/tools/msbuild.md
- S13 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/tools/wixext/wixui.md · https://github.com/wixtoolset/wix/blob/main/src/ext/UI/wixlib/ExitDialog.wxs
- S14 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/registrysearch.mdx
- S15 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/directorysearch.mdx
- S16 https://github.com/wixtoolset/wix/blob/v7.0.0/src/ext/NetFx/wixlib/NetFx48.wxs
- S17 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/removeregistrykey.mdx
- S18 https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/SysInfo/merged-view-of-hkey-classes-root.md
- S19 https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/Msi/custom-action-in-script-execution-options.md
- S20 https://github.com/chriswoodle/openaxis-solidworks/blob/548f1a0197cc96e4f3bcf57509d74886c93ec84f/src/OpenAxis.SolidWorks/OpenAxis.SolidWorks.csproj (and README.md, OpenAxisAddin.cs)
- S21 https://github.com/xarial/codestack/blob/master/solidworks-api/deployment/manual/index.md
- S22 https://help.solidworks.com/2023/English/api/sldworksapiprogguide/OVERVIEW/Using_SwAddin_to_Create_a_SOLIDWORKS_Addin.htm (search snippet only)
- S23 https://github.com/angelsix/solidworks-api/blob/develop/SolidDna/AngelSix.SolidDna/SolidWorks/Integration/AddIn/AddInIntegration.cs
- S24 https://github.com/xarial/xcad/blob/f9feb293f74576a6a5dbb3ffcca99502e0a257ed/src/SolidWorks/SwApplicationFactory.cs
- S25 https://github.com/bluerobotics/bluePLM/blob/f9d8e3fc1c29dc3937fa7322bb5fd3c83d90bc7c/solidworks-service/BluePLM.SolidWorksService/SolidWorksComRegistry.cs
- S26 https://github.com/amorygalili/solidworks-mcp/blob/4d1b932151c1ece10ccbc31b16ff53f24b8905af/solidworks-mcp/demo-output/DEMO-TRANSCRIPT.md · …/src/swmcp/com/install.py
- S27 https://help.solidworks.com/2022/english/swconnected/sldworks/t_install_sw_connected.htm
- S28 https://3dswym.3dexperience.3ds.com/post/solidworks-news-info/new-installation-capabilities-for-solidworks-design-on-the-3dexperience-platform-with-r2026x-fd01_BjeifrBjRdKFyIQN1FrJ7A
- S29 https://github.com/aemarconnet/ImpressionFEA/blob/b150b287db4bd70953b686a87ac0a8efba1bb670/tools/installer/ImpressionFEA.iss
- S30 https://github.com/Kadenvh/adze-cad/blob/main/CHANGELOG.md
- S31 https://github.com/dotnet/docs/blob/main/docs/framework/deployment/how-the-runtime-locates-assemblies.md
- S32 https://github.com/angelsix/solidworks-api/issues/111
- S33 https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/inputs#embedinteroptypes
- S34 https://github.com/xarial/codestack/blob/master/solidworks-api/getting-started/index.md
- S35 https://github.com/CAD-Booster/solidworks-api/blob/master/README.md
- S36 https://xcad.xarial.com/target-versions/
- S37 https://github.com/meirka8/swbridge/blob/main/docs/THIRD-PARTY-NOTICES.md
- S38 https://api.nuget.org/v3/registration5-semver1/solidworks.interop.sldworks/index.json
- S39 https://www.solidworks.com/solution/solidworks-makers/3dexperience-solidworks-makers
- S40 https://github.com/facebookresearch/project_superdex/blob/main/superdex_studio/superdex_cad_exporter/installer/solidworks/SuperDexCadExporter.wxs
- S41 https://learn.microsoft.com/en-us/windows/win32/msi/ice57
- S42 https://github.com/wixtoolset/icebreaker
- S43 https://github.com/Owl29-Brain/digitalgarden/blob/0c3641465fdf606536a17f24a84d276e53c8d48b/src/site/notes/3DExperience%20SOLIDWORKS%20Questions/Perform%20a%20clean%20uninstall%20of%203DEXPERIENCE%20SOLIDWORKS%20(SOLIDWORKS%20Connected)%2C%20including%20registry%20keys%20and%20folders..md
- S44 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/util/closeapplication.mdx
- S45 https://github.com/pedropaulovc/harmonic-analyzer/blob/main/memory/solidworks-3dx-launch.md
- S46 https://github.com/firegiant/docs/blob/main/src/content/docs/wix/schema/wxs/registrykey.mdx
- S47 https://github.com/xarial/codestack/blob/master/solidworks-api/deployment/installer/wix/Product.wxs
- S48 https://github.com/projectchrono/chrono-solidworks/blob/15f05597bab0b30b97c96ea625a2d03e10ae19d9/installer/installer_addin.iss
- S50 https://github.com/dotnet/docs/blob/main/docs/framework/tools/regasm-exe-assembly-registration-tool.md
- S51 https://help.solidworks.com/2026/english/api/sldworksapiprogguide/Overview/SOLIDWORKS_Connected.htm (read via the offline crawl)

Local files and measurements:
- L1 `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Spiegelwerk.csproj`
- L2 `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Addin/SpiegelwerkAddin.cs`
- L3 `/home/user/Claude/spiegelwerk/src/Spiegelwerk/Addin/IconFactory.cs`
- L4 scratch measurements: `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/nopia2/err.txt` and the test project `/tmp/claude-0/-home-user-Claude/150560a9-9ace-5e30-a2c4-acf08fab4c35/scratchpad/nopia3/src/Spiegelwerk/Spiegelwerk.csproj`

No files under `/home/user/Claude` were modified.