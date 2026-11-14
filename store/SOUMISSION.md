# Publier Pesée sur le Microsoft Store

## Ce que change la version Store

- **Clic droit direct** : « Analyser la taille » apparaît dans le menu principal de Windows 11, sans passer par
  « Afficher plus d'options ». C'est `PeseeMenu.dll` (dossier `menu\`), déclarée dans le manifeste du paquet.
  Windows 11 réserve ce menu aux applications empaquetées (MSIX) : c'est pour ça que le Store est nécessaire.
- **Signature** : le Store signe lui-même le paquet avec un certificat Microsoft. Smart App Control et
  SmartScreen ne bloquent donc plus l'app, sans acheter de certificat de signature de code.
- **Installation / désinstallation** : gérées par le Store. Pesee.exe ne propose plus de s'installer
  et n'écrit plus dans le registre quand il tourne depuis le paquet.
- La version classique (`build.ps1`, `install.ps1`) continue de fonctionner comme avant.

## 1. Compte développeur (à faire soi-même)

1. Aller sur https://partner.microsoft.com/dashboard et s'inscrire au programme « Applications et jeux ».
2. Choisir le type de compte : **particulier** (gratuit pour les particuliers depuis 2024, à vérifier à
   l'inscription) ou **entreprise** (payant, vérification de l'entreprise en plus).
   Le nom d'éditeur choisi s'affichera sur la fiche du Store.
3. Vérification d'identité : pièce d'identité demandée, compter de quelques heures à quelques jours.

## 2. Réserver le nom et récupérer l'identité

1. Partner Center > Applications et jeux > **Nouveau produit** > Application MSIX ou PWA.
2. Réserver le nom **Pesée** (s'il est pris : « Pesée - Taille des dossiers », par exemple).
3. Ouvrir **Gestion du produit > Identité du produit** et noter :
   - `Package/Identity/Name` (ex. `12345NomEditeur.Pesee`)
   - `Package/Identity/Publisher` (ex. `CN=1A2B3C4D-....`)
   - `Package/Properties/PublisherDisplayName`

## 3. Fabriquer le paquet final

```
powershell -NoProfile -ExecutionPolicy Bypass -File build-store.ps1 -IdentityName "<Name>" -Publisher "<Publisher>" -PublisherDisplayName "<PublisherDisplayName>"
```

Résultat : `store\out\Pesee_1.0.0.0.msixbundle` (x64 + ARM64). À chaque mise à jour, augmenter
`-Version` (1.0.1.0, 1.1.0.0... le dernier chiffre reste 0).

Avant d'envoyer : lancer le **Windows App Certification Kit** (menu Démarrer > « Windows App Cert Kit »)
sur le paquet. Ce sont les mêmes contrôles techniques que ceux du Store.

Résultat du 14/11/2026 (paquet x64) : **RÉUSSI**. Seul « Fichiers exécutables bloqués » est en échec, sans
bloquer la publication : Pesée lance des programmes (lui-même depuis le clic droit, l'Explorateur), ce qui
la rend indisponible sur les PC en **Windows mode S**. C'est inhérent à son fonctionnement.

Limite connue : le Store n'accepte pas le clic droit moderne sur l'icône d'un disque dans « Ce PC ».
« Analyser la taille » est proposé sur les dossiers et dans le vide d'un dossier, y compris à la racine
d'un disque ouvert.

## 4. Soumission

### Tarification et disponibilité
- Gratuit (ou payant, au choix), marchés : France et pays francophones, ou tous les marchés.
- L'app est en français uniquement (`fr-FR` dans le manifeste) : la fiche doit être rédigée en français.

### Propriétés
- Catégorie : **Utilitaires et outils**.
- Confidentialité : Pesée ne collecte ni n'envoie aucune donnée. Une URL de politique de confidentialité
  peut quand même être demandée (voir texte plus bas, à héberger sur une page simple).

### Classification par âge
- Questionnaire IARC : catégorie « Application / utilitaire », répondre « non » à tout → 3+.

### Packages
- Envoyer `Pesee_1.0.0.0.msixbundle`. Familles d'appareils : **Bureau** uniquement.

### Fonctionnalité restreinte `runFullTrust` (justification à coller)
> Pesée est une application de bureau (.NET Framework) qui parcourt les dossiers choisis par l'utilisateur
> pour calculer leur taille, et ajoute la commande « Analyser la taille » au menu contextuel de l'Explorateur
> de fichiers (extension windows.fileExplorerContextMenus). Elle a besoin d'un accès complet au système de
> fichiers en lecture. Elle ne modifie, ne supprime et n'envoie aucun fichier ni aucune donnée.

### Notes pour la certification (à coller)
> Pour tester : dans l'Explorateur, clic droit sur un dossier (ou dans le vide d'un dossier, ou sur un disque)
> > « Analyser la taille ». L'app peut aussi être lancée depuis le menu Démarrer : elle demande alors
> le dossier à analyser. Aucun compte ni connexion Internet n'est nécessaire.

### Fiche du Store

**Description**
> Pesée vous montre en un clic ce qui prend de la place sur votre PC.
>
> Clic droit sur un dossier ou un disque > « Analyser la taille » : une petite fenêtre liste les sous-dossiers
> et fichiers avec leur poids, et une pastille de couleur du vert (léger) au rouge (lourd). Les tailles
> s'affichent en direct pendant l'analyse, qui utilise tous les cœurs du processeur.
>
> Double-cliquez sur un dossier pour y entrer, triez par taille ou par nom, ouvrez un élément dans
> l'Explorateur ou copiez son chemin. Les éléments cachés suivent vos réglages de l'Explorateur.
>
> Pesée ne modifie et ne supprime rien. Aucune donnée n'est collectée ni envoyée.

**Fonctionnalités** (une par ligne)
- Clic droit « Analyser la taille » directement dans le menu de Windows 11
- Taille de chaque sous-dossier, calculée en parallèle et affichée en direct
- Pastille de couleur selon le poids relatif
- Tri par taille ou dans l'ordre de l'Explorateur
- Navigation au double-clic, retour au clavier ou à la souris
- Ne suit pas les liens symboliques ni les jonctions (pas de double comptage)

**Mots-clés** : taille dossier, espace disque, stockage, poids, nettoyage, explorateur

**Captures d'écran** : au moins une, 1366×768 minimum (PNG). Par exemple la fenêtre de Pesée triée par taille
sur un dossier bien rempli, et le clic droit de l'Explorateur montrant « Analyser la taille ».

### Politique de confidentialité (texte de la page à héberger)
> Pesée ne collecte, ne stocke et ne transmet aucune donnée personnelle. L'application lit uniquement les noms
> et tailles des fichiers des dossiers que vous choisissez d'analyser, sur votre PC, sans connexion Internet.
> En cas d'erreur inattendue, un fichier texte « Pesee-erreur.txt » est écrit dans votre dossier temporaire
> local ; il n'est jamais envoyé. Contact : <votre adresse>.

## 5. Après l'envoi

- La certification Microsoft prend en général de quelques heures à 3 jours ouvrés. On ne peut pas la forcer :
  on met toutes les chances de son côté (kit de certification, notes de test, justification de `runFullTrust`).
- En cas de refus, Partner Center donne le motif précis : corriger, augmenter la version, renvoyer.
- Astuce : publier d'abord en **visibilité masquée** (Disponibilité > « Masquer ce produit ») pour installer
  la version signée par le Store sur son propre PC, puis rendre l'app publique.
