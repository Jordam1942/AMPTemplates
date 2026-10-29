# Spaakspanning Meter

Mobile-first webapp voor het meten en corrigeren van fietsspaakspanning met een
Unior Spoke Tension Meter (1752/2 en 1752/6). Eén bestand (`index.html`),
Tailwind CSS via CDN, vanilla JavaScript, geen build-stap.

## Hosten op Cloudflare Pages

1. Cloudflare Dashboard → Workers & Pages → Create → Pages → Connect to Git.
2. Kies deze repository.
3. Build settings: **Framework preset: None**, **Build command: leeg**,
   **Build output directory: `spaakspanning`**.
4. Deploy. De app staat direct online; er is geen backend.

Alternatief: sleep de map `spaakspanning` in "Direct Upload".

## Gebruik

1. Nieuw wiel → kies voor/achter, spaakaantal en spaaktype.
2. Begin bij de spaak naast het ventiel, werk met de klok mee, tik de meterstand in.
3. Resultaat: balansscore, lijst "wat moet je doen" en ronde spaakgrafiek.
4. Print Klantrapport → neutraal A4-rapport (of "Opslaan als PDF").

## Kalibratie (aanbevolen)

De ingebouwde omrekening meterstand → kgf is indicatief. Het bijstel-advies
werkt op de onderlinge verhouding tussen de spaken en is daar niet van
afhankelijk. Voor exacte kgf-waarden: Instellingen → Kalibratie en neem per
spaaktype twee of meer regels over uit de omrekentabel die bij de meter zit.

Alle gegevens worden lokaal opgeslagen (localStorage); een meting gaat niet
verloren als de pagina sluit.
