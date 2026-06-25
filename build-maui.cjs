const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

console.log('Starting parallel .NET MAUI build pipeline (Player/Admin)...');

// Recursively find a folder/file matching targetName within a directory tree
function findAppBundle(dir, targetName) {
  if (!fs.existsSync(dir)) return null;
  const items = fs.readdirSync(dir, { withFileTypes: true });
  for (const item of items) {
    const fullPath = path.join(dir, item.name);
    if (item.isDirectory()) {
      if (item.name === targetName) {
        return fullPath;
      }
      const found = findAppBundle(fullPath, targetName);
      if (found) return found;
    }
  }
  return null;
}

const releaseDir = path.join(__dirname, 'release');
const tempReleaseDir = path.join(__dirname, 'release_temp');

// Ensure output directories exist
if (!fs.existsSync(releaseDir)) {
  fs.mkdirSync(releaseDir, { recursive: true });
}
if (fs.existsSync(tempReleaseDir)) {
  fs.rmSync(tempReleaseDir, { recursive: true, force: true });
}
fs.mkdirSync(tempReleaseDir, { recursive: true });

function cleanViteBuild() {
  console.log('Cleaning up Vite dist folder...');
  if (fs.existsSync('dist')) {
    fs.rmSync('dist', { recursive: true, force: true });
  }
  fs.mkdirSync('dist');
}

function compileViteAssets(mode) {
  console.log(`\nCompiling Vite frontend assets for MAUI mode: ${mode}...`);
  execSync('npx vite build', {
    env: { ...process.env, VITE_APP_MODE: mode },
    stdio: 'inherit'
  });

  // Write exact dynamic mode identifier configuration file
  const configPath = path.join(__dirname, 'dist', 'app-config.json');
  fs.writeFileSync(configPath, JSON.stringify({ mode }, null, 2));
  console.log(`Wrote dist/app-config.json for MAUI mode: ${mode}.`);
}

function cleanMauiIntermediate() {
  console.log('Cleaning up Maui bin and obj directories for pristine build state...');
  const binDir = path.join(__dirname, 'maui', 'bin');
  const objDir = path.join(__dirname, 'maui', 'obj');
  if (fs.existsSync(binDir)) {
    fs.rmSync(binDir, { recursive: true, force: true });
  }
  if (fs.existsSync(objDir)) {
    fs.rmSync(objDir, { recursive: true, force: true });
  }
}

function buildMauiApp(mode, variantId, targetFramework, rid, minOS) {
  console.log(`\nCompiling .NET MAUI Parallel C# Application for mode: ${mode} [${variantId}]...`);
  const csprojPath = path.join(__dirname, 'maui', 'InterstitialerMaui.csproj');
  const modeOutputDir = path.join(tempReleaseDir, `${mode}_${variantId}`);

  let platformPublishArgs = '';

  if (process.platform === 'win32') {
    // On Windows, publish as a completely self-contained deployment bundled with .NET runtime
    platformPublishArgs = `-p:WindowsPackageType=None -p:SelfContained=true -p:PublishSelfContained=true -r win-x64`;
  } else if (process.platform === 'darwin') {
    platformPublishArgs = `-p:CreatePackage=false -p:SuppressSdkDetection=true -p:_SuppressSdkDetection=true -p:SkipXcodeValidation=true -p:SdkValidation=false -p:_SdkValidation=false`;
    if (rid) {
      platformPublishArgs += ` -r ${rid}`;
    }
    if (minOS) {
      platformPublishArgs += ` -p:SupportedOSPlatformVersion=${minOS}`;
    }
  } else {
    console.log(`Building for current default platform: ${process.platform}`);
    return;
  }

  const buildCmd = `dotnet publish "${csprojPath}" -c Release -f ${targetFramework} -o "${modeOutputDir}" ${platformPublishArgs}`;
  console.log(`Executing Build Command: ${buildCmd}`);
  
  try {
    execSync(buildCmd, { stdio: 'inherit' });
    console.log(`Successfully built .NET MAUI program binary for ${mode} [${variantId}].`);
  } catch (err) {
    console.error(`Failed to compile C# MAUI program for ${mode} [${variantId}]:`, err.message);
    throw err;
  }
}

