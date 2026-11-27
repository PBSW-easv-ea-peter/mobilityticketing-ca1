# Design A – live-tavlen beregnes, når nogen spørger

Fuld pil = write (command), stiplet pil = read (query). Tynd linje mellem tabeller = foreign key.

```mermaid
flowchart TB
  subgraph Liveboard
    direction TB
    subgraph LB_W["Commands (writes)"]
      CTC[CancelTripCommand] --> CTH[CancelTripHandler]
    end
    subgraph LB_R["Queries (reads)"]
      GLQ[GetLiveboardQuery] --> GLH[GetLiveBoardHandler]
    end
    LB_NOTE["Beregner live-tavlen ved forespørgsel<br/>ved at læse routes + trips (for givet tidsvindue)"]
  end
  %% Review: RenameRouteCommand mangler i Design A (findes i B)

  subgraph Booking
    direction TB
    subgraph BK_W["Commands (writes)"]
      MBC[MakeBookingCommand] --> MBH[MakeBookingHandler]
    end
    MBT["Transaction (MakeBooking)<br/>1. Tjek request_id (idempotency)<br/>2. Lås tur (SELECT … FOR UPDATE)<br/>3. Tjek trips.status = 'Scheduled'<br/>4. Opret booking"]
  end
  %% Review: hvad hvis to requests med samme request_id kommer samtidig? Hvor tjekkes RequestConflict?

  subgraph PublishedPlan
    direction TB
    subgraph PP_R["Queries (reads)"]
      GPQ[GetPublishedPlanQuery] --> GPH[GetPublishedPlanHandler]
    end
    subgraph PP_W["Commands (writes)"]
      PPC[PublishPlanCommand] --> PPH[PublishPlanHandler]
    end
    PPT["Transaction (PublishPlan)<br/>1. Læs routes + trips (for vindue)<br/>2. Beregn værdier pr. rute<br/>3. Gem i published_plan_snapshot"]
  end
  %% Review: idempotency (samme ID + vindue) og SnapshotConflictException mangler her (findes i B)
  %% Review: CancelTrip mangler sin transaktion (kontrakten: LockTripAsync ved booking OG aflysning)

  subgraph Database
    direction LR
    routes["<b>routes</b><br/>id text PK<br/>short_name text"]
    trips["<b>trips</b><br/>id text PK<br/>route_id text FK<br/>scheduled_departure_utc timestamptz<br/>status text"]
    bookings["<b>bookings</b><br/>request_id text PK<br/>trip_id text FK<br/>booked_at_utc timestamptz"]
    snapshot["<b>published_plan_snapshot</b><br/>id text<br/>published_at_utc timestamptz<br/>route_id text<br/>route_name text<br/>scheduled_trip_count integer<br/>next_departure_utc timestamptz"]
    trips --- |route_id| routes
    bookings --- |trip_id| trips
  end

  CTH -->|write| trips
  GLH -.->|read| trips
  %% Review: live-tavlen læser også routes (navn + ruter uden afgange) – pil mangler

  MBH -->|write| trips
  MBH -.->|read| bookings
  %% Review: i Canva peger booking-write på trips og read på bookings – booking skriver vel i bookings og læser/låser trips?

  PPH -->|write| trips
  GPH -.->|read| snapshot
  %% Review: i Canva peger publish-write på trips – publish læser routes + trips og skriver snapshot?

  classDef ctxLive fill:#ff5a5a22,stroke:#e05050
  classDef ctxBook fill:#ff66cc22,stroke:#e055b5
  classDef ctxPlan fill:#50c88a22,stroke:#3fae74
  classDef note fill:#f2f2f2,stroke:#bbb,color:#222
  classDef table fill:#fffaf2,stroke:#2ea6ff,color:#222
  class Liveboard ctxLive
  class Booking ctxBook
  class PublishedPlan ctxPlan
  class LB_NOTE,MBT,PPT note
  class routes,trips,bookings,snapshot table
```

## Tabeller

```mermaid
erDiagram
  routes ||--o{ trips : "route_id"
  trips ||--o{ bookings : "trip_id"

  routes {
    text id PK
    text short_name
  }
  trips {
    text id PK
    text route_id FK
    timestamptz scheduled_departure_utc
    text status
  }
  bookings {
    text request_id PK
    text trip_id FK
    timestamptz booked_at_utc
  }
  %% Review: published_plan_snapshot har ingen PK – hvad identificerer én række (én pr. rute)?
  %% Review: vinduet (from/to) mangler – nødvendigt for PublishedSnapshot.Window og SnapshotConflictException
  published_plan_snapshot {
    text id
    timestamptz published_at_utc
    text route_id
    text route_name
    integer scheduled_trip_count
    timestamptz next_departure_utc
  }
```

## Fordele
- Altid korrekt og enkel.
- Ingen ekstra tabeller at holde opdateret.
- Understøtter alle tidsvinduer.

## Ulemper
- En database-forespørgsel hver gang tavlen vises.
- Kan være langsommere ved høj belastning.
