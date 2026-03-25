# US Bank System

Aplikacja webowa symulująca działanie amerykańskiego banku detalicznego. Projekt grupowy — moduł **Bank B (USA)**.

## Zakres

- Zarządzanie kontami (checking / savings)
- Przelewy wewnętrzne i zewnętrzne
- Integracja z ACH, RTP, FedNow, SWIFT
- Obsługa kart płatniczych
- BLIK-USD — mobilne płatności w dolarach
- Panel zarządzania kontem

## Stack

| Warstwa | Technologia |
|---|---|
| Backend | C# ASP.NET Core 8 Web API |
| Frontend | React + Vite + TypeScript |
| Baza danych | PostgreSQL 16 |
| ORM | Entity Framework Core 8 |
| API Docs | Swagger / OpenAPI (Swashbuckle) |
| Auth | JWT Bearer Tokens |
| Konteneryzacja | Docker + Docker Compose |

---

## Uruchomienie

### Wymagania

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (lub Docker Engine + Compose plugin)
- [Git](https://git-scm.com/)

### Krok 1 — Klonowanie repo

```bash
git clone https://github.com/g0rzki/us-bank-system.git
cd us-bank-system
```

### Krok 2 — Konfiguracja zmiennych środowiskowych

Skopiuj szablon i uzupełnij swoimi danymi:

```bash
cp .env.example .env
```

Otwórz `.env` i uzupełnij:

```env
POSTGRES_DB=usbank          # nazwa bazy — zostaw bez zmian
POSTGRES_USER=twoj_user     # dowolna nazwa użytkownika bazy
POSTGRES_PASSWORD=twoje_haslo
POSTGRES_PORT=5433          # port na hoście (5433 jeśli lokalny postgres zajmuje 5432)
JWT_SECRET=min_32_znaki     # dowolny ciąg min. 32 znaków
INTEGRATIONS_ACH_URL=http://localhost:6001
INTEGRATIONS_SWIFT_URL=http://localhost:6002
INTEGRATIONS_CARDS_URL=http://localhost:6003
INTEGRATIONS_BLIK_URL=http://localhost:6004
```

> Plik `.env` jest wykluczony z gita — nie commituj go.

### Krok 3 — Konfiguracja Ridera

Skopiuj szablon `launchSettings.json`:

```bash
cp src/UsBankSystem.Api/Properties/launchSettings.template.json src/UsBankSystem.Api/Properties/launchSettings.json
```

Otwórz `launchSettings.json` i uzupełnij wartości w profilu `http` danymi z `.env`:

```json
"ConnectionStrings__Default": "Host=localhost;Port=5433;Database=usbank;Username=POSTGRES_USER;Password=POSTGRES_PASSWORD",
"Jwt__Secret": "JWT_SECRET"
```

> Plik `launchSettings.json` jest wykluczony z gita — nie commituj go.

### Krok 4 — Uruchomienie

```bash
docker compose up --build
```

Pierwsze uruchomienie pobiera obrazy i buduje kontenery — może potrwać kilka minut.

Aplikacja dostępna pod:

| Serwis | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API | http://localhost:5000 |
| Swagger UI | http://localhost:5000/swagger |
| Health check | http://localhost:5000/health |

### Zatrzymanie aplikacji

```bash
docker compose down
```

Aby usunąć również dane z bazy (wolumen PostgreSQL):

```bash
docker compose down -v
```

---

## Struktura projektu

```
us-bank-system/
├── src/
│   ├── UsBankSystem.Api/             # ASP.NET Core Web API
│   ├── UsBankSystem.Core/            # Domain entities, interfaces
│   └── UsBankSystem.Infrastructure/  # EF Core, repositories
├── frontend/                         # React + Vite SPA
├── docs/
│   ├── domain.md                     # Wiedza domenowa
│   ├── uml/                          # Diagramy UML
│   └── bpmn/                         # Diagramy BPMN
├── docker-compose.yaml
├── .env.example
└── README.md
```

---

## API

Pełna dokumentacja dostępna przez Swagger UI pod `/swagger` po uruchomieniu aplikacji.

Główne endpointy:

| Metoda | Endpoint | Opis |
|---|---|---|
| POST | /auth/register | Rejestracja użytkownika |
| POST | /auth/login | Logowanie, zwraca JWT |
| GET | /accounts/{id} | Dane konta |
| GET | /accounts/{id}/balance | Saldo |
| GET | /accounts/{id}/transactions | Historia transakcji |
| POST | /transfers/internal | Przelew wewnętrzny |
| POST | /transfers/ach | Przelew ACH |
| POST | /transfers/rtp | Przelew RTP / FedNow |
| POST | /transfers/swift | Przelew SWIFT |
| GET | /transfers/{id}/status | Status przelewu |
| POST | /blik/generate | Generowanie kodu BLIK-USD |
| POST | /blik/verify | Weryfikacja kodu (dla systemu BLIK) |

---

## Integracje zewnętrzne

Projekt integruje się z modułami tworzonymi przez inne grupy. Adresy konfigurowane przez zmienne środowiskowe w `.env`:

```
INTEGRATIONS_ACH_URL=http://ach-module
INTEGRATIONS_SWIFT_URL=http://swift-module
INTEGRATIONS_CARDS_URL=http://cards-module
INTEGRATIONS_BLIK_URL=http://blik-module
```

W środowisku deweloperskim używane są lokalne stuby HTTP (mock serwisy).

---

## Migracje bazy danych

Projekt używa Entity Framework Core do zarządzania schematem bazy danych.

### Tworzenie nowej migracji

```bash
dotnet ef migrations add NazwaMigracji -p src/UsBankSystem.Infrastructure -s src/UsBankSystem.Api
```

### Aplikowanie migracji do bazy

```bash
dotnet ef database update -p src/UsBankSystem.Infrastructure -s src/UsBankSystem.Api --connection "Host=localhost;Port=5433;Database=usbank;Username=app;Password=secret"
```

---

## Workflow Git

- Główne gałęzie: `main` (produkcja), `develop` (integracja)
- Feature branche: `feature/US-XX-krotki-opis`
- Każda zmiana przez Pull Request z code review
- Format commitów: `Feat: krótki opis` / `Fix: ...` / `Docs: ...`

---

## Dokumentacja

- [Wiedza domenowa (ACH, RTP, FedNow, SWIFT, BLIK-USD)](docs/domain.md)
- [Diagramy UML](docs/uml/)
- [Diagramy BPMN](docs/bpmn/)
- [Backlog — Trello](https://trello.com/b/SoYXGs0x/tablica-projektowa)

---

## Zespół

| Osoba | Zakres |
|---|---|
| [Piotr Gorzkiewicz](https://github.com/g0rzki) | Backend core, Docker, integracje ACH/SWIFT |
| [Jakub Siłka](https://github.com/jakub7038) | Auth, frontend, karty, BLIK-USD |