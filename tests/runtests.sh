#!/bin/bash
# Tests sans base sous Linux (Mono + Xvfb) : chaque tests/<Nom>.cs est compilé avec Roslyn contre Outil_Azur_complet/bin/Debug
# puis lancé dans un dossier de travail neuf. Pendant Linux de Run-Tests.ps1 (qui reste la validation complète sous Windows).
#
# Usage : tests/runtests.sh [NomDuTest…]   (sans argument : la liste ci-dessous)
# Variables facultatives :
#   AZUR_TEST_BIN              dossier des binaires testés (Outil_Azur_complet/bin/Debug par défaut)
#   AZUR_REFERENCE_ASSEMBLIES  assemblys de référence .NET Framework 4.8 (paquet NuGet microsoft.netframework.referenceassemblies.net48)
#   AZUR_CSC                   chemin de csc.dll (sinon le plus récent des SDK .NET de « dotnet --list-sdks »)
#   AZUR_PACKAGES              dossier des paquets NuGet de la solution (packages/ à la racine par défaut)
#   AZUR_TEST_TIMEOUT          durée maximale d'un test en secondes (240 par défaut)
# Prérequis : solution compilée en Debug, mono, xvfb-run (si aucun écran n'est ouvert), SDK .NET.
# Ne lance ni les 8 tests propres à Windows ni les tests d'intégration (MySQL) ; BotStarLocoLiveSmoke réussit sans rien
# contacter tant que AZUR_STARLOCO_LOGIN n'est pas défini (voir tests/README.md).
set -u
ROOT=$(cd "$(dirname "$0")/.." && pwd)
BIN=${AZUR_TEST_BIN:-$ROOT/Outil_Azur_complet/bin/Debug}
RA=${AZUR_REFERENCE_ASSEMBLIES:-${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8}
PACKAGES=${AZUR_PACKAGES:-$ROOT/packages}
LIMIT=${AZUR_TEST_TIMEOUT:-240}
CSC=${AZUR_CSC:-}
if [ -z "$CSC" ] && command -v dotnet >/dev/null 2>&1; then
  SDK=$(dotnet --list-sdks 2>/dev/null | sort -V | tail -1 | sed -E 's/^([^ ]+) \[(.*)\]$/\2\/\1/')
  [ -n "$SDK" ] && CSC=$SDK/Roslyn/bincore/csc.dll
fi
[ -f "$BIN/Outil_Azur_complet.exe" ] || { echo "Compilez d'abord la solution (Debug) : $BIN/Outil_Azur_complet.exe est absent." >&2; exit 2; }
[ -n "$CSC" ] && [ -f "$CSC" ] || { echo "Compilateur csc.dll introuvable (AZUR_CSC)." >&2; exit 2; }
[ -f "$RA/mscorlib.dll" ] || { echo "Assemblys de référence .NET 4.8 introuvables (AZUR_REFERENCE_ASSEMBLIES) : $RA" >&2; exit 2; }

DEFAULT_TESTS="
QueryBuilderSmoke
UiSafetySmoke
MapCodecSmoke
MapProjectSmoke
SwfImportSafetySmoke
MapSwfSmoke
WindowClosingSmoke
ItemClientSwfSmoke
NetworkCaptureSmoke
BotTransportSmoke
BotConfigSmoke
BotGameplaySmoke
BotSpellsSmoke
BotSpellXmlSmoke
BotMapViewSmoke
BotCombatSmoke
BotClientSkinSmoke
BotDialogsSmoke
BotShopSmoke
ResourceManagerSmoke
EmulatorProfileSmoke
BotSessionSmoke
BotActorsModelSmoke
BotPanelsSmoke
BotServerExportsSmoke
BotLangDataSmoke
BotDecorAnchorsSmoke
BotFightProtocolSmoke
BotChatProtocolSmoke
BotNpcDialogTextsSmoke
BotSpriteSheetsSmoke
BotMovementSmoke
BotExchangeSmoke
BotMapActionsSmoke
BotClientIconsSmoke
BotInteractivesSmoke
BotPartySmoke
BotActorRenderSmoke
BotFriendsSmoke
BotChatUiSmoke
BotServerCommandsSmoke
BotBannerSmoke
BotFightUiSmoke
BotInventoryGridSmoke
BotWorldMapSmoke
BotHouseMerchantSmoke
BotGuildSmoke
BotAuctionSmoke
BotAlignmentSmoke
BotCraftSmoke
BotMountSmoke
BotQuestsSmoke
BotStatsSheetSmoke
BotAnimationQueueSmoke
BotPointsHitDeathSmoke
BotSpellEffectsSmoke
BotSpellProjectilesSmoke
BotStarLocoLiveSmoke
"
TESTS=${*:-$DEFAULT_TESTS}

OUT=$ROOT/tests/bin; WORK=$OUT/work-$$
mkdir -p "$OUT" "$WORK"
for dll in "$PACKAGES"/SwfDotNet.IO.*/lib/net40-full/SwfDotNet.IO.dll "$PACKAGES"/log4net.*/lib/net45/log4net.dll; do
  [ -f "$dll" ] && cp "$dll" "$OUT/"
done
[ -f "$OUT/SwfDotNet.IO.dll" ] || { echo "SwfDotNet.IO.dll introuvable (AZUR_PACKAGES) : restaurez les paquets NuGet." >&2; exit 2; }

REFS="-r:$BIN/Outil_Azur_complet.exe -r:$BIN/Tools_protocol.dll -r:$BIN/Tool_Editor.dll -r:$BIN/Tool_BotProtocol.dll -r:$BIN/MySql.Data.dll -r:$OUT/SwfDotNet.IO.dll"
for f in mscorlib System System.Core System.Data System.Drawing System.Windows.Forms System.Xml System.Xml.Linq System.Security System.Net.Http; do
  REFS="$REFS -r:$RA/$f.dll"
done
# Sans écran ouvert, chaque test reçoit son propre Xvfb.
DISPLAY_RUN=""
if [ -z "${DISPLAY:-}" ]; then
  command -v xvfb-run >/dev/null 2>&1 || { echo "Aucun écran (DISPLAY) et xvfb-run absent." >&2; exit 2; }
  DISPLAY_RUN="xvfb-run -a"
fi

pass=0; fail=0
for t in $TESTS; do
  if [ ! -f "$ROOT/tests/$t.cs" ]; then echo "SKIP $t (tests/$t.cs absent)"; continue; fi
  if ! dotnet "$CSC" -nologo -nostdlib -noconfig -target:exe -out:"$OUT/$t.exe" $REFS \
      "$ROOT/tests/TestPaths.cs" "$ROOT/tests/EditorFixtures.cs" "$ROOT/tests/$t.cs" > "$WORK/$t-build.log" 2>&1; then
    echo "BUILD FAIL $t"; grep error "$WORK/$t-build.log" | head -5; fail=$((fail+1)); continue
  fi
  ( cd "$WORK" && MONO_PATH=$BIN AZUR_TEST_BIN=$BIN AZUR_TEST_WORK=$WORK timeout "$LIMIT" $DISPLAY_RUN mono "$OUT/$t.exe" > "$WORK/$t.log" 2>&1 )
  rc=$?
  if [ $rc -eq 0 ]; then echo "PASS $t"; pass=$((pass+1)); else echo "FAIL($rc) $t"; tail -5 "$WORK/$t.log"; fail=$((fail+1)); fi
done
echo "pass=$pass fail=$fail work=$WORK"
[ $fail -eq 0 ]
