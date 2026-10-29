# Confidentialité

- L’audio et la transcription sont traités localement. VocalFolio ne les envoie pas à un service distant et n’intègre pas de télémétrie.
- Les téléchargements de modèles contactent Hugging Face et ses hébergeurs ; les dépendances manquantes contactent GitHub/NVIDIA. Ces hébergeurs reçoivent les informations réseau normales des requêtes, notamment l’adresse IP. Aucune piste de réunion n’est jointe à ces requêtes.
- Le moteur embarqué utilise des processus enfants locaux, sans serveur HTTP de transcription.
- Les données sont dans le profil Windows, sous `%LOCALAPPDATA%\ReunionLocale`, ou le dossier personnalisé choisi. Les WAV, JSON, exports et journaux peuvent contenir des informations sensibles.
- Les fichiers ne sont pas chiffrés par l’application. Utilisez les protections Windows et une politique d’accès, de sauvegarde et de conservation adaptée.
- Un dossier synchronisé (OneDrive, sauvegarde réseau, etc.) peut être transmis par ce service indépendamment de VocalFolio.
- L’utilisateur contrôle conservation et suppression dans l’historique. La suppression n’efface pas les exports, copies, sauvegardes ou fichiers déjà synchronisés et n’est pas un effacement sécurisé.
- L’application ne recueille pas le consentement des autres participants à votre place. Voir [LEGAL.md](LEGAL.md).
