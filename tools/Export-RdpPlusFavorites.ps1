# Writes the decrypted Remote Desktop Plus favorites listing that the launcher imports
# (RDP sessions page > Import RDP+). Uses RDP+'s own decryption routine from rdp.exe, so RDP+ must be
# installed. Run with Windows PowerShell 5.1 (powershell.exe), not pwsh. Password switches are masked.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Export-RdpPlusFavorites.ps1 > rdpplus.favorites.txt
param([string]$Favorites = "Favorites Remote Desktop Plus.xml")

$asm = [Reflection.Assembly]::LoadFile("${env:ProgramFiles(x86)}\Remote Desktop Plus\rdp.exe")
try { $types = $asm.GetTypes() } catch { $types = $_.Exception.InnerException.Types | Where-Object { $_ } }
$t = $types[5]
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
$methods = $t.GetMethods($flags)
$decrypt = $methods[1]
$init = $methods[2]
$enum = $decrypt.GetParameters()[1].ParameterType
try { [void]$init.Invoke($null, @()) } catch { "init failed: $($_.Exception.InnerException.Message)" }

[xml]$xml = Get-Content -LiteralPath $Favorites -Encoding Unicode
foreach ($fav in $xml.Data.Favorite) {
    $plain = $null
    foreach ($mode in 0, 1, 2) {
        try {
            $r = $decrypt.Invoke($null, @($fav.CommandLine, [Enum]::ToObject($enum, $mode)))
            if ($r -and $r -match '/') { $plain = $r; break }
        } catch {}
    }

    if ($plain) {
        $masked = $plain -replace '(?i)(/(p|pe|password)(:|=))("[^"]*"|\S+)', '$1***'
        "{0} | secure={1} | {2}" -f $fav.FavoriteName.Trim(), $fav.Secure, $masked
    } else {
        "{0} | secure={1} | (could not decrypt)" -f $fav.FavoriteName.Trim(), $fav.Secure
    }
}
