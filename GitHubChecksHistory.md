# Interstitial-er—GitHub Check & Change History

This document serves as the persistent historical log of all inquiries made via the **"Check your work in github"** protocol. It outlines the specific version numbers analyzed, logs provided, recommended solutions, and approved corrections executed in the workspace.

---

## Historical Ledger of GitHub Runs & Workspace Changes

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
