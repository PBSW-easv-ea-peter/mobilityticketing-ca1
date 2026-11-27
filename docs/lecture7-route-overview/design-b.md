# Design B – live-tavlen læses fra en summary-tabel

Fuld pil = write (command), stiplet pil = read (query). Tynd linje mellem tabeller = foreign key.

```mermaid
flowchart TB
  subgraph Liveboard
    direction TB
    subgraph LB_W["Commands (writes)"]
      RRC[RenameRouteCommand] --> RRH[RenameRouteHandler]
      CTC[CancelTripCommand] --> CTH[CancelTripHandler]
    end
    subgraph LB_R["Queries (reads)"]
      GLQ[GetLiveboardQuery] --> GLH[GetLiveBoardHandler]
    end
    LB_NOTE["Live-tavlen læses fra route_overview_summary<br/>(ingen beregning ved forespørgsel)"]
  end
  %% Review: CancelTrip mangler sin transaktion (kontrakten: LockTripAsync ved booking OG aflysning)

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
    PPT["Transaction (PublishPlan)<br/>1. Læs routes + trips (for vindue)<br/>2. Beregn værdier pr. rute<br/>3. Gem i published_plan_snapshot<br/>4. Commit<br/><b>Idempotency:</b> samme plan + vindue,<br/>ellers → SnapshotConflictException"]
  end
  %% Review: hvad sker der ved to samtidige publish med samme ID?

  subgraph Database
    direction LR
    routes["<b>routes</b><br/>id text PK<br/>short_name text"]
    trips["<b>trips</b><br/>id text PK<br/>route_id text FK<br/>scheduled_departure_utc timestamptz<br/>status text"]
    bookings["<b>bookings</b><br/>request_id text PK<br/>trip_id text FK<br/>booked_at_utc timestamptz"]
    summary["<b>route_overview_summary</b><br/>route_id text PK<br/>window_start_utc timestamptz PK<br/>window_end_utc timestamptz<br/>route_name text<br/>scheduled_trip_count integer<br/>next_departure_utc timestamptz"]
    snapshot["<b>published_plan_snapshot</b><br/>id text PK<br/>window_start_utc timestamptz<br/>window_end_utc timestamptz<br/>route_id text<br/>route_name text<br/>scheduled_trip_count integer<br/>next_departure_utc timestamptz"]
    trips --- |route_id| routes
    bookings --- |trip_id| trips
  end

  RRH -->|write| routes
  CTH -->|write| trips
  GLH -.->|read| summary
  %% Review: hvem skriver i route_overview_summary? Rename og Cancel skal vel også opdatere den?

  MBH -.->|read| trips
  MBH -->|write| bookings

  PPH -->|write| snapshot
  GPH -.->|read| snapshot
  %% Review: publish læser routes + trips (trin 1) – pil mangler

  classDef ctxLive fill:#ff5a5a22,stroke:#e05050
  classDef ctxBook fill:#ff66cc22,stroke:#e055b5
  classDef ctxPlan fill:#50c88a22,stroke:#3fae74
  classDef note fill:#f2f2f2,stroke:#bbb,color:#222
  classDef table fill:#fffaf2,stroke:#2ea6ff,color:#222
  class Liveboard ctxLive
  class Booking ctxBook
  class PublishedPlan ctxPlan
  class LB_NOTE,MBT,PPT note
  class routes,trips,bookings,summary,snapshot table
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
  %% Review: summary skal dække ethvert vindue ≤ 24 t i mikrosekunder – hvilke vinduer skal den indeholde?
  route_overview_summary {
    text route_id PK
    timestamptz window_start_utc PK
    timestamptz window_end_utc
    text route_name
    integer scheduled_trip_count
    timestamptz next_departure_utc
  }
  %% Review: id er PK, men der er én række pr. rute – så kan en plan kun have én rute?
  %% Review: published_at_utc mangler (kontrakten kræver vindue OG publiceringstidspunkt)
  published_plan_snapshot {
    text id PK
    timestamptz window_start_utc
    timestamptz window_end_utc
    text route_id
    text route_name
    integer scheduled_trip_count
    timestamptz next_departure_utc
  }
```

## Fordele
- Meget hurtig at læse (ingen beregning ved forespørgsel).
- Mindre belastning på databasen ved mange forespørgsler.
- Velegnet hvis det samme vindue vises ofte.

## Ulemper
- Flere ting at holde opdateret (trips + summary).
- Risiko for inkonsistens, hvis en handler glemmer at opdatere summary-tabellen.
- Understøtter primært ét tidsvindue (eller kræver flere rækker pr. vindue).
- Mere kompleksitet.
