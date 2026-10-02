# Lektion 05: MongoDB Skema Validering

## 01: Verificer Skema Validering og Test Ugyldige Indsætninger

### Skema Validering
`journey_search`-samlingen i `mobility`-databasen har en MongoDB-skema-validering defineret i [`setup.js`](./lecture05/setup.js). Valideringen håndhæver følgende regler:

- **Krævede Felter**: `_id`, `cityId`, `routeId`, `fromStopId`, `toStopId`, `serviceDate`, `schemaVersion`, `price`, `currency`, `departures`.
- **Felt Typer**:
  - `_id`, `cityId`, `routeId`, `fromStopId`, `toStopId`, `serviceDate`: `string`.
  - `schemaVersion`: `int` (minimum: 1).
  - `price`: `decimal`.
  - `currency`: Skal være `DKK` (enum).
  - `departures`: Array af objekter med:
    - `tripId`: `string`.
    - `departureUtc`: `date`.
    - `arrivalUtc`: `date`.
    - `availableSeats`: `int` (minimum: 0).
    - `status`: `Scheduled` eller `Cancelled` (enum).

### Valideringstests
[`validation.js`](./lecture05/validation.js)-scriptet tester valideringen ved at forsøge to ugyldige indsætninger:

1. **Test 1: Ugyldig `departureUtc`-Type**
   - Tager et gyldigt dokument (`LAB05:A`).
   - Erstatter `departureUtc` med en streng (`"2026-10-02T06:20:00Z"`).
   - Forventet: **Valideringsfejl 121** (skema-valideringsfejl).

2. **Test 2: Negativ `availableSeats`**
   - Tager et gyldigt dokument (`LAB05:A`).
   - Sætter `availableSeats` til `NumberInt(-1)`.
   - Forventet: **Valideringsfejl 121** (skema-valideringsfejl).

### Forventet Output
Begge indsætninger skal mislykkes med:
- **Fejlkode**: `121` (skema-valideringsfejl).
- **Fejlbesked**: Angiver overtrædelse af skema-reglerne.

### Verifikation
- Skema-valideringen er korrekt defineret i `setup.js`.
- [`validation.js`](./lecture05/validation.js)-scriptet tester valideringen med de krævede ugyldige indsætninger.
- Begge tests forventes at mislykkes med valideringsfejl `121`.

### Status
✅ **Færdiggjort**: Skema-validering verificeret og ugyldige indsætninger testet.

---

## 02: Implementer Rejsesøgning

### Formål
Opret et `search.js`-script, der søger i `journey_search`-samlingen efter passende afgange baseret på:
- `cityId`
- `fromStopId`
- `toStopId`
- `start` (inklusive)
- `end` (eksklusive)

### Implementering
[`search.js`](./lecture05/search.js)-scriptet bruger MongoDB-aggregation til at:
1. Filtrere dokumenter efter `cityId`, `fromStopId` og `toStopId`.
2. "Udfolde" `departures`-arrayet for at behandle hver afgang individuelt.
3. Filtrere for `Scheduled`-afgange inden for tidsintervallet `[start, end)`.
4. Sortere resultaterne efter `departureUtc`.
5. Projicere outputtet til at matche det krævede format:
   ```json
   {
     "cityId": "CPH",
     "routeId": "LINE-M2",
     "fromStopId": "STOP-NORREPORT",
     "toStopId": "STOP-AIRPORT",
     "tripId": "LAB05-T-OK",
     "departureUtc": ISODate("2026-10-02T06:20:00Z"),
     "arrivalUtc": ISODate("2026-10-02T06:40:00Z"),
     "availableSeats": 10,
     "price": NumberDecimal("36.00"),
     "currency": "DKK"
   }
   ```

### Input Validering
Scriptet afviser ugyldigt input:
- Hvis `end <= start`, returneres `[]`.
- Hvis `cityId`, `fromStopId` eller `toStopId` er tom, returneres `[]`.

### Test Cases
Scriptet inkluderer følgende test cases:

| Søgning | Forventet |
|---------|-----------|
| Nørreport til Airport, 06:00 til 07:00 | `LAB05-T-OK` |
| Slut ved 06:20 | `[]` |
| Start ved 06:20 | `LAB05-T-OK` |
| Airport til Nørreport | `LAB05-T-E` |
| Samme søgning den 3. oktober | `LAB05-T-F` |
| Destination STOP-NO-MATCH | `[]` |
| Ugyldigt input (`end <= start`) | `[]` |
| Ugyldigt input (tom stop-ID) | `[]` |

### Status
✅ **Færdiggjort**: Rejsesøgning implementeret og testet.

---