function packageMauiApp(mode, variantId, suffix) {
  console.log(`\nPackaging and renaming C# MAUI artifact outputs for mode: ${mode} [${variantId}]...`);
  const modeOutputDir = path.join(tempReleaseDir, `${mode}_${variantId}`);

  // Read version dynamically from package.json
  const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, 'package.json'), 'utf8'));
  const version = pkg.version;

  if (process.platform === 'win32') {
    const humanAppName = `Interstitial-er ${mode}`;
    const installerName = `${humanAppName}-${version}-Windows-Installer-maui.exe`;
    const installerPath = path.join(releaseDir, installerName);

    // Dynamic Inno Setup Script Generation
    const issScriptPath = path.join(tempReleaseDir, `installer-${mode}.iss`);
    const iconPath = path.join(__dirname, 'src', 'assets', 'images', 'win', 'icon.ico');
    const setupIconLine = fs.existsSync(iconPath) 
      ? `SetupIconFile=${iconPath.replace(/\\/g, '\\\\')}`
      : ``;

    const issContent = `
[Setup]
AppName=${humanAppName} (MAUI)
AppVersion=${version}
DefaultDirName={autopf}\\${humanAppName} (MAUI)
DefaultGroupName=${humanAppName} (MAUI)
OutputDir=${releaseDir.replace(/\\/g, '\\\\')}
OutputBaseFilename=${humanAppName}-${version}-Windows-Installer-maui
Compression=lzma
SolidCompression=yes
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
${setupIconLine}

[Files]
Source: "${modeOutputDir.replace(/\\/g, '\\\\')}\\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\${humanAppName} (MAUI)"; Filename: "{app}\\InterstitialerMaui.exe"
Name: "{autodesktop}\\${humanAppName} (MAUI)"; Filename: "{app}\\InterstitialerMaui.exe"

[Run]
Description: "Launch ${humanAppName}"; Filename: "{app}\\InterstitialerMaui.exe"; Flags: postinstall nowait skipifsilent
`;

    try {
      fs.writeFileSync(issScriptPath, issContent, 'utf8');
      console.log(`Wrote Inno Setup config file to: ${issScriptPath}`);

      console.log(`Compiling Windows installer executable via Inno Setup (iscc)...`);
      execSync(`iscc "${issScriptPath}"`, { stdio: 'inherit' });
      console.log(`Successfully compiled Windows setup installer: ${installerName}`);
    } catch (e) {
      console.warn(`Windows Inno Setup execution skipped or failed (${e.message}). Falling back to self-contained ZIP packaging.`);
      
      const outputZipPath = path.join(releaseDir, `${humanAppName}-${version}-Windows-Installer-maui.zip`);
      console.log(`Compressing self-contained Windows output structure to ZIP: ${outputZipPath}`);
      
      const zipCmd = `powershell -NoProfile -Command "Compress-Archive -Path '${modeOutputDir}/*' -DestinationPath '${outputZipPath}' -Force"`;
      try {
        execSync(zipCmd, { stdio: 'inherit' });
        console.log(`Successfully compiled Windows ZIP package: ${path.basename(outputZipPath)}`);
      } catch (zipErr) {
        console.error(`Failed to execute powershell ZIP compression:`, zipErr.message);
      }
    }
  } else if (process.platform === 'darwin') {
    // Locate the .app bundle
    let appBundlePath = path.join(modeOutputDir, 'InterstitialerMaui.app');
    if (!fs.existsSync(appBundlePath)) {
      console.log(`Initial path InterstitialerMaui.app not found. Searching subdirectories recursively...`);
      let foundPath = findAppBundle(modeOutputDir, 'InterstitialerMaui.app');
      if (!foundPath) {
        console.log(`Not found under modeOutputDir. Searching in standard MAUI bin Release directory...`);
        const binPath = path.join(__dirname, 'maui', 'bin');
        foundPath = findAppBundle(binPath, 'InterstitialerMaui.app');
      }

      if (foundPath) {
        console.log(`Found built .app bundle at path: ${foundPath}`);
        try {
          // Relocate the nested .app bundle to the standard top-level modeOutputDir to preserve uniform downstream packaging
          if (fs.cpSync) {
            fs.cpSync(foundPath, appBundlePath, { recursive: true });
            try { fs.rmSync(foundPath, { recursive: true, force: true }); } catch (rmErr) {}
          } else {
            fs.renameSync(foundPath, appBundlePath);
          }
          console.log(`Relocated bundle to standard path: ${appBundlePath}`);
        } catch (moveErr) {
          console.error(`Failed to relocate app bundle: ${moveErr.message}. Falling back to using found path directly.`);
          appBundlePath = foundPath;
        }
      } else {
        console.error(`Could not locate built .app bundle anywhere under ${modeOutputDir} or the project bin/ directory.`);
        return;
      }
    }

    const humanAppName = `Interstitial-er ${mode} (MAUI)`;
    const renamedAppPath = path.join(modeOutputDir, `${humanAppName}.app`);

    // Rename to clean branded name prior to DMG packaging
    try {
      if (fs.existsSync(renamedAppPath)) {
        fs.rmSync(renamedAppPath, { recursive: true, force: true });
      }
      fs.renameSync(appBundlePath, renamedAppPath);
      console.log(`Renamed build app folder successfully to: ${renamedAppPath}`);
    } catch (renameErr) {
      console.error(`Failed to rename build bundle directory:`, renameErr.message);
    }

    const dmgName = `Interstitial-er ${mode}-${version}-${suffix}.dmg`;
    const dmgPath = path.join(releaseDir, dmgName);

    console.log(`Packaging macOS volume structure directly to Disk Image (.dmg): ${dmgPath}`);

    // Call standard native hdiutil to construct Mac DMGs
    const hdiutilCmd = `hdiutil create -volname "${humanAppName}" -srcfolder "${renamedAppPath}" -ov -format UDZO "${dmgPath}"`;
    try {
      execSync(hdiutilCmd, { stdio: 'inherit' });
      console.log(`Successfully compiled macOS disk image (.dmg): ${dmgName}`);
    } catch (dmgErr) {
      console.error(`Failed to build native DMG volume (${dmgErr.message}). Falling back to standard compressed zip bundle...`);

      const outputZipPath = path.join(releaseDir, `Interstitial-er ${mode}-${version}-${suffix}.zip`);
      const zipCmd = `zip -r "${outputZipPath}" "${humanAppName}.app"`;
      try {
        execSync(zipCmd, { cwd: modeOutputDir, stdio: 'inherit' });
        console.log(`Successfully compressed fallback macOS ZIP package: ${path.basename(outputZipPath)}`);
      } catch (zipErr) {
        console.error(`Failed to execute fallback zip command:`, zipErr.message);
      }
    }
  } else {
    console.log(`Packaging skipped on unsupported platform: ${process.platform}`);
  }
}

