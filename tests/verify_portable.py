"""Verify every file and the privacy boundary of an Azur portable archive."""

import hashlib
import sys
import zipfile
from pathlib import PurePosixPath


def verify(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Entrées ZIP dupliquées")
        expected = {}
        for line in archive.read("Azur/SHA256SUMS.txt").decode("utf-8").splitlines():
            digest, separator, name = line.partition("  ")
            if not separator or len(digest) != 64 or name in expected:
                raise ValueError("Manifeste SHA-256 invalide")
            expected[name] = digest
        actual = set(names) - {"Azur/SHA256SUMS.txt", "Azur/README-PORTABLE.md"}
        if actual != set(expected):
            raise ValueError("Le manifeste ne couvre pas tous les fichiers")
        required = {
            "Azur/Outil_Azur_complet.exe",
            "Azur/Outil_Azur_complet.exe.config",
            "Azur/Tools_protocol.dll",
            "Azur/Tool_BotProtocol.dll",
            "Azur/Tool_Editor.dll",
            "Azur/ressources/maps/sols/Herbe/4.png",
            "Azur/ressources/Bot/UI/Selection/scene.png",
            "Azur/ressources/Bot/UI/Selection/artwork-80.png",
            "Azur/ressources/Bot/UI/Selection/PROVENANCE.md",
            "Azur/ressources/Bot/sorts/10.png",
            "Azur/ressources/Bot/sprites/80F.png",
            "Azur/docs/KAUTH_COMPATIBILITE.md",
            "Azur/docs/GUIDE_EDITEURS.md",
            "Azur/docs/ETAT_PROJET.md",
            "Azur/docs/CAPTURE_RESEAU.md",
            "Azur/docs/BOT_STARLOCO.md",
            "Azur/docs/BRIEF_REDESIGN_UI_BOT.md",
        }
        if not required <= actual:
            raise ValueError("Exécutable, dépendances ou ressources absents")
        for name in names:
            parts = PurePosixPath(name).parts
            if len(parts) < 2 or parts[0] != "Azur" or ".." in parts or name.startswith("/"):
                raise ValueError("Chemin dangereux dans l'archive : " + name)
            lower = name.lower()
            if lower.endswith(("/config.json", "/botconfig.json", ".pdb", ".tmp")) or "/auth/" in lower or "/world/" in lower or "/accountsingle/" in lower:
                raise ValueError("Configuration ou symbole local inclus : " + name)
        for name, digest in expected.items():
            hash_value = hashlib.sha256()
            with archive.open(name) as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    hash_value.update(chunk)
            if hash_value.hexdigest() != digest:
                raise ValueError("Empreinte incorrecte : " + name)
    print(f"OK : {len(expected)} fichiers vérifiés ; aucune configuration locale incluse.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Utilisation : python tests/verify_portable.py Azur-portable.zip")
    verify(sys.argv[1])
