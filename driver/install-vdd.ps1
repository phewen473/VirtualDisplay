# install-vdd.ps1 - installs the reviewed Virtual Display Driver (25.7.23 driver-only package) using Windows' own APIs. # purpose
# Run from an elevated (Administrator) PowerShell:  powershell -ExecutionPolicy Bypass -File driver\install-vdd.ps1        # how to run
# Copyright (c) 2026 P. H. Ewen (KO4WSC). MIT License. The driver itself is (c) Virtual Display, MIT License.          # copyright
param(                                                                                                    # optional settings
    [string]$PackagePath = (Join-Path $PSScriptRoot 'VirtualDisplayDriver')                               # driver files: by default the folder next to this script
)                                                                                                         # end of settings
$ErrorActionPreference = 'Stop'                                                                           # any error stops the script
$log = Join-Path $env:TEMP 'vdd-install.log'                                                              # install log goes in the user's temp folder
Start-Transcript -Path $log -Force | Out-Null                                                             # record everything printed below
try {                                                                                                     # everything inside is reported as SUCCESS or FAILED
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()) # who is running this
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator' } # driver installs need admin rights
    $pkg = $PackagePath                                                                                   # folder holding MttVDD.dll/.inf/.cat
    $inf = Join-Path $pkg 'MttVDD.inf'                                                                    # the driver's setup file
    $expected = @{                                                                                        # SHA-256 of the files that were reviewed
        'MttVDD.dll' = 'C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05'                  # the driver
        'mttvdd.cat' = '08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6'                  # its signed catalog
        'MttVDD.inf' = '550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D'                  # its setup file
    }                                                                                                     # end of hash list
    foreach ($f in $expected.Keys) {                                                                      # check each file
        $h = (Get-FileHash (Join-Path $pkg $f) -Algorithm SHA256).Hash                                    # its actual hash
        if ($h -ne $expected[$f]) { throw "Hash mismatch on $f - aborting" }                              # refuse anything that is not the reviewed file
    }                                                                                                     # end of hash check
    Write-Output 'STEP hashes OK'                                                                         # progress note

    $settingsDir = Join-Path "$env:SystemDrive\" 'VirtualDisplayDriver'                                   # settings folder the driver reads: \VirtualDisplayDriver on the Windows drive
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null                                     # create it if missing
    if (-not (Test-Path (Join-Path $settingsDir 'vdd_settings.xml'))) {                                   # keep existing settings if there are any
        Copy-Item (Join-Path $pkg 'vdd_settings.xml') (Join-Path $settingsDir 'vdd_settings.xml')         # otherwise copy the starting settings
    }                                                                                                     # end of settings copy
    Write-Output 'STEP settings folder OK'                                                                # progress note

    $sig = Get-AuthenticodeSignature (Join-Path $pkg 'mttvdd.cat')                                        # read the catalog's digital signature
    if ($sig.Status -ne 'Valid') { throw 'Catalog signature not valid - aborting' }                       # refuse an unsigned or tampered package
    $cert = $sig.SignerCertificate                                                                        # the signer's certificate (leaf only)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher','LocalMachine') # Trusted Publishers store
    $store.Open('ReadWrite')                                                                              # open it for changes
    $already = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }                  # was it trusted before we started?
    if (-not $already) { $store.Add($cert) }                                                              # trust it temporarily so the install does not prompt; removed below
    Write-Output "STEP signer trusted temporarily: $($cert.Subject)"                                      # progress note

    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;                                      // C# helper compiled on the fly
