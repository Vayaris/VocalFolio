# VocalFolio

**Capturez vos réunions. Retrouvez chaque voix. Gardez vos données en local.**

Application Windows de capture micro + son du PC, transcription locale et distinction des intervenants. Interface française, mode portable, aucun compte ni service de transcription distant.

## Télécharger

Depuis [Releases](https://github.com/Vayaris/VocalFolio/releases), télécharger `VocalFolio.exe`. Windows 11 x64. L’EXE contient .NET et le moteur de transcription CPU ; il peut être déplacé seul. Les modèles sont téléchargés ou importés séparément.

Le premier lancement peut récupérer les bibliothèques Microsoft manquantes depuis la distribution officielle NVIDIA, sans installateur ni élévation. Cette dépendance est vérifiée par empreinte. Internet est nécessaire pour les composants absents ; une fois installés, la transcription fonctionne hors ligne.

## Utilisation

1. Choisir le microphone et la sortie audio réellement utilisée par Teams, Zoom ou le navigateur. Tester les niveaux.
2. Informer les participants et obtenir les autorisations requises. Cocher la confirmation avant chaque enregistrement.
3. Enregistrer, mettre en pause si besoin, puis **Terminer et transcrire**.
4. Suivre le pourcentage et le temps restant estimé. Renommer les voix et corriger la transcription.
5. Réécouter un passage sur la sortie d’écoute choisie ; ouvrir le dossier audio ou exporter TXT/SRT.

La confirmation dans l’application est votre déclaration : elle ne recueille pas le consentement des autres participants et ne rend pas un enregistrement automatiquement légal. Lire [Usage responsable](LEGAL.md) et [Confidentialité](PRIVACY.md).

## Modèles

| Profil | Usage | Téléchargement |
|---|---|---|
| Nemotron ASR 0.6B | Rapide, français/anglais et autres langues ; progression sur l’audio traité | 742 Mo + Sortformer 147 Mo |
| Parakeet TDT 0.6B v3 | Alternative multilingue ; progression estimée après calibration | 714 Mo + le même Sortformer |
| Nemotron 3.5 Transcribe 30B | Profil avancé affiché, non activable faute de distribution validée | Indisponible |

Les paramètres permettent le téléchargement avec reprise, l’import de GGUF Q8 officiels, le choix actif et l’ouverture du dossier des modèles. Les révisions et empreintes sont épinglées dans [model-manifest.json](model-manifest.json). Le choix précédent est conservé en cas d’échec. Aucun GPU NVIDIA requis pour les profils compacts ; CPU fourni, CUDA avancé non testé.

## Stockage et portabilité

Les préférences, modèles, moteurs et réunions sont dans `%LOCALAPPDATA%\ReunionLocale`. Ce nom interne est conservé pour la compatibilité avec les prototypes précédents. Les anciens modèles trouvés à proximité de l’ancienne distribution ou via les paramètres sont réutilisés après vérification, sans suppression des originaux.

Portable signifie « sans installation » : les données restent dans le profil Windows et ne suivent pas automatiquement une clé USB. Le runtime .NET utilise aussi son cache d’extraction standard Windows.

## Limites

- Le micro est étiqueté « Moi » ; Sortformer distingue jusqu’à quatre voix sur la piste PC. Les noms sont attribués manuellement. La cible testée comprend des extraits jusqu’à trois voix ; une réunion réelle de deux heures à quatre personnes n’a pas été validée.
- Horodatages, transcription et attribution peuvent être inexacts, notamment avec des voix simultanées. Vérifier avant utilisation.
- Le temps restant est une estimation. 100 % est réservé à la sauvegarde réussie.
- Audio en WAV mono 16 bits : environ 0,69 Go par piste pour deux heures à 48 kHz ; limite WAV classique d’environ 4 Go par piste.
- Décocher la conservation des audios supprime les WAV après réussite et empêche réécoute/nouvelle analyse. Les pistes sont conservées après erreur ou annulation.
- La sortie sélectionnée est capturée dans son ensemble, y compris notifications et autres applications. Un casque réduit les reprises par le micro.
- Pas de résumé automatique, reconnaissance d’identité entre réunions ou chiffrement intégré. Bluetooth en changement de profil et CUDA non validés de bout en bout.

## Compiler et vérifier

SDK .NET 10 x64 : `./build.ps1` publie `portable/VocalFolio.exe`. Le projet garde son nom interne `src/ReunionLocale.csproj`. Les ressources du moteur embarqué sont dans `src/Assets` et leurs empreintes sont vérifiées à l’extraction.

`./verify.ps1` télécharge des extraits publics et les modèles de test manquants (environ 1,6 Go au total), puis exécute les contrôles. `-Playback` ajoute une lecture réelle ; `-Capture` ajoute une courte capture locale des périphériques par défaut. Aucun audio personnel n’est fourni avec le dépôt.

Les prototypes ont passé 25 contrôles, dont extraction/réparation, migration, intégrité et reprise des téléchargements, pipeline natif, Parakeet et lecture WASAPI avec capture du signal. Voir [VALIDATION.md](VALIDATION.md).

## Licences et attribution

Code VocalFolio : [MIT](LICENSE), © 2026 Vayaris. Les composants et modèles tiers gardent leurs propres licences ; voir [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) et `licenses/`.

Projet indépendant, sans affiliation ni approbation de NVIDIA ou Microsoft. Leurs noms servent à identifier les composants utilisés. Le logiciel est fourni en l’état ; les limites légales impératives restent applicables.