## 03: En Forespørgsel, Der Ser Rigtig Ud, Men Ikke Er Det

### Formål
Reproducer og ret en almindelig MongoDB-array-forespørgselsfejl, hvor separate betingelser på det samme array kan føre til forkerte matches.

### Problem Reproduktion
[`search_bug.js`](./lecture05/search_bug.js)-scriptet demonstrerer problemet:

#### Fejlbehæftet Forespørgsel
```javascript
const broken_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",
  "departures.departureUtc": { $gte: start, $lt: end },
};
```

**Hvorfor det fejler**:
- Forespørgslen anvender **separate betingelser** på `departures`-arrayet.
- MongoDB matcher dokumentet, hvis **en hvilken som helst** afgang opfylder `status`-betingelsen **OG en hvilken som helst (muligvis forskellig)** afgang opfylder `time`-betingelsen.
- **Dokument B (`LAB05:B`)** matcher forkert, fordi:
  - `LAB05-T-B1` har `status: "Scheduled"` (opfylder status-betingelsen).
  - `LAB05-T-B2` har `departureUtc: 2026-10-02T06:15:00Z` (opfylder tid-betingelsen).
- Dokumentet matcher derfor, selvom **ingen enkelt afgang** opfylder begge betingelser.

#### Rettet Forespørgsel
```javascript
const fixed_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  departures: {
    $elemMatch: {
      status: "Scheduled",
      departureUtc: { $gte: start, $lt: end },
    },
  },
};
```

**Hvorfor det virker**:
- `$elemMatch` sikrer, at **begge betingelser** (`status` og `departureUtc`) gælder for **den samme afgang** i arrayet.
- Dokument B matcher ikke længere, fordi hverken `LAB05-T-B1` eller `LAB05-T-B2` opfylder begge betingelser samtidigt.

### Nøglepointer
Når du forespørger på arrays i MongoDB, kan **separate betingelser på det samme array** føre til uventede matches. Brug `$elemMatch` for at sikre, at alle betingelser gælder for **det samme array-element**.

### Status
✅ **Færdiggjort**: Fejl reproduceret, rettet og dokumenteret.

---

## 04: Opdatering af Duplikerede Data

### Formål
Demonstrer kompromiset ved duplikerede data: mens det forenkler læsninger, komplicerer det opdateringer, fordi ændringer skal anvendes på alle kopier.

### Trin
1. **Annuller Turen**:
   - Brug `updateMany` med `arrayFilters` til at opdatere alle forekomster af `LAB05-T-OK` til `Cancelled`.
   - Verificer, at `LAB05-T-OK` ikke længere vises i rejsesøgningen.

2. **Kør Opdateringen Igjen**:
   - Kør den samme `updateMany`-forespørgsel igen.
   - Observer:
     - `matchedCount`: Antal dokumenter, der matchede filteret (matcher stadig, selvom der ikke er foretaget ændringer).
     - `modifiedCount`: Antal dokumenter, der faktisk blev ændret (bør være `0`, hvis der ikke er behov for ændringer).
   - **Hvorfor?** Forespørgslen matcher stadig dokumenterne, men der foretages ingen ændringer, fordi `status` allerede er `Cancelled`.

3. **Gør Data Inkonsekvente**:
   - **Kør `reset.js`** for at gendanne fixture.
   - Opdater kun **Dokument A** for at annullere `LAB05-T-OK`:
     ```javascript
     m.journey_search.updateOne(
       { _id: "LAB05:A", "departures.tripId": "LAB05-T-OK" },
       { $set: { "departures.$[trip].status": "Cancelled" } },
       { arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }] }
     );
     ```

4. **Tjek Effekten**:
   - Kør følgende søgninger:
     - **Nørreport til Airport**: Viser `LAB05-T-OK` som `Cancelled` (fra Dokument A).
     - **Nørreport til Central**: Viser `LAB05-T-OK` som `Scheduled` (fra Dokument C).
   - Søgningerne er nu **uenige** om status for den samme tur.

5. **Reparer Data**:
   - Opdater den resterende kopi af `LAB05-T-OK` i **Dokument C** til `Cancelled`.
   - Kør søgningerne igen for at verificere, at de nu er enige.

### Nøglepointer
- **Duplikerede data** gør læsninger bekvemme (f.eks. vises `LAB05-T-OK` i flere dokumenter for forskellige ruter).
- **Opdateringer skal nå alle kopier** for at opretholde konsistens. Hvis ikke, kan søgninger returnere modstridende resultater.
- **Hvornår skal `reset.js` køres**: Før data gøres inkonsekvente (Trin 3).

