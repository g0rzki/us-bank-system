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
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) — tylko do lokalnego developmentu poza Dockerem

### Krok 1 — Klonowanie repo

```bash
git clone https://github.com/g0rzki/us-bank-system.git
cd us-bank-system
```

### Krok 2 — Konfiguracja sekretów (lokalny development)

Skopiuj szablon konfiguracji i uzupełnij wartości:

```bash
cp src/UsBankSystem.Api/appsettings.Development.template.json src/UsBankSystem.Api/appsettings.Development.json
```

Plik `appsettings.Development.json` jest wykluczony z gita (`.gitignore`) — nie commituj go.

### Krok 3 — Uruchomienie przez Docker Compose

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
| pgAdmin | http://localhost:5050 |

pgAdmin login: `admin@usbank.local` / `admin`

### Krok 4 — Migracje bazy danych

Migracje są uruchamiane **automatycznie** przy starcie kontenera API. Nic nie trzeba robić ręcznie.

Jeśli chcesz uruchomić je manualnie (lokalny development poza Dockerem):

```bash
cd src/UsBankSystem.Api
dotnet ef database update
```

### Zatrzymanie aplikacji

```bash
docker compose down
```

Aby usunąć również dane z bazy (wolumen PostgreSQL):

```bash
docker compose down -v
```

---

## Lokalny development (bez Dockera)

Jeśli chcesz uruchomić API bezpośrednio przez Ridera lub CLI bez Dockera, potrzebujesz lokalnej instancji PostgreSQL na porcie `5432` z bazą `usbank`.

Uruchomienie samej bazy przez Docker (bez API):

```bash
docker compose up db pgadmin
```

Potem uruchom API z Ridera lub:

```bash
cd src/UsBankSystem.Api
dotnet run
```

Frontend (wymaga Node.js 20+):

```bash
cd frontend
npm install
npm run dev
```

---

## Struktura projektu

```
us-bank-system/
├── src/
│   ├── UsBankSystem.Api/           # ASP.NET Core Web API
│   ├── UsBankSystem.Core/          # Domain entities, interfaces
│   └── UsBankSystem.Infrastructure/# EF Core, repositories
├── frontend/                       # React + Vite SPA
├── docs/
│   ├── domain.md                   # Wiedza domenowa
│   ├── uml/                        # Diagramy UML
│   └── bpmn/                       # Diagramy BPMN
├── docker-compose.yml
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

Projekt integruje się z modułami tworzonymi przez inne grupy. Adresy konfigurowane przez zmienne środowiskowe w Docker Compose:

```
Integrations__AchUrl=http://ach-module
Integrations__SwiftUrl=http://swift-module
Integrations__CardsUrl=http://cards-module
Integrations__BlikUrl=http://blik-module
```

W środowisku deweloperskim używane są lokalne stuby HTTP (mock serwisy).

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

| Osoba                                          | Zakres |
|------------------------------------------------|---|
| [Piotr Gorzkiewicz](https://github.com/g0rzki) | Backend core, Docker, integracje ACH/SWIFT |
| [Jakub Siłka](https://github.com/jakub7038)    | Auth, frontend, karty, BLIK-USD |