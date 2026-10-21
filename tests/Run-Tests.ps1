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
    # Lot X1 : le scénario h ajoute environ 45 s ; AZUR_STARLOCO_SCENARIOS=h le joue seul après la connexion.
    $azurTests += 'BotStarLocoLiveSmoke'
    $azurTests += 'BotAnimationQueueSmoke'
    $azurTests += 'BotPointsHitDeathSmoke'
    $azurTests += 'BotSpellEffectsSmoke'
    $azurTests += 'BotSpellProjectilesSmoke'
    $azurTests += 'BotRecolorSmoke'
    $azurTests += 'BotAccessoriesSmoke'
    $azurTests += 'BotExtraClipsSmoke'
    # Lot X2 (tombe, fantôme, menu de son personnage, mariage) : couvert par BotSessionSmoke, BotMapActionsSmoke et les scénarios
    # x2a à x2c de BotStarLocoLiveSmoke ; AZUR_STARLOCO_SCENARIOS=x2a,x2b,x2c ne joue que ceux-là après la connexion (scénario a).
    # Lot X3 (pseudo d'un compte neuf, M0, nouveau niveau, perte de connexion) : BotHandshakeSmoke, BotSessionSmoke, BotConnectionUiSmoke
    # et les scénarios x3a à x3c ; x3c arrête puis relance le serveur de jeu par AZUR_STARLOCO_GAME_STOP et AZUR_STARLOCO_GAME_START.
    $azurTests += 'BotConnectionUiSmoke'
    # Lot X4 (repas du familier et de la monture, garde et résurrection) : couvert par BotInventoryGridSmoke, BotExchangeSmoke et les
    # scénarios x4a à x4c de BotStarLocoLiveSmoke (environ 60 s) ; AZUR_STARLOCO_SCENARIOS=x4a,x4b,x4c ne joue que ceux-là.
    # Lot X4-CORR (OM et OD refusés pendant un échange, EREO après ER17 ignoré, seuls les messages du repas gardés sous l'objet, boîte de
    # quantité abandonnée au clic seulement et « OK » entier, croix de la case 16 sans monture, x4a à x4c joués juste après le scénario a,
    # objets des scénarios x4 détruits en jeu) : couvert par les mêmes tests.
    # Lot X32 (changement de nom en jeu par la potion 10860, roulette du Bouftou TC/TV) : couvert par BotSessionSmoke, BotConnectionUiSmoke,
    # BotHandshakeSmoke et les scénarios x32a et x32b de BotStarLocoLiveSmoke (potion, kamas et personnages écrits dans la base par le test) ;
    # AZUR_STARLOCO_SCENARIOS=x32a,x32b ne joue que ceux-là après le scénario a.
    # Lot X5 (artisanat sécurisé : fenêtre CraftSecureExchange des types 12 et 13, demandes ERK 12/13, paiement EP/Ep, volet) : couvert
    # par BotCraftSmoke, BotExchangeSmoke et le scénario x5 de BotStarLocoLiveSmoke (deux comptes inventés sur une carte à forge) ;
    # AZUR_STARLOCO_SCENARIOS=x5 ne joue que celui-là après la connexion.
    # Lot X6 (demandes ER12/ER13 depuis le menu d'un joueur, ERET dans le chat, EW+ à l'arrivée, clic gauche sur les acteurs, carte de
    # l'artisan du livre) : couvert par BotCraftSmoke, BotMapActionsSmoke et les scénarios x6 et x6b de BotStarLocoLiveSmoke (comptes
    # inventés sur la carte de la forge) ; AZUR_STARLOCO_SCENARIOS=x6,x6b ne joue que ceux-là après la connexion.
    # Lot X7 (événements de combat visibles : journal dans le chat, zones GDZ, cellules GDC, clip « prêt », icônes Go, invisibilité GA;150) :
    # couvert par BotFightUiSmoke, BotMapViewSmoke, BotChatUiSmoke et le scénario x7 de BotStarLocoLiveSmoke (Sram inventé, deux pièges,
    # Invisibilité, fenêtre de B restée sur la carte) ; AZUR_STARLOCO_SCENARIOS=x7 ne joue que celui-là après la connexion.
    # Lot X12 (boîte de quantité pour jeter, détruire, proposer, déposer et les kamas, ConfirmDropItem, glisser-déposer vers les volets
    # d'échange, de coffre, de sacoches et d'atelier, six tris de l'inventaire, cases de sort voilées selon le client) : couvert par
    # BotInventoryGridSmoke, BotShopSmoke, BotExchangeSmoke, BotCraftSmoke, BotMountSmoke, BotBannerSmoke, BotFightUiSmoke et le scénario
    # x12 de BotStarLocoLiveSmoke (échange entre deux comptes et banque) ; AZUR_STARLOCO_SCENARIOS=x12 ne joue que celui-là après la connexion.
    # Lot X8 (visée des sorts : portée #246696 avec le bonus GA;117/116, pointeur des zones d'effet, curseurs du client, chemin de
    # combat survolé puis GA001) : couvert par BotCombatSmoke, BotMapViewSmoke, BotFightUiSmoke, BotFightProtocolSmoke, BotEntitiesSmoke,
    # BotWorldMapSmoke et le scénario x8 de BotStarLocoLiveSmoke (Iop inventé défié par A) ; AZUR_STARLOCO_SCENARIOS=x8 ne joue que celui-là après la connexion.
    # Lot X13 (objets vivants : Of, Os, Ox, OCO relu, fiche et rangée de l'inventaire ; OU avec cible et cellule par la visée d'objet
    # de la carte) : couvert par BotInventoryGridSmoke et le scénario x13 de BotStarLocoLiveSmoke (chapeau vivant de la base, pain donné
    # à l'autre joueur) ; AZUR_STARLOCO_SCENARIOS=x13 ne joue que celui-là après la connexion.
    # Lot X33 (ligne de vue, états du lanceur et lancers par cible vérifiés avant un sort, cases ciblables en #0066CC, mode tactique local) :
    # couvert par BotCombatSmoke, BotMapViewSmoke, BotFightUiSmoke et le scénario x33 de BotStarLocoLiveSmoke (Flèche Magique contre A) ;
    # AZUR_STARLOCO_SCENARIOS=x33 ne joue que celui-là après la connexion.

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
        & $azurPython.Source (Join-Path $azurRoot 'tools\client-analysis\tests\test_exporter_accessoires.py')
        if ($LASTEXITCODE -ne 0) { throw 'Échec des tests de exporter_accessoires.py.' }
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