(async () => {
  try {
    const isMac = process.platform === 'darwin';

    // Mac-specific build variants to support both Apple Silicon and Intel legacy platforms
    const macVariants = [
      {
        id: 'Silicon-new',
        suffix: 'Mac-Silicon-new-maui',
        targetFramework: 'net9.0-maccatalyst16.0',
        rid: 'maccatalyst-arm64',
        minOS: '13.1'
      },
      {
        id: 'Intel-legacy',
        suffix: 'Mac-Intel-legacy-maui',
        targetFramework: 'net9.0-maccatalyst16.0',
        rid: 'maccatalyst-x64',
        minOS: '13.1'
      }
    ];

    // --- Step 1: Admin Build ---
    console.log('\n=========================================');
    console.log(' BUILDING INTERSTITIAL-ER MAUI - ADMIN ');
    console.log('=========================================\n');
    cleanViteBuild();
    compileViteAssets('Admin');

    if (isMac) {
      for (const variant of macVariants) {
        console.log(`\n--- Compilation of Admin Variant: ${variant.id} ---`);
        cleanMauiIntermediate();
        buildMauiApp('Admin', variant.id, variant.targetFramework, variant.rid, variant.minOS);
        packageMauiApp('Admin', variant.id, variant.suffix);
      }
    } else {
      cleanMauiIntermediate();
      buildMauiApp('Admin', 'Windows', 'net9.0-windows10.0.19041.0', 'win-x64', null);
      packageMauiApp('Admin', 'Windows', null);
    }

    // --- Step 2: Player Build ---
    console.log('\n=========================================');
    console.log(' BUILDING INTERSTITIAL-ER MAUI - PLAYER ');
    console.log('=========================================\n');
    cleanViteBuild();
    compileViteAssets('Player');

    if (isMac) {
      for (const variant of macVariants) {
        console.log(`\n--- Compilation of Player Variant: ${variant.id} ---`);
        cleanMauiIntermediate();
        buildMauiApp('Player', variant.id, variant.targetFramework, variant.rid, variant.minOS);
        packageMauiApp('Player', variant.id, variant.suffix);
      }
    } else {
      cleanMauiIntermediate();
      buildMauiApp('Player', 'Windows', 'net9.0-windows10.0.19041.0', 'win-x64', null);
      packageMauiApp('Player', 'Windows', null);
    }

    console.log('\n=========================================');
    console.log(' MAUI BUILD COMPLETED SUCCESSFULLY ');
    console.log('=========================================\n');

  } catch (err) {
    console.error('\nAn error occurred during MAUI compilation/packaging:', err);
    process.exitCode = 1;
  } finally {
    // Cleanup temporary folders
    if (fs.existsSync(tempReleaseDir)) {
      try {
        fs.rmSync(tempReleaseDir, { recursive: true, force: true });
        console.log('Cleared temporary release files.');
      } catch (e) {
        console.error('Failed to clear temporary files:', e);
      }
    }
  }
})();
