#!/bin/bash
# Captures d'écran de l'interface du bot sous Linux (Xvfb + Mono), pour docs/captures.
#
# Usage : tools/captures/run-captures.sh [dossier de sortie] [vue…]
#   dossier de sortie : docs/captures par défaut ; vues : 01-connexion, 04-jeu… (toutes par défaut)
# Variables facultatives (données lues au moment de la capture, jamais copiées dans le dépôt) :
#   AZUR_CAPTURE_SQL     export SQL du serveur de jeu StarLoco (table maps) : vraie carte au lieu de la prairie synthétique
#   AZUR_CAPTURE_MAP     identifiant de cette carte (7411 par défaut)
#   AZUR_CAPTURE_CLIENT  dossier du client 1.34 (data/maps/*.swf) : fond de cette carte
# Prérequis : solution compilée en Debug (Outil_Azur_complet/bin/Debug), mono, xvfb-run, ImageMagick (import),
# python3 avec Pillow, SDK .NET (compilateur Roslyn csc.dll) et les assemblys de référence .NET Framework 4.8.
set -u
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:-$ROOT/docs/captures}; [ $# -gt 0 ] && shift
BIN=$ROOT/Outil_Azur_complet/bin/Debug
TOOL=$ROOT/tools/captures/bin
RA=${AZUR_REFERENCE_ASSEMBLIES:-$HOME/.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8}
CSC=${AZUR_CSC:-$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)}
PACKAGES=$ROOT/packages
[ -f "$BIN/Outil_Azur_complet.exe" ] || { echo "Compilez d'abord la solution (Debug) : $BIN/Outil_Azur_complet.exe est absent." >&2; exit 2; }
[ -n "$CSC" ] && [ -f "$CSC" ] || { echo "Compilateur csc.dll introuvable (AZUR_CSC)." >&2; exit 2; }
mkdir -p "$TOOL" "$OUT"
cp "$PACKAGES/SwfDotNet.IO.1.0.0.1/lib/net40-full/SwfDotNet.IO.dll" "$PACKAGES/log4net.2.0.17/lib/net45/log4net.dll" "$TOOL/" 2>/dev/null

REFS="-r:$BIN/Outil_Azur_complet.exe -r:$BIN/Tool_BotProtocol.dll -r:$BIN/Tool_Editor.dll -r:$BIN/Tools_protocol.dll"
for f in mscorlib System System.Core System.Data System.Drawing System.Windows.Forms System.Xml System.Xml.Linq; do REFS="$REFS -r:$RA/$f.dll"; done
dotnet "$CSC" -nologo -nostdlib -noconfig -target:exe -out:"$TOOL/BotCaptures.exe" $REFS "$ROOT"/tools/captures/*.cs > "$TOOL/build.log" 2>&1 \
  || { echo "Compilation de l'outil impossible :"; grep -E "error" "$TOOL/build.log" | head -20; exit 1; }

RAW=$(mktemp -d)
( cd "$RAW" && MONO_PATH=$BIN AZUR_TEST_BIN=$BIN timeout 900 xvfb-run -a -s "-screen 0 1280x1024x24" mono "$TOOL/BotCaptures.exe" "$RAW" "$@" )
rc=$?
# Pillow : largeur ≤ 1280 px, palette optimisée (PNG indexé) ; seules les captures produites remplacent celles du dossier.
python3 -I "$ROOT/tools/captures/optimize.py" "$RAW" "$OUT" || rc=1
rm -rf "$RAW"
echo "exit=$rc sortie=$OUT"
exit $rc
