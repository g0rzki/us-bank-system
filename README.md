# US Bank System

Aplikacja webowa symulująca działanie amerykańskiego banku detalicznego. Projekt grupowy — moduł **Bank B (USA)**.

## Zakres

- Przelewy wewnętrzne między kontami
- ACH — standardowy przelew międzybankowy (rozliczenie T+1)
- RTP — natychmiastowy przelew konsumencki (real-time, 24/7)
- FedNow — przelew RTGS przez bank centralny
- SWIFT — przelew międzynarodowy
- Karty płatnicze (integracja) — transakcje tylko w USD
- BLIK — przelewy natychmiastowe (integracja)
- Konto junior (7-13 lat) — podpięte do konta rodzica, wszystkie transakcje wymagają zatwierdzenia przez rodzica, możliwość podpięcia karty prepaid z limitami

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

## Wiedza domenowa

### ACH (Automated Clearing House)
> 📝 TODO (US-57) — opis mechanizmu, okna czasowe batch, rozliczenie T+1, rola NACHA

### RTP (Real-Time Payments)
> 📝 TODO (US-57) — opis mechanizmu, rozliczenie natychmiastowe 24/7, rola The Clearing House

### FedNow
> 📝 TODO (US-57) — opis mechanizmu RTGS, rola Fed Reserve, różnica vs RTP

### SWIFT
> 📝 TODO (US-57) — opis sieci korespondentów, IBAN, BIC, SWIFT gpi

### Karty płatnicze
> 📝 TODO (US-58) — opis autoryzacji, rozliczenia, rola issuera, acquirera, sieci kartowej

### BLIK / KLIK C2B

Integracja z systemem **KLIK** (akademicki klon BLIK). Bank jest klientem API KLIK i wystawia webhook.

**Flow C2B:**
1. Klient klika „Generuj kod" → bank wywołuje `POST /api/v1/codes/generate` w KLIK → dostaje 6-cyfrowy kod ważny 120s
2. Klient pokazuje kod kasjerowi / terminalowi (agent)
3. Agent wywołuje `POST /api/v1/payments/initiate` w KLIK → KLIK wysyła webhook `POST /klik/webhook/authorize` do banku
4. Bank natychmiast odpowiada `{received: true}` i pokazuje użytkownikowi modal z kwotą i merchant_name
5. Użytkownik zatwierdza lub odrzuca → bank wywołuje `POST /api/v1/payments/confirm` w KLIK i obciąża konto (ACCEPTED) lub odrzuca (REJECTED)

**KLIK API key** i adres instancji konfigurowane przez env (`Integrations__BlikUrl`, `Integrations__KlikApiKey`).
W dev: mock KLIK uruchomiony jako część `mock-gateways` na porcie **6006**.

### Konto junior
> 📝 TODO (US-58) — opis mechanizmu zatwierdzania transakcji przez rodzica, karta prepaid, limity
 
---

## Diagramy

### Model domenowy (UML Class Diagram)

> 📝 TODO (US-52) — diagram klas: User, Account, JuniorAccount, Transaction, Transfer, Card, BlikCode
> Narzędzie: Mermaid lub draw.io

```mermaid
%% TODO (US-52): Uzupełnić diagram modelu domenowego
classDiagram
    class User
    class Account
    class JuniorAccount
    class Transaction
    class Transfer
    class Card
    class BlikCode
```

### Przepływ przelewu ACH (BPMN)

> 📝 TODO (US-53) — diagram przepływu: inicjacja → batch → clearing → settlement → status

```mermaid
%% TODO (US-53): Uzupełnić diagram BPMN przepływu ACH
flowchart LR
    A[Inicjacja ACH] --> B[Batch window]
    B --> C[Clearing]
    C --> D[Settlement T+1]
    D --> E[Status update]
```

### Przepływ przelewu FedNow (BPMN)

> 📝 TODO (US-53) — diagram przepływu: inicjacja → walidacja → RTGS → settlement natychmiastowy

```mermaid
%% TODO (US-53): Uzupełnić diagram BPMN przepływu FedNow
flowchart LR
    A[Inicjacja FedNow] --> B[Walidacja]
    B --> C[RTGS Fed Reserve]
    C --> D[Settlement natychmiastowy]
```

### Przepływ KLIK C2B (BPMN)

```mermaid
flowchart LR
    A[POST /blik/generate] --> B[KLIK codes/generate]
    B --> C[Kod 6-cyfrowy TTL 120s]
    C --> D[Agent skanuje kod]
    D --> E[KLIK → webhook /authorize]
    E --> F[Modal Approve/Reject]
    F --> G[POST /blik/id/approve|reject]
    G --> H[KLIK payments/confirm]
    H --> I[Debit konta / REJECTED]
```

### Przepływ zatwierdzania transakcji junior (BPMN)

> 📝 TODO (US-54) — diagram przepływu: transakcja pending → powiadomienie rodzica → approve/reject

```mermaid
%% TODO (US-54): Uzupełnić diagram BPMN zatwierdzania transakcji junior
flowchart LR
    A[Transakcja junior] --> B[Status: pending_approval]
    B --> C[Rodzic zatwierdza / odrzuca]
    C --> D[Wykonanie / anulowanie]
```
 
