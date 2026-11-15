# CLAUDE.md

## Réalisé avec Claude

Pesée a été conçue par son auteur et développée avec **Claude**, l'assistant IA d'[Anthropic](https://www.anthropic.com),
utilisé dans **Claude Code**. L'auteur a fixé les objectifs et pris les décisions (fonctionnement, licence, prix,
publication) ; Claude a écrit le code, les scripts et les textes, et a mené les étapes techniques.

Ce que Claude a réalisé :

- **L'application** : le moteur d'analyse parallèle (`src\Scanner.cs`), la fenêtre dessinée sur mesure
  (`src\MainForm.cs`), les appels Windows (`src\Native.cs`), l'installation sans droits administrateur.
- **La version Microsoft Store** : le module C++ qui place « Analyser la taille » directement dans le clic droit de
  Windows 11 (`menu\PeseeMenu.cpp`), le manifeste MSIX, la génération des logos, le script `build-store.ps1`,
  le passage au Windows App Certification Kit, puis le remplissage de la soumission dans Partner Center.
- **La publication** : le dépôt GitHub, le README, la release, et les images de la fiche du Store.

Ce fichier sert aussi de guide pour les prochaines sessions de Claude Code sur ce projet.

## Le projet en bref

Pesée affiche le poids de chaque sous-dossier d'un dossier, depuis le clic droit de l'Explorateur Windows.
C# WinForms, .NET Framework 4.8 (présent sur tous les Windows 10 et 11), aucune dépendance, interface en français.

| Dossier | Contenu |
|---|---|
| `src\` | Code de l'application (une seule assembly) |
| `menu\` | `PeseeMenu.dll` (C++, IExplorerCommand) : clic droit moderne, version Store uniquement |
| `store\` | Manifeste MSIX (gabarit), guide `SOUMISSION.md`, images de la fiche dans `captures\` |
| `tools\` | Dessin de l'icône (`MakeIcon.cs`) et des logos du Store (`MakeStoreAssets.cs`) |
| `res\` | Icône et manifeste de l'exécutable |

## Compiler

| Commande | Résultat | Outils nécessaires |
|---|---|---|
| `build.ps1` | `bin\Pesee.exe` (version classique) | aucun (csc de Windows) |
| `build-release.ps1` | `release\` : zip PowerShell + `Pesee.exe` pour GitHub | aucun |
| `build-store.ps1` | `store\out\Pesee_<version>.msixbundle` (x64 + ARM64) | Windows SDK, VS Build Tools C++ |

Lancer chaque script avec `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`.

Pour le Store, `build-store.ps1` attend l'identité Partner Center :
`-IdentityName "Wharro.Pesee" -Publisher "CN=D7033690-214B-466D-8795-282DC8C62D13" -PublisherDisplayName "Wharro" -DisplayName "Pesee"`.
`-DisplayName` doit être identique au nom réservé dans Partner Center (« Pesee », sans accent), sinon le paquet est refusé.

## Règles à respecter

- **Smart App Control** bloque les `.exe` non signés, y compris ceux qu'on vient de compiler. Les outils de build
  tournent donc en mémoire via `Add-Type` (voir `Pesee.ps1` et la génération des logos), jamais en `.exe` lancé.
- **Encodage** : Windows PowerShell 5.1 lit un `.ps1` sans BOM en ANSI. Tout script contenant des accents doit être
  enregistré en UTF-8 **avec BOM**, sinon « Pesée » devient « PesÃ©e ».
- **Version Store** : le symbole `STORE` (`/define:STORE`) retire `src\Installer.cs` ; le Store gère l'installation.
  Le test « Fichiers exécutables bloqués » du kit de certification échoue (Pesée lance des processus) : c'est
  attendu, cela rend seulement l'app indisponible en Windows mode S.
- **Le clic droit moderne** n'accepte que `Directory` et `Directory\Background` (pas `Drive`).
- **Confidentialité** : ne jamais publier de capture montrant les dossiers ou logiciels d'un vrai PC. Les captures
  utilisent un dossier de démonstration fait de fichiers creux (sparse).
- **Versions** : augmenter `Version` dans `src\Program.cs` ; pour le Store, `-Version` au format `x.y.z.0`.
