# Spaakspanning Werkplaats

Single-page werkplaats-app voor het meten, balanceren en certificeren van
spaakspanning met de Unior Spoke Tension Meter (1752/2 en 1752/6).
Eén bestand (`index.html`): Tailwind CSS via CDN, vanilla JavaScript, geen build.

## Functies

- Klantenbestand (naam, telefoon, e-mail, fiets, framenummer) met zoeken en wielhistorie per klant.
- Meetwizard, zero-typing meetscherm (snelkeuze + numpad, auto-advance, geluid/trilling), toetsenbordbediening op desktop.
- Diagnose: kgf per spaak, balans per zijde, actielijst op prioriteit, notities over zijdelingse slag en rondheid, wielscore 0–100.
- Nameten van alleen de bijgestelde spaken; nulmeting vs. eindmeting.
- A4-certificaat (1 pagina) met klant- en fietsgegevens, radiale grafiek, uitleg en handtekeningveld.
- Opslag in localStorage; optionele Google Drive-synchronisatie (OAuth Client ID, scope `drive.file`); JSON export/import.

## Hosten op Cloudflare Pages

Git: Build command leeg, Build output directory `spaakspanning`.
Direct upload: upload `spaakspanning.zip` uit de root van deze repo als nieuwe deployment.

## Google Drive-sync instellen (eenmalig)

1. console.cloud.google.com → project aanmaken → Google Drive API inschakelen.
2. OAuth consent screen (External) invullen, jezelf als testgebruiker toevoegen.
3. Credentials → OAuth client ID → Web application → Authorized JavaScript origin = het adres van de app.
4. Client ID plakken in Instellingen → Verbinden.