---

## Konfiguracja sesji płatności

Plik `src/UsBankSystem.Api/payment-config.json` pozwala konfigurować parametry czasowe systemów płatności. W środowisku deweloperskim skracasz wartości żeby testować integracje bez czekania na prawdziwe okna czasowe.

```json
{
  "PaymentSessions": {
    "Ach": {
      "BatchWindowMinutes": 1,
      "CutoffHour": 23
    },
    "FedNow": {
      "TimeoutSeconds": 10
    },
    "Rtp": {
      "TimeoutSeconds": 10
    },
    "Swift": {
      "TimeoutSeconds": 30
    }
  }
}
```

Wartości produkcyjne:
- ACH batch window: ~2-3h, cutoff: 17:00 ET
- FedNow timeout: 20s
- RTP timeout: 10s
- SWIFT: 1-5 dni roboczych
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
API_URL=http://localhost:5100  # adres API — używany przez frontend (dev i Docker)
JWT_SECRET=min_32_znaki     # dowolny ciąg min. 32 znaków
WEBHOOK_SECRET=dowolny_sekret  # używany przez mock gateway do wysyłania webhooków
CORS_ORIGIN=http://localhost:3000
INTEGRATIONS_ACH_URL=http://localhost:6001
INTEGRATIONS_RTP_URL=http://localhost:6002
INTEGRATIONS_FEDNOW_URL=http://localhost:6003
INTEGRATIONS_SWIFT_URL=http://localhost:6004
INTEGRATIONS_CARDS_URL=http://localhost:6005
INTEGRATIONS_BLIK_URL=http://localhost:6006
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

> `docker compose up` odpala też mock gateway automatycznie jako osobny serwis — nie trzeba nic robić ręcznie.

Aplikacja dostępna pod:

| Serwis | URL                           |
|---|-------------------------------|
| Frontend | http://localhost:3000         |
| API | http://localhost:5100         |
| Swagger UI | http://localhost:5100/swagger |
| Health check | http://localhost:5100/health  |
| Mock KLIK C2B | http://localhost:6006         |

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
│   │   └── payment-config.json       # konfiguracja sesji płatności (timeouty, okna batch)
│   ├── UsBankSystem.Core/            # Domain entities, interfaces
│   ├── UsBankSystem.Infrastructure/  # EF Core, repositories
│   ├── UsBankSystem.MockGateways/    # Mock stuby ACH/RTP/FedNow/SWIFT/KLIK (porty 6001-6004, 6006)
│   ├── UsBankSystem.Tests/           # Testy API
│   └── UsBankSystem.MockGateways.Tests/ # Testy mock stubów
├── frontend/                         # React + Vite SPA
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
| GET | /accounts/{id}/transactions | Historia transakcji z paginacją |
| POST | /accounts | Tworzenie konta checking/savings |
| POST | /accounts/junior | Tworzenie konta junior (wymaga parent_account_id) |
| GET | /accounts/{id}/junior-accounts | Lista kont junior (widok rodzica) |
| GET | /accounts/{id}/junior-details | Szczegóły konta junior |
| PATCH | /accounts/{id}/junior-limit | Zmiana limitu karty prepaid przez rodzica |
| GET | /transfers/pending-approval | Lista transakcji czekających na zatwierdzenie (rodzic) |
| POST | /transfers/{id}/approve | Zatwierdzenie transakcji junior przez rodzica |
| POST | /transfers/{id}/reject | Odrzucenie transakcji junior przez rodzica |
| POST | /transfers/internal | Przelew wewnętrzny |
| POST | /transfers/ach | Przelew ACH (T+1) |
| POST | /transfers/rtp | Przelew RTP (real-time) |
| POST | /transfers/fednow | Przelew FedNow (RTGS) |
| POST | /transfers/swift | Przelew SWIFT |
| GET | /transfers/{id}/status | Status przelewu |
| GET | /accounts/{id}/cards | Lista kart konta |
| POST | /cards/register | Rejestracja karty |
| POST | /cards/authorize | Webhook autoryzacji kartowej |
| POST | /blik/generate | Generowanie kodu BLIK (wywołuje KLIK API) |
| GET | /blik/pending | Lista oczekujących autoryzacji BLIK |
| POST | /blik/{id}/approve | Zatwierdź autoryzację BLIK (debit konta, potwierdź KLIK) |
| POST | /blik/{id}/reject | Odrzuć autoryzację BLIK |
| GET | /blik/transactions | Historia autoryzacji BLIK |
| POST | /klik/webhook/authorize | Webhook przychodzący z KLIK (autoryzacja płatności) |
| POST | /klik/webhook/ping | Webhook ping od KLIK (keepalive) |

---

## Integracje zewnętrzne

Projekt integruje się z modułami tworzonymi przez inne grupy. Adresy konfigurowane przez zmienne środowiskowe w `.env`:

