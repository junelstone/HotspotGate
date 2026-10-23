# Stone's Hotspot — Manuel d'utilisation

Stone's Hotspot transforme un PC Windows en hotspot Wi-Fi payant : les clients se connectent au Wi-Fi, une page
(le « portail ») leur demande un code, un compte, ou un paiement Mobile Money, puis ils ont Internet pendant le
temps ou le volume acheté. Vous gérez tout depuis le **tableau de bord**, sur le PC ou sur votre téléphone.

Ce manuel est pour le gérant et les caissiers. Pour l'installation technique et les problèmes de réseau, voir aussi
le `README.md` (en anglais) ou contactez Stone's IT Consulting.

---

## 1. Ce qu'il faut

- Un PC Windows 10 ou 11 (64 bits), allumé pendant les heures d'ouverture.
- Deux connexions : celle qui apporte **Internet** (box, modem 4G, câble) et celle où se connectent les **clients**
  (un point d'accès Wi-Fi branché sur une deuxième prise réseau, ou le « Point d'accès sans fil mobile » de Windows).
- Le point d'accès doit être en mode **point d'accès / pont** (pas en mode routeur, DHCP éteint).
- Pour les tickets et reçus : une imprimante A4 (ou des PDF).

## 2. Installation et premier démarrage

1. Lancez `StonesHotspot-Setup.exe` et acceptez les droits administrateur. Rien d'autre à installer.
2. À la fin, le tableau de bord s'ouvre sur la **Première installation** (6 étapes) :
   1. **Langue** du tableau de bord et du portail (français ou anglais).
   2. **Nom du commerce** (affiché sur le portail et les reçus) et **nom du Wi-Fi**.
   3. **Mot de passe administrateur** du compte `admin` (8 caractères au moins). Notez-le et gardez-le en lieu sûr.
   4. **Internet et Wi-Fi clients** : choisissez la connexion Internet et celle des clients, puis **Partager
      Internet**. Le partage et le pare-feu sont réglés tout seuls. (« Plus tard » si le matériel n'est pas prêt :
      menu **Réseau**.)
   5. **Premiers forfaits** : ajustez les prix des trois forfaits de départ.
   6. **Terminer**, puis connectez-vous avec `admin` et votre mot de passe.
3. L'assistant ne s'ouvre que sur le PC lui-même. Sur un téléphone, il dit de terminer sur le PC.

Le service Stone's Hotspot démarre avec Windows et redémarre tout seul en cas de problème. L'icône
**Stone's Hotspot** près de l'horloge montre son état ; double-cliquez pour ouvrir le tableau de bord.

## 3. Ouvrir le tableau de bord

- Sur le PC : icône **Stone's Hotspot** du bureau, ou <http://localhost:8080/admin>.
- Sur un téléphone connecté au Wi-Fi du hotspot : <http://192.168.137.1:8080/admin>
  (ou en sécurisé <https://192.168.137.1/admin> ; le navigateur avertit une fois car le certificat est celui du PC).
- Depuis Internet, le tableau de bord n'est **pas** accessible (c'est voulu).
- Après 2 heures sans activité, il faut se reconnecter. Après 5 mauvais mots de passe, attendre 5 minutes.

Deux rôles :

| Rôle | Peut faire |
|---|---|
| **Caissier** | Accueil, Caisse, Vendre, Ma caisse, Comptes (chercher, recharger) |
| **Administrateur** | Tout : forfaits, tickets, produits, rapports, personnel, paramètres, réseau, licence, sauvegarde… |

## 4. La journée du caissier

