# Lektion 05: MongoDB Øvelsesvejledning

## Opgavevejledning

### Opgave 01: Verificer Skema Validering
- **Script**: `validation.js`
- **Formål**: Test ugyldige indsætninger (`departureUtc` som streng, `availableSeats` som negativ).
- **Kør**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/validation.js
  ```
- **Forventet**: Begge indsætninger fejler med **valideringsfejl 121**.

---

### Opgave 02: Implementer Rejsesøgning
- **Script**: `search.js`
- **Formål**: Søg efter afgange, der matcher `cityId`, `fromStopId`, `toStopId`, `start` og `end`.
- **Kør**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/search.js
  ```
- **Forventet**: Alle test cases bestås (f.eks. `LAB05-T-OK` for Nørreport → Airport, 06:00–07:00).

---

### Opgave 03: Fejlsøg Array Forespørgsel
- **Script**: `search_bug.js`
- **Formål**: Reproducer og ret en fejl, hvor separate betingelser på et array forårsager forkerte matches.
- **Kør**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/search_bug.js
  ```
- **Forventet**:
  - Fejlbehæftet forespørgsel: Dokument B (`LAB05:B`) matcher forkert.
  - Rettet forespørgsel: Dokument B matcher ikke længere.

---

### Opgave 04: Opdater Duplikerede Data
- **Script**: `updates/status.js`
- **Formål**: Opdater `LAB05-T-OK` til `Cancelled` i alle dokumenter, og test derefter for inkonsekvenser.
- **Trin**:
  1. Kør scriptet for at annullere `LAB05-T-OK` overalt:
     ```bash
     docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/updates/status.js
     ```
  2. **Nulstil databasen** (til test af inkonsekvens):
     ```bash
     docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/reset.js
     ```
  3. Opdater manuelt **kun Dokument A** (fjern kommentaren for `updateOne` i scriptet eller kør det separat).
  4. Kør scriptet igen for at se inkonsekvensen (Nørreport → Airport vs. Nørreport → Central).
  5. Reparer ved at opdatere den resterende kopi af `LAB05-T-OK`.

---

### Opgave 05: Dokumentvækst
- **Script**: `growth.js`
- **Formål**: Mål dokumentstørrelsen med 100 og 1.000 afgange.
- **Kør**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/growth.js
  ```
- **Forventet**:
  - 100 afgange: ~X bytes.
  - 1.000 afgange: ~10X bytes (lineær vækst).
- **Beslutning**: Brug et **begrænset tidsvindue** (f.eks. 24–48 timer) for at begrænse dokumentstørrelsen.

---

### Opgave 06: Én Afgang Pr. Dokument
- **Script**: `alternative/alternative.js`
- **Formål**: Opret `journey_search_by_trip`-samlingen og implementer den samme søgning.
- **Kør**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/alternative/alternative.js
  ```
- **Forventet**:
  - 10 dokumenter indsat i `journey_search_by_trip`.
  - Alle test cases fra Opgave 02 bestås.
  - Annullering af `LAB05-T-OK` opdaterer **kun 2 dokumenter** (modsat flere i den originale model).