```
INTEGRATIONS_ACH_URL=http://ach-module
INTEGRATIONS_RTP_URL=http://rtp-module
INTEGRATIONS_FEDNOW_URL=http://fednow-module
INTEGRATIONS_SWIFT_URL=http://swift-module
INTEGRATIONS_CARDS_URL=http://cards-module
INTEGRATIONS_BLIK_URL=http://klik-module      # adres KLIK API (mock: http://localhost:6006)
INTEGRATIONS_KLIK_API_KEY=twoj_api_key        # klucz API od operatora KLIK
KLIK_WEBHOOK_SECRET=opcjonalny_sekret         # nagłówek X-Webhook-Secret na /klik/webhook/*
```

W środowisku deweloperskim każda integracja działa przez **mock stub** (`UsBankSystem.MockGateways`) — osobny serwis który symuluje realistyczne zachowanie każdego kanału:

| Kanał | Port | Zachowanie |
|---|---|---|
| ACH | 6001 | Odpowiada natychmiast, po skonfigurowanym czasie wysyła webhook do API z wynikiem (jak prawdziwy batch) |
| RTP | 6002 | Czeka kilka sekund i odpowiada synchronicznie `Completed` (real-time rail) |
| FedNow | 6003 | Tak samo jak RTP |
| SWIFT | 6004 | Odpowiada natychmiast, webhook po dłuższym czasie (settlement 1-5 dni roboczych) |
| KLIK C2B | 6006 | Mock KLIK: generuje kody (TTL 120s), symuluje agenta (`POST /simulate/initiate`), przetwarza potwierdzenia z fee 1%+0.5% |

Czasy opóźnień ACH/RTP/FedNow/SWIFT są brane z `payment-config.json`.

#### Testowanie KLIK C2B z mockiem

```bash
# 1. Zaloguj się i zapisz token
TOKEN=$(curl -s -X POST http://localhost:5100/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"john.doe@example.com","password":"Test123!"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])")

# 2. Wygeneruj kod BLIK (zastąp accountId ID konta z /accounts)
curl -s -X POST http://localhost:5100/blik/generate \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"accountId":"aaaa1111-1111-1111-1111-111111111111"}'
# → {"code":"123456","expiresAt":"...","expiresIn":119}

# 3. Zasymuluj agenta — wpisz kod z poprzedniego kroku
INIT=$(curl -s -X POST http://localhost:6006/simulate/initiate \
  -H "Content-Type: application/json" \
  -d '{"code":"123456","amount":25.00,"currency":"USD","merchant_name":"Coffee Corner","is_on_us":false}')
echo $INIT
# → {"transaction_id":"...","status":"pending"}

# 4. Sprawdź oczekującą autoryzację
curl -s http://localhost:5100/blik/pending -H "Authorization: Bearer $TOKEN"

# 5. Zatwierdź (zastąp {authId} ID z kroku 4)
curl -s -X POST http://localhost:5100/blik/{authId}/approve \
  -H "Authorization: Bearer $TOKEN"
# → {"status":"accepted","localTransactionId":"..."}

# 6. Sprawdź status w KLIK (zastąp {txId} z INIT.transaction_id)
curl -s http://localhost:6006/api/v1/payments/status/{txId}
# → {"status":"COMPLETED","klik_fee":0.25,"agent_fee":0.125,"merchant_net":24.625}
```

Żeby przełączyć się z mocka na prawdziwy moduł — zmień odpowiedni URL w `.env` na adres modułu innej grupy, np.:

```env
INTEGRATIONS_ACH_URL=http://adres-modulu-ach
```

---

## Migracje bazy danych

Projekt używa Entity Framework Core. Migracje aplikują się **automatycznie** przy starcie aplikacji (`docker compose up --build`).

### Tworzenie nowej migracji

```bash
dotnet ef migrations add NazwaMigracji -p src/UsBankSystem.Infrastructure -s src/UsBankSystem.Api
```

---

## Workflow Git

- Gałąź `main` — każda zmiana przez PR z 1 approvem drugiego członka zespołu
- Gałąź `develop` - integracje z zewnętrznymi modułami innych grup
- Feature branche: `feature/US-XX-krotki-opis`, tworzone od `main`
- Commity mergowane przez **Squash and merge**
- Nie merguj własnego PR bez review drugiej osoby

### Format commitów

```
Feat: krótki opis       # nowa funkcjonalność
Fix: krótki opis        # naprawa błędu
Docs: krótki opis       # dokumentacja
Refactor: krótki opis   # refaktor bez zmiany funkcjonalności
```

### Tworzenie feature brancha

```bash
git checkout main
git pull
git checkout -b feature/US-XX-krotki-opis
```

---

## Dokumentacja

- [Backlog — Trello](https://trello.com/b/SoYXGs0x/tablica-projektowa)
- [Swagger UI](http://localhost:5000/swagger) — po uruchomieniu aplikacji

---

## Zespół

| Osoba | Zakres                                                |
|---|-------------------------------------------------------|
| [Piotr Gorzkiewicz](https://github.com/g0rzki) | Backend core, przelewy zewnętrzne, konto junior, BLIK |
| [Jakub Siłka](https://github.com/jakub7038) | Auth, frontend, karty, SWIFT                          |