public static class DevInst {                                                                               // creates the device and installs the driver
  [StructLayout(LayoutKind.Sequential)] public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; } // SetupAPI device record
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid g, IntPtr hwnd); // new empty device list
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiCreateDeviceInfoW(IntPtr s, string name, ref Guid g, string desc, IntPtr hwnd, int flags, ref SP_DEVINFO_DATA d); // new device in the list
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr s, ref SP_DEVINFO_DATA d, int prop, byte[] buf, int size); // sets its hardware id
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiCallClassInstaller(int fn, IntPtr s, ref SP_DEVINFO_DATA d); // registers it with Windows
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s); // frees the list
  [DllImport("newdev.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwnd, string hwid, string inf, int flags, out bool reboot); // installs the driver for that hardware id
  public static void CreateNode(string hwid) {                                                              // makes the virtual "Root\MttVDD" device
    Guid g = new Guid("4D36E968-E325-11CE-BFC1-08002BE10318");                                              // the Display adapters device class
    IntPtr s = SetupDiCreateDeviceInfoList(ref g, IntPtr.Zero);                                             // empty list for that class
    if (s == new IntPtr(-1)) throw new Exception("CreateDeviceInfoList " + Marshal.GetLastWin32Error());    // failed: report the Windows error
    try {                                                                                                   // always free the list
      SP_DEVINFO_DATA d = new SP_DEVINFO_DATA(); d.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));        // device record with its size filled in
      if (!SetupDiCreateDeviceInfoW(s, "Display", ref g, null, IntPtr.Zero, 1, ref d)) throw new Exception("CreateDeviceInfo " + Marshal.GetLastWin32Error()); // create it (1 = generate a unique name)
      byte[] b = Encoding.Unicode.GetBytes(hwid + "\0\0");                                                  // hardware id as a double-null-terminated list
      if (!SetupDiSetDeviceRegistryPropertyW(s, ref d, 1, b, b.Length)) throw new Exception("SetHardwareId " + Marshal.GetLastWin32Error()); // 1 = SPDRP_HARDWAREID
      if (!SetupDiCallClassInstaller(0x19, s, ref d)) throw new Exception("RegisterDevice " + Marshal.GetLastWin32Error()); // 0x19 = DIF_REGISTERDEVICE
    } finally { SetupDiDestroyDeviceInfoList(s); }                                                          // free the list
  }                                                                                                         // end of CreateNode
  public static bool Update(string hwid, string inf) {                                                      // installs the driver; returns "reboot needed"
    bool reboot;                                                                                            // filled in by Windows
    if (!UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hwid, inf, 1, out reboot)) throw new Exception("UpdateDriver 0x" + Marshal.GetLastWin32Error().ToString("X")); // 1 = INSTALLFLAG_FORCE
    return reboot;                                                                                          // tell the caller
  }                                                                                                         // end of Update
}                                                                                                           // end of DevInst
'@
    # (the C# block above ends here; every line inside it carries its own // comment)                    # note

    $hwid = 'Root\MttVDD'                                                                                 # hardware id the driver's .inf expects
    $existing = Get-PnpDevice | Where-Object { $_.HardwareID -contains $hwid }                            # is the device already there?
    $created = $false                                                                                     # remember if we made it (for rollback)
    if (-not $existing) { [DevInst]::CreateNode($hwid); $created = $true; Write-Output 'STEP device node created' } # make it if missing
    else { Write-Output 'STEP device node already present' }                                              # otherwise reuse it
    try {                                                                                                 # install, with rollback on failure
        $reboot = [DevInst]::Update($hwid, $inf)                                                          # install the driver
        Write-Output "STEP driver installed. RebootRequired=$reboot"                                      # progress note
    } catch {                                                                                             # install failed
        Write-Output "ERROR driver install failed: $($_.Exception.Message)"                               # say why
        if ($created) {                                                                                   # we made the device ourselves
            Get-PnpDevice | Where-Object { $_.HardwareID -contains $hwid } | ForEach-Object { pnputil /remove-device $_.InstanceId | Out-String | Write-Output } # remove it again
            Write-Output 'STEP rolled back device node'                                                   # progress note
        }                                                                                                 # end of rollback
        throw                                                                                             # pass the failure on
    } finally {                                                                                           # always runs
        if (-not $already) { $store.Remove($cert); Write-Output 'STEP temporary signer trust removed' }   # undo the temporary trust
        $store.Close()                                                                                    # close the certificate store
    }                                                                                                     # end of install
    Start-Sleep -Seconds 5                                                                                # give Windows a moment to start the device
    Get-PnpDevice | Where-Object { $_.HardwareID -contains $hwid } | Format-List FriendlyName,Status,Class,InstanceId | Out-String | Write-Output # show the result
    Write-Output 'RESULT SUCCESS'                                                                         # all done
} catch {                                                                                                 # anything failed
    Write-Output "RESULT FAILED: $($_.Exception.Message)"                                                 # report it
} finally {                                                                                               # always runs
    Stop-Transcript | Out-Null                                                                            # close the log
}                                                                                                         # end of script