(**Ma caisse**, **Caisse** et les produits font partie de l'édition Pro ; en Basic, on vend avec **Vendre**.)

### Ouvrir la caisse (**Ma caisse**)
Comptez l'argent du tiroir et saisissez les **espèces au départ**, puis **Ouvrir la caisse**. Toutes les ventes de la journée
s'ajoutent à votre caisse.

### Vendre un code Wi-Fi (**Vendre**)
Choisissez le forfait, le nombre de codes et le moyen de paiement (espèces, MTN MoMo, Airtel Money).
**Vendre** → le ticket s'affiche avec le code et un QR code : imprimez-le (A4, 3 par ligne, à découper) ou
lisez le code au client. Pour une carte déjà imprimée (lot de tickets), tapez son code dans **Vendre un ticket déjà imprimé**.

### Encaisser à la caisse (**Caisse**)
Ajoutez des produits (boissons, impressions…) et des codes Wi-Fi dans le même panier, saisissez la somme reçue :
la monnaie à rendre s'affiche. **Encaisser** → reçu A4 (imprimer ou PDF).

### Recharger un compte client (**Comptes**)
Ouvrez le compte du client (recherche par nom ou téléphone), **Recharger le compte**, saisissez le montant, **Encaisser la recharge**.

### Fermer la caisse (**Ma caisse**)
Comptez l'argent, saisissez les **espèces comptées**, puis **Fermer la caisse**. L'écart avec le montant attendu est affiché et enregistré.
Imprimez le récapitulatif si besoin.

## 5. Ce que voit le client

1. Il se connecte au Wi-Fi (nom choisi dans les paramètres). Son téléphone affiche « Se connecter au réseau » ;
   sinon il ouvre n'importe quel site, la page du hotspot apparaît.
2. Sur le portail, selon ce que vous avez activé :
   - **Code** : il tape le code du ticket (majuscules/minuscules et tirets sans importance).
   - **Compte** : nom d'utilisateur et mot de passe (clients réguliers avec un solde).
   - **Mobile Money** : il choisit un forfait, tape son numéro, valide le paiement sur son téléphone ; le code
     arrive tout seul.
   - **Accès gratuit**, **mot de passe du jour**, **code par SMS** (si activés).
3. Une page **Statut** montre le temps ou le volume restant, avec un bouton **Se déconnecter** (le temps restant
   reste sur le code pour plus tard, selon le forfait).
4. Quand le temps ou le volume est fini, Internet s'arrête et le portail revient.

Un code ne marche que sur un appareil à la fois (ou le nombre d'appareils prévu par le forfait). Après 5 codes
faux, l'appareil attend 5 minutes ; trop d'essais d'une même adresse sont aussi freinés.

## 6. Forfaits et tickets (administrateur)

- **Forfaits** : nom, prix (FCFA), type (**durée**, **volume** ou **illimité**), décompte (seulement pendant
  l'utilisation, ou en temps réel depuis la première connexion), validité, débit maximum, nombre d'appareils,
  déconnexion après inactivité. Un forfait déjà vendu ne se supprime pas : désactivez-le.
- **Tickets** : créez un lot de codes à l'avance (par ex. 100 codes « 1 heure »), imprimez les cartes A4 avec QR
  code ou exportez en CSV. Un lot ou un code peut être désactivé.

## 7. En direct (administrateur)

La liste des clients connectés : appareil, forfait, temps/volume restant, débit. Pour chacun : **ajouter du temps**,
**déconnecter**, **bloquer** l'appareil (plus jamais d'accès) ou le mettre en **liste blanche** (toujours Internet
sans code : PC de la caisse, imprimante…).

## 8. Produits, rapports, journal

- **Produits** : groupes, prix TTC avec TVA, stock (livraisons, inventaires). Le stock baisse à chaque vente.
- **Rapports** : chiffre d'affaires par jour, ventes par forfait, transactions, caisses, sessions, Mobile Money.
  Chaque rapport s'exporte en **CSV** (Excel) ou **PDF**.
- **Journal** : qui a fait quoi et quand (ventes, changements de prix, connexions du personnel…).

## 9. Accès gratuit, SMS et Mobile Money (administrateur)

- **Accès gratuit** : accès offert après acceptation des conditions (par ex. 15 minutes une fois par jour), ou un
  **mot de passe du jour** à donner aux clients (fixe, ou nouveau chaque jour, affiché sur l'accueil).
- **Code par SMS** : le client reçoit un code par SMS (modem USB ou fournisseur SMS). Limites par numéro, par
  appareil et par jour.
- **Mobile Money** : entrez les clés MTN MoMo et Airtel Money (fournies par les opérateurs), **Tester la
  connexion**, choisissez les forfaits vendus en ligne. Les paiements apparaissent dans **Mobile Money** et dans les
  rapports. Un paiement validé crée le code tout seul, même si le client a fermé la page.

## 10. Débit et filtrage (administrateur)

- **Débit** : partage équitable de la connexion entre les appareils actifs, débit minimum et maximum, test de
  vitesse de la connexion Internet.
- **Filtrage web** : bloquer des sites (par nom) ou des catégories ; filtre familial (DNS). Un site bloqué affiche
  une page « site bloqué ».

## 11. Personnel (administrateur)

Créez un compte par caissier (jamais de compte partagé). Le mot de passe donné est temporaire : il doit être changé
à la première connexion. Un compte désactivé est déconnecté tout de suite. Mot de passe oublié : un administrateur
le réinitialise dans **Personnel**.

**Plus aucun administrateur ne peut se connecter ?** Sur le PC, ouvrez une invite de commandes **en tant
qu'administrateur** et tapez :
`"C:\Program Files\Stone's Hotspot\StonesHotspot.Service.exe" --reset-admin-password`
Le nouveau mot de passe temporaire du compte `admin` est dans `C:\ProgramData\StonesHotspot\initial-admin-password.txt`.

## 12. Paramètres (administrateur)

Nom du commerce, nom du Wi-Fi, logo et couleur du portail, page d'accueil après connexion, conditions
d'utilisation (FR/EN), langue par défaut, déconnexion après inactivité, en-tête et pied des reçus, et
**Conserver les journaux de connexion** : après ce nombre de jours (365 par défaut, de 30 à 3650), les anciennes
connexions perdent ce qui identifie le client (adresse MAC, adresse IP, numéro de téléphone masqué). Les ventes et les
rapports restent. **Vérifiez la durée exigée par la loi** pour votre activité.

## 13. Sauvegardes

- Une **sauvegarde automatique** est faite chaque jour (les 14 dernières sont gardées) dans
  `C:\ProgramData\StonesHotspot\backups`.
- **Sauvegarde** → **Créer une sauvegarde et la télécharger** : copiez régulièrement une sauvegarde sur une clé USB ou un autre ordinateur
  (si le disque du PC tombe en panne, les sauvegardes du PC sont perdues aussi).
- **Restaurer** : remplace toutes les données par celles du fichier choisi ; la base actuelle est gardée en copie
  juste avant.

## 14. Licence

- Les 30 premiers jours sont un **essai** avec toutes les fonctions.
- Pour continuer : **Licence** → **Envoyer par WhatsApp** le **Machine ID** à Stone's IT Consulting, puis collez le
  code reçu (`SHS1-…`) et **Activer**. Le code ne marche que sur ce PC.
- Édition **Basic** : codes, comptes, forfaits, filtrage, débit, rapports de base. Édition **Pro** : en plus la
  caisse, les produits, les caisses du personnel, Mobile Money, SMS, accès gratuit, personnel supplémentaire.
- Avant la fin : rappels 30, 14, 7, 3 et 1 jour(s) avant (bandeau et icône). Après la fin : 7 jours de grâce, puis
  le hotspot s'arrête (les clients n'ont plus Internet). **Vos données ne sont jamais effacées** ; un nouveau code
  remet tout en marche aussitôt.
- Changement de PC : demandez un nouveau code pour le nouveau Machine ID.

## 15. Mises à jour

Quand une nouvelle version est publiée, un bandeau vert l'annonce aux administrateurs et l'icône Stone's Hotspot
propose **Installer la mise à jour**. Cliquez, acceptez la demande de droits administrateur : le hotspot s'arrête
environ une minute puis redémarre. Les données, paramètres et la licence sont gardés. Le fichier téléchargé n'est
lancé que s'il est bien celui publié par Stone's IT Consulting.

## 16. Bonnes pratiques de sécurité

- Un compte par personne, des mots de passe différents, jamais affichés au mur.
- Ne donnez le compte administrateur qu'au gérant.
- Gardez Windows à jour et un antivirus actif ; laissez le pare-feu Windows activé.
- Téléchargez une sauvegarde au moins une fois par semaine.
- Si un téléphone du personnel est perdu : changez son mot de passe dans **Personnel**.

## 17. Problèmes fréquents

| Problème | Que faire |
|---|---|
| Les clients ne voient pas la page du hotspot | Ouvrez **Réseau** : les deux connexions doivent être correctes et le partage actif. Le client peut ouvrir `http://192.168.137.1` |
| « Votre appareil n'a pas pu être identifié » | Le client éteint puis rallume le Wi-Fi de son téléphone |
| Les téléphones reçoivent une adresse 192.168.0.x ou 192.168.1.x | Le point d'accès est en mode routeur : passez-le en mode point d'accès/pont, DHCP éteint |
| Plus d'Internet pour personne | Vérifiez la connexion Internet du PC (box, crédit 4G). Le bandeau rouge « Service arrêté » = licence terminée |
| « Trop d'essais » | Attendre une minute |
| Un site bloqué s'ouvre encore | Le client éteint et rallume le Wi-Fi |
| Coupure de courant | Au retour du courant, Windows et le hotspot redémarrent seuls ; les sessions en cours reprennent |

Autres cas : `README.md`, section *Troubleshooting*, ou contactez **Stone's IT Consulting**.
