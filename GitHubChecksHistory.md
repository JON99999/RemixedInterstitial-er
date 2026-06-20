# Interstitial-er—GitHub Check & Change History

This document serves as the persistent historical log of all inquiries made via the **"Check your work in github"** protocol. It outlines the specific version numbers analyzed, logs provided, recommended solutions, and approved corrections executed in the workspace.

---

## Historical Ledger of GitHub Runs & Workspace Changes

### [Date: June 19, 2026] Analysis of Run v0.1.2 Build Failure
*   **Version Number Targeted**: `v0.1.2`
*   **Source Log / Input URL**: Supplied directly in prompt.
*   **Identified Issues**:
    1. **Missing Windows WinUI 3 Entry Point (CS5001)**: The program compiled for `TargetFramework=net10.0-windows10.0.19041.0` on the virtual runner fails because the MSBuild system doesn't find a static `Main` entry point. In traditional .NET MAUI templates, WinUI 3 native launch bootstrap files are housed under the `Platforms/Windows/` directory. Since there is no `Platforms` directory in the target `maui` module, Windows compilation fails with `error CS5001: Program does not contain a static 'Main' method suitable for an entry point`.
    2. **Obsolete MainPage Warning (CS0618)**: Setting `Application.MainPage` in `maui/App.xaml.cs` constructor at line 14 is obsolete in .NET 10.0 MAUI. The framework suggests overriding `Application.CreateWindow(IActivationState)` and setting properties on `Windows[0].Page` instead.
*   **Proposed Resolution Paths**:
    1. **Create Platforms Bootstrappers**: Create the necessary platform-specific subfolders and files inside `maui/Platforms/Windows/` to provide the standard entry point for WinUI 3. These files are:
       * `maui/Platforms/Windows/App.xaml` (using standard `MauiWinUIApplication`)
       * `maui/Platforms/Windows/App.xaml.cs` (to redirect application startup back to `MauiProgram`)
       * `maui/Platforms/Windows/Program.cs` (to bootstrap WinRT / COM and start the application via `Application.Start`)
    2. **Address Obsolete Property Warning**: Eliminate the deprecation warning by cleaner window layout configuration in `maui/App.xaml.cs`.
*   **Status / Final Execution**: **Executed and Resolved in release v0.1.3**
    *   Generated WinUI 3 bootstrap entry point files (`App.xaml`, `App.xaml.cs`, `Program.cs`) in `/maui/Platforms/Windows/` folder to resolve the missing static `Main` method compiler error (CS5001).
    *   Replaced the obsolete `Application.MainPage` setter assignment inside `/maui/App.xaml.cs` with direct instantiation of `new Window(new MainPage())` inside the `CreateWindow` override hook, eliminating obsolete set warnings (CS0618).
    *   Updated target version from `0.1.2` to `0.1.3` globally across all manifests and guides (`package.json`, `package-lock.json`, `InterstitialerMaui.csproj`, and `/HOW_TO_RELEASE_IN_GITHUB_ONLINE.md`).

---

### [Date: June 19, 2026] Analysis of Run 27851339696 (v0.1.0)
*   **Version Number Targeted**: `v0.1.0`
*   **Source Log / Input URL**: Log content supplied in prompt.
*   **Identified Issues**:
    1. **NuGet Package Mono Restore Failure (NU1102)**: The build machine attempting to restore `.NET 10.0-maccatalyst` packages on Windows fails to locate a compatible `Microsoft.NETCore.App.Runtime.Mono.win-x64` with version `10.0.9`. Because `.NET 10.0` is in preview, this workload dependency isn't natively host-mirrored on NuGet for Windows x64.
    2. **Implicit Package Validation Warnings (MA002)**: Set `<UseMaui>true</UseMaui>` without matching explicit PackageReferences due to restrictive `Condition="'\$(TargetFramework)' == ''"` guards, triggering MA002 warning outputs.
*   **Proposed Resolution Paths**:
    1. Modify `maui/InterstitialerMaui.csproj` to define `<TargetFrameworks>` conditionally based on the building OS environment (so Windows runners only see and restore the Windows Target Framework, and macOS runners only see/restore the MacCatalyst Target Framework).
    2. Clean up `<PackageReference>` evaluation guards and include `<SkipValidateMauiImplicitPackageReferences>true</SkipValidateMauiImplicitPackageReferences>`.
*   **Status / Final Execution**: **Executed and Resolved in v0.1.2**
    *   Applied OS conditional targets inside `maui/InterstitialerMaui.csproj`.
    *   Added `<SkipValidateMauiImplicitPackageReferences>true</SkipValidateMauiImplicitPackageReferences>`.
    *   Updated target version from `0.1.0` to `0.1.2` globally (inside `package.json`, `package-lock.json`, and dynamic instruction sheets).
    *   *Note on v0.1.1*: Version string `v0.1.1` was skipped entirely during development and never existed on GitHub; it does not need to be tracked or analyzed.

---

### [Date: June 19, 2026] No Check Registered Yet (Initialized)
*   **Version Number Targeted**: N/A
*   **Source Log / Input URL**: Initial setup of the check ledger.
*   **Identified Issues**: N/A
*   **Proposed Resolution Paths**: N/A
*   **Status / Final Execution**: Init placeholder setup complete. No changes have been approved or executed based on remote GitHub failure reports in this session yet.

---

## Guidelines for Updating This File

1. **Initial Entry (On "Check your work in github" trigger)**:
   * Record the session timestamp and the target application version.
   * Document the user-provided execution log content or URLs.
   * List the distinct problems identified with their corresponding files.
   * Categorize potential solution paths.

2. **Commit Details (On User Approval & Tool Application)**:
   * When the user gives the express go-ahead to apply changes, document which files were edited.
   * State the specific code sections modified.
   * Verify the workspace locally via `compile_applet` before closing out the entry.
