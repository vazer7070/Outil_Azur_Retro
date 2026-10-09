[CmdletBinding()]
param(
    [switch]$Integration,
    [switch]$NoBuild,
    [switch]$Outils,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateRange(10, 600)]
    [int]$TestTimeoutSeconds = 90,
    [string]$MySqlBin = "$env:ProgramFiles\MySQL\MySQL Server 8.4\bin"
)

$ErrorActionPreference = 'Stop'
$azurRoot = Split-Path -Parent $PSScriptRoot
$azurBin = Join-Path $azurRoot ('Outil_Azur_complet\bin\' + $Configuration)
$azurTestBin = Join-Path $PSScriptRoot 'bin'
$azurWork = Join-Path $azurTestBin ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $azurWork -Force | Out-Null
$azurPreviousBin = $env:AZUR_TEST_BIN
$azurPreviousWork = $env:AZUR_TEST_WORK
$azurMySql = $null
$azurAdmin = $null

try {
    if (!$NoBuild) {
        $azurVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        $azurMSBuild = & $azurVsWhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if (!$azurMSBuild) { throw 'MSBuild de Visual Studio est introuvable.' }
        & $azurMSBuild (Join-Path $azurRoot 'Outil_Azur_complet.sln') /t:Build "/p:Configuration=$Configuration" /nologo /v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'La compilation du projet a échoué.' }
    }

    $env:AZUR_TEST_BIN = $azurBin
    $env:AZUR_TEST_WORK = $azurWork
    # Les tests sont écrits en C# 6 et 7 ($"…", out var, membres =>) : le csc.exe du .NET Framework s'arrête à C# 5.
    # Compilateur Roslyn de Visual Studio ou des Build Tools (vswhere), sinon repli sur l'ancien avec un avertissement.
    $azurCompiler = $null
    if (${env:ProgramFiles(x86)}) {
        $azurVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $azurVsWhere) {
            $azurCompiler = & $azurVsWhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
        }
    }
    if (!$azurCompiler) {
        $azurCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        Write-Warning "Compilateur Roslyn introuvable (vswhere, MSBuild\**\Bin\Roslyn\csc.exe) : repli sur $azurCompiler, limité à C# 5, qui refusera les tests écrits en C# 6 ou 7. Installez Visual Studio ou ses Build Tools avec MSBuild."
    }
    else { Write-Host "Compilateur des tests : $azurCompiler" }
    $azurReferences = @('Outil_Azur_complet.exe', 'Tools_protocol.dll', 'Tool_Editor.dll', 'Tool_BotProtocol.dll', 'MySql.Data.dll') |
        ForEach-Object { '/r:' + (Join-Path $azurBin $_) }
    $azurSwfLibrary = Join-Path $azurRoot 'packages\SwfDotNet.IO.1.0.0.1\lib\net40-full\SwfDotNet.IO.dll'
    Copy-Item -LiteralPath $azurSwfLibrary -Destination (Join-Path $azurTestBin 'SwfDotNet.IO.dll') -Force
    $azurVerifierLog = Join-Path $azurRoot 'packages\log4net.2.0.17\lib\net45\log4net.dll'
    if (!(Test-Path -LiteralPath $azurVerifierLog)) { throw 'Restaurez aussi tests/packages.config avec NuGet dans le dossier packages du dépôt.' }
    Copy-Item -LiteralPath $azurVerifierLog -Destination (Join-Path $azurTestBin 'log4net.dll') -Force
    $azurReferences += '/r:' + $azurSwfLibrary
    $azurReferences += @('/r:System.Data.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.Linq.dll')
    $azurTests = @('CharacterEditSmoke', 'QueryBuilderSmoke', 'UiSafetySmoke', 'MapCodecSmoke', 'MapProjectSmoke', 'MapEditorSmoke', 'SwfImportSafetySmoke', 'MapSwfSmoke', 'WindowClosingSmoke', 'EditorWorkflowSmoke')
    $azurTests += 'AllEditorsWorkflowSmoke'
    $azurTests += 'ItemClientSwfSmoke'
    $azurTests += @('NetworkCaptureSmoke', 'BotTransportSmoke')
    $azurTests += @('BotConfigSmoke', 'BotHandshakeSmoke', 'BotGameplaySmoke', 'BotSpellsSmoke', 'BotSpellXmlSmoke', 'BotMapViewSmoke', 'BotUiSmoke', 'BotClientSkinSmoke')
    $azurTests += @('BotCombatSmoke', 'BotCombatUiSmoke', 'BotEntitiesSmoke')
    $azurTests += @('BotDialogsSmoke', 'BotShopSmoke')
    $azurTests += 'ResourceManagerSmoke'
    $azurTests += 'EmulatorProfileSmoke'
    $azurTests += 'BotSessionSmoke'
    $azurTests += 'BotActorsModelSmoke'
    $azurTests += 'BotPanelsSmoke'
    $azurTests += 'BotServerExportsSmoke'
    $azurTests += 'BotLangDataSmoke'
    $azurTests += 'BotDecorAnchorsSmoke'
    $azurTests += 'BotFightProtocolSmoke'
    $azurTests += 'BotChatProtocolSmoke'
    $azurTests += 'BotNpcDialogTextsSmoke'
    $azurTests += 'BotSpriteSheetsSmoke'
    $azurTests += 'BotMovementSmoke'
    $azurTests += 'BotExchangeSmoke'
    $azurTests += 'BotMapActionsSmoke'
    $azurTests += 'BotClientIconsSmoke'
    $azurTests += 'BotInteractivesSmoke'
    $azurTests += 'BotPartySmoke'
    $azurTests += 'BotActorRenderSmoke'
    $azurTests += 'BotFriendsSmoke'
    $azurTests += 'BotChatUiSmoke'
    $azurTests += 'BotServerCommandsSmoke'
    $azurTests += 'BotBannerSmoke'
    $azurTests += 'BotFightUiSmoke'
    $azurTests += 'BotInventoryGridSmoke'
    $azurTests += 'BotWorldMapSmoke'
    $azurTests += 'BotHouseMerchantSmoke'
    $azurTests += 'BotGuildSmoke'
    $azurTests += 'BotAuctionSmoke'
    $azurTests += 'BotAlignmentSmoke'
    $azurTests += 'BotCraftSmoke'
    $azurTests += 'BotMountSmoke'
    $azurTests += 'BotQuestsSmoke'
    $azurTests += 'BotStatsSheetSmoke'
    # Bot sur un vrai StarLoco local (deux comptes inventés, sans base de test) : réussi sans rien lancer si AZUR_STARLOCO_LOGIN
    # n'est pas défini ; avec le serveur, prévoir -TestTimeoutSeconds 300 (voir tests/README.md).
    $azurTests += 'BotStarLocoLiveSmoke'
    $azurTests += 'BotAnimationQueueSmoke'
    $azurTests += 'BotPointsHitDeathSmoke'
    $azurTests += 'BotSpellEffectsSmoke'
    $azurTests += 'BotSpellProjectilesSmoke'
    $azurTests += 'BotRecolorSmoke'

    if ($Integration) {
        $azurMysqld = Join-Path $MySqlBin 'mysqld.exe'
        $azurAdmin = Join-Path $MySqlBin 'mysqladmin.exe'
        if (!(Test-Path -LiteralPath $azurMysqld) -or !(Test-Path -LiteralPath $azurAdmin)) { throw 'Indiquez le dossier bin de MySQL avec -MySqlBin.' }
        $azurBusy = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
            Where-Object { $_.Port -eq 43306 }
        if ($azurBusy) { throw 'Le port de test 43306 est déjà occupé. Aucun serveur existant ne sera utilisé ou arrêté.' }
        $azurData = Join-Path $azurWork 'mysql-data'
        New-Item -ItemType Directory -Path $azurData | Out-Null
        & $azurMysqld --no-defaults --initialize-insecure "--datadir=$azurData" "--basedir=$(Split-Path -Parent $MySqlBin)" *> (Join-Path $azurWork 'mysql-init.log')
        if ($LASTEXITCODE -ne 0) { throw 'Initialisation du serveur MySQL de test impossible. Consultez mysql-init.log.' }
        $azurArguments = '--no-defaults --console --bind-address=127.0.0.1 --port=43306 --mysqlx=0 --datadir="' + $azurData + '" --basedir="' + (Split-Path -Parent $MySqlBin) + '"'
        $azurMySql = Start-Process -FilePath $azurMysqld -ArgumentList $azurArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $azurWork 'mysql-out.log') -RedirectStandardError (Join-Path $azurWork 'mysql-error.log')
        $azurReady = $false
        $azurDeadline = [DateTime]::UtcNow.AddSeconds(25)
        while ([DateTime]::UtcNow -lt $azurDeadline) {
            if ($azurMySql.HasExited) { throw 'Le serveur MySQL de test a quitté. Consultez mysql-error.log.' }
            & $azurAdmin --no-defaults --protocol=tcp --host=127.0.0.1 --port=43306 --user=root ping 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $azurReady = $true; break }
            Start-Sleep -Milliseconds 250
        }
        if (!$azurReady) { throw 'Le serveur MySQL de test ne répond pas.' }
        $azurTests += @('InventoryIntegrationSmoke', 'ModerationIntegrationSmoke', 'ItemCreationIntegrationSmoke', 'ResourceExportIntegrationSmoke', 'MapActionIntegrationSmoke', 'ServerEditingIntegrationSmoke')
        $azurTests += 'AllEditorsIntegrationSmoke'
        $azurTests += 'KauthSchemaIntegrationSmoke'
        $azurTests += 'StarLocoSchemaIntegrationSmoke'
    }

    Push-Location -LiteralPath $azurWork
    try {
        foreach ($azurTest in $azurTests) {
            $azurExe = Join-Path $azurTestBin ($azurTest + '.exe')
            & $azurCompiler /nologo /target:exe "/out:$azurExe" @azurReferences (Join-Path $PSScriptRoot 'TestPaths.cs') (Join-Path $PSScriptRoot 'EditorFixtures.cs') (Join-Path $PSScriptRoot ($azurTest + '.cs'))
            if ($LASTEXITCODE -ne 0) { throw "Compilation du test $azurTest impossible." }
            # Each harness owns only synthetic fixtures. Bound a hung UI/message loop
            # so the parent's finally can still stop its isolated MySQL instance.
            $azurStdout = Join-Path $azurWork ($azurTest + '-out.log')
            $azurStderr = Join-Path $azurWork ($azurTest + '-error.log')
            $azurTestProcess = Start-Process -FilePath $azurExe -WorkingDirectory $azurWork -WindowStyle Hidden -PassThru -RedirectStandardOutput $azurStdout -RedirectStandardError $azurStderr
            try {
                $azurTestDeadline = [DateTime]::UtcNow.AddSeconds($TestTimeoutSeconds)
                while (!$azurTestProcess.WaitForExit(1000)) {
                    if ([DateTime]::UtcNow -ge $azurTestDeadline) {
                        $azurTestProcess.Kill()
                        $azurTestProcess.WaitForExit(5000) | Out-Null
                        throw "Le test $azurTest a dépassé $TestTimeoutSeconds secondes. Journaux : $azurWork"
                    }
                }
                $azurExit = $azurTestProcess.ExitCode
                Get-Content -LiteralPath $azurStdout -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
                Get-Content -LiteralPath $azurStderr -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
            }
            finally { $azurTestProcess.Dispose() }
            if ($azurExit -ne 0) { throw "Échec du test $azurTest." }
        }
    }
    finally { Pop-Location }
    if ($Outils) {
        # Outils d'analyse du client (Rust) : swfsvg se teste sur un SWF fabriqué par ses tests.
        if (!(Get-Command cargo -ErrorAction SilentlyContinue)) { throw 'cargo est introuvable : installez Rust ou retirez -Outils.' }
        & cargo test --release --quiet --manifest-path (Join-Path $azurRoot 'tools\client-analysis\swfsvg\Cargo.toml')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de swfsvg.' }
        Write-Host 'Tests de swfsvg réussis.'
        # Export des sprites d'acteurs (Python, cairosvg et Pillow) : faux swfsvg, aucun fichier du client.
        $azurPython = Get-Command python3, python -ErrorAction SilentlyContinue | Select-Object -First 1
        if (!$azurPython) { throw 'Python 3 est introuvable : installez-le avec cairosvg et Pillow ou retirez -Outils.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_exporter_sprites.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de exporter_sprites.py.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_exporter_icons.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de exporter_icons.py.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\test_docs2xml.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de docs2xml.py.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_exporter_etats_interactifs.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de exporter_etats_interactifs.py.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_exporter_artworks.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de exporter_artworks.py.' }
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_choisir_gfx_animes.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de choisir_gfx_animes.py.' }
    }
    Write-Host "$($azurTests.Count) tests réussis. Fichiers temporaires et journaux : $azurWork"
}
finally {
    if ($azurMySql -and !$azurMySql.HasExited) {
        & $azurAdmin --no-defaults --protocol=tcp --host=127.0.0.1 --port=43306 --user=root shutdown
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Arrêt MySQL de test impossible : vérifiez le processus indiqué dans les journaux.' }
        else { $azurMySql.WaitForExit(10000) | Out-Null }
    }
    $env:AZUR_TEST_BIN = $azurPreviousBin
    $env:AZUR_TEST_WORK = $azurPreviousWork
}