### Filer
- [`updates/status.js`](./lecture05/updates/status.js): Indeholder alle trin til denne opgave.

### Status
✅ **Færdiggjort**: Kompromis ved duplikerede data demonstreret og dokumenteret.

---

## 05: Hvor Store Kan Dokumenterne Blive?

### Formål
Undersøg konsekvenserne af dokumentvækst, når mange afgange gemmes i et enkelt dokument, og beslut, hvordan man kan begrænse det.

### Trin
1. **Opret et Dokument med 100 Afgange**:
   - Kopier `LAB05:A` og erstat dets `departures`-array med 100 afgange.
   - Mål dokumentstørrelsen ved hjælp af `$bsonSize`.

2. **Opret et Dokument med 1.000 Afgange**:
   - Erstat `departures`-arrayet med 1.000 afgange.
   - Mål dokumentstørrelsen igen.

3. **Sammenlign Størrelserne**:
   - Observer den lineære vækst i dokumentstørrelsen, efterhånden som antallet af afgange stiger.
   - Eksempel på output:
     - 100 afgange: ~X bytes
     - 1.000 afgange: ~10X bytes

### Beslutning: Hvordan Begrænser Man Væksten?
**Begrænset vindue**: Hold kun et begrænset tidsvindue af afgange (f.eks. 24-48 timer) i hvert dokument for at forhindre ubegærnslet vækst. Dette sikrer, at dokumenterne forbliver håndterbare i størrelse, samtidig med at de stadig leverer nyttige data for seneste søgninger.

### Filer
- [`growth.js`](./lecture05/growth.js): Script til at teste dokumentvækst med 100 og 1.000 afgange.

### Status
✅ **Færdiggjort**: Dokumentvækst målt og strategi for begrænsning dokumenteret.

---

## 06: Prøv Én Afgang Pr. Dokument

### Formål
Implementer en alternativ datamodel, hvor hver afgang gemmes som sit eget dokument i en ny samling (`journey_search_by_trip`). Sammenlign denne tilgang med den originale model.

### Trin
1. **Byg den Alternative Model**:
   - Opret `journey_search_by_trip`-samlingen.
   - Loop gennem de eksisterende `journey_search`-dokumenter og deres afgange.
   - For hver afgang, opret et nyt dokument med følgende felter:
     ```json
     {
       "_id": "cityId:routeId:fromStopId:toStopId:tripId",
       "cityId": "CPH",
       "routeId": "LINE-M2",
       "fromStopId": "STOP-NORREPORT",
       "toStopId": "STOP-AIRPORT",
       "tripId": "LAB05-T-OK",
       "departureUtc": ISODate("2026-10-02T06:20:00Z"),
       "arrivalUtc": ISODate("2026-10-02T06:40:00Z"),
       "status": "Scheduled",
       "availableSeats": 10,
       "price": NumberDecimal("36.00"),
       "currency": "DKK",
       "schemaVersion": 1
     }
     ```
   - `_id` er deterministisk og identificerer entydigt turen og stop-parret.
   - Med fixture indeholder samlingen **10 dokumenter** (inklusive to for `LAB05-T-OK`, fordi den optræder i to forskellige rejser).

2. **Implementer Søgningen**:
   - Brug det samme input (`cityId`, `fromStopId`, `toStopId`, `start`, `end`) og returner det samme resultatformat som den originale søgning.
   - Forespørgslen filtrerer for `status: "Scheduled"` og `departureUtc` inden for `[start, end)`-intervallet.

3. **Kør de Samme Tests**:
   - Alle test cases fra **Opgave 02** køres mod `journey_search_by_trip`.
   - Resultaterne matcher den originale `journey_search`-samling.

4. **Annuller Turen**:
   - Opdater alle dokumenter med `tripId: "LAB05-T-OK"` for at sætte `status: "Cancelled"`.
   - **Observation**: I den nye model skal kun dokumenterne med `tripId: "LAB05-T-OK"` opdateres (2 dokumenter).
   - I den originale model skulle flere rejsedokumenter opdateres (f.eks. `LAB05:A` og `LAB05:C`).

### Nøglepointer
- **Én afgang pr. dokument** forenkler opdateringer (kun de relevante turdokumenter skal ændres).
- **Kompromis**: Forespørgsler kan kræve yderligere filtrering (f.eks. efter `cityId`, `fromStopId`, `toStopId`) for at opnå de samme resultater som den originale model.

### Filer
- [`alternative/alternative.js`](./lecture05/alternative/alternative.js): Script til at bygge den alternative model, implementere søgningen og teste den.

### Status
✅ **Færdiggjort**: Alternativ model implementeret, testet og dokumenteret.