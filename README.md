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

Integracja z zewnętrznym systemem **Karty-Platnicze-Aplikacje-Biznesowe** (payment-gateway + card-provider).

**Typy kart:**
- **Debit** — podpięta do konta bankowego, brak własnego salda. Rejestrowana jako `VIRTUAL` w payment-gateway, auto-aktywuje się w ciągu ~60s.
- **Prepaid** — ma własne saldo w payment-gateway. Po rejestracji bank automatycznie przeprowadza kartę przez lifecycle (`REQUESTED → PRODUCING → SHIPPED → ACTIVE`) i kartę można od razu doładować (topup).

**Przepływ płatności:**
1. Klient przykłada kartę do terminala POS
2. POS wywołuje autoryzację w payment-gateway → `APPROVED` / `DECLINED`
3. Card-provider po max 30s (dev) / 24h (prod) wysyła settlement `POST /capture` do banku
4. Bank zapisuje transakcję w historii konta

**Ograniczenia:**
- Jedno aktywne konto może mieć max 1 aktywną kartę debitową i 1 aktywną prepaid
- Konto junior może mieć wyłącznie kartę prepaid (max 1 aktywna)
- Zablokowana karta może zostać odblokowana dopiero po 24h od zablokowania
- Topup dostępny tylko dla kart prepaid w statusie `active`

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

Konto powiązane z kontem rodzica dla dzieci w wieku 7–13 lat.

- Każda transakcja inicjowana przez juniora trafia do statusu `pending_approval` i wymaga zatwierdzenia przez rodzica
- Rodzic widzi listę oczekujących transakcji i może je zatwierdzić lub odrzucić
- Junior może mieć jedną kartę prepaid z limitem dziennym i miesięcznym ustawianym przez rodzica
- Topup karty juniora wykonuje rodzic
 
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
POSTGRES_DB=usbank               # nazwa bazy — zostaw bez zmian
POSTGRES_USER=twoj_user          # dowolna nazwa użytkownika bazy
POSTGRES_PASSWORD=twoje_haslo
POSTGRES_PORT=5433               # port na hoście (5433 jeśli lokalny postgres zajmuje 5432)
FRONTEND_PORT=3000               # port frontendu — kontroluje Docker i CORS jednocześnie
API_URL=http://localhost:5100    # adres API — używany przez frontend
JWT_SECRET=min_32_znaki          # dowolny ciąg min. 32 znaków
WEBHOOK_SECRET=dowolny_sekret    # używany przez mock gateway do wysyłania webhooków
INTEGRATIONS_ACH_URL=http://localhost:6001
INTEGRATIONS_RTP_URL=http://localhost:6002
INTEGRATIONS_FEDNOW_URL=http://localhost:6003
INTEGRATIONS_SWIFT_URL=http://localhost:6004
INTEGRATIONS_CARDS_URL=http://payment-gateway:8000   # adres payment-gateway w sieci Docker
INTEGRATIONS_BLIK_URL=http://localhost:6006
CARDS_API_KEY=bank-key-us-a
CARDS_HMAC_SECRET=secret-us-a-hmac
CARDS_ADMIN_KEY=admin-secret-key-2026
```

> `FRONTEND_PORT` to jedyne miejsce gdzie ustawiasz port frontendu — `docker-compose.yaml` używa go zarówno do mapowania portów jak i do konfiguracji CORS w API.

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

| Serwis | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API | http://localhost:5100 |
| Swagger UI | http://localhost:5100/swagger |
| Health check | http://localhost:5100/health |
| Mock KLIK C2B | http://localhost:6006 |

> Jeśli uruchamiasz razem z projektem **Karty-Platnicze-Aplikacje-Biznesowe**, po każdym `docker compose up` w us-bank-system musisz podłączyć kontener banku do sieci payment-gatewaya, żeby settlement działał:
>
> ```bash
> docker network connect cards-backend us-bank-a
> ```
>
> Bez tego card-provider nie może wysłać callbacku `/capture` do banku i transakcje kartowe nie pojawią się w historii konta. Patrz sekcja [Integracja z Karty-Platnicze](#integracja-z-karty-platnicze).

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

### Auth

| Metoda | Endpoint | Opis |
|---|---|---|
| POST | /auth/register | Rejestracja użytkownika |
| POST | /auth/login | Logowanie, zwraca JWT (ważny 1h) |

### Konta

| Metoda | Endpoint | Opis |
|---|---|---|
| POST | /accounts | Tworzenie konta checking/savings |
| GET | /accounts | Lista kont zalogowanego użytkownika |
| GET | /accounts/{id} | Dane konta |
| GET | /accounts/{id}/balance | Saldo |
| GET | /accounts/{id}/transactions | Historia transakcji (paginacja: `?page=1&pageSize=20`) |
| POST | /accounts/junior | Tworzenie konta junior |
| GET | /accounts/{id}/junior-accounts | Lista kont junior podpiętych do konta rodzica |
| POST | /accounts/junior/{id}/card | Dodanie karty prepaid do konta junior (tylko rodzic) |
| PATCH | /accounts/{id}/junior-limit | Zmiana limitu karty prepaid juniora (tylko rodzic) |

### Przelewy

| Metoda | Endpoint | Opis |
|---|---|---|
| POST | /transfers/internal | Przelew wewnętrzny (natychmiastowy) |
| POST | /transfers/ach | Przelew ACH (T+1, batch) |
| POST | /transfers/rtp | Przelew RTP (real-time) |
| POST | /transfers/fednow | Przelew FedNow (RTGS) |
| POST | /transfers/swift | Przelew SWIFT (międzynarodowy) |
| GET | /transfers | Lista przelewów użytkownika |
| GET | /transfers/{id}/status | Status przelewu |
| GET | /transfers/pending-approval | Przelewy juniora czekające na zatwierdzenie |
| POST | /transfers/{id}/approve | Zatwierdzenie przelewu juniora przez rodzica |
| POST | /transfers/{id}/reject | Odrzucenie przelewu juniora przez rodzica |
| POST | /transfers/{id}/webhook | Webhook od mock gateway (zmiana statusu przelewu) |

### Karty

| Metoda | Endpoint | Opis |
|---|---|---|
| GET | /accounts/{id}/cards | Lista kart konta |
<<<<<<< HEAD
| POST | /cards/register | Rejestracja karty |
| POST | /cards/authorize | Webhook autoryzacji kartowej |
| POST | /blik/generate | Generowanie kodu BLIK (wywołuje KLIK API) |
| GET | /blik/pending | Lista oczekujących autoryzacji BLIK |
| POST | /blik/{id}/approve | Zatwierdź autoryzację BLIK (debit konta, potwierdź KLIK) |
| POST | /blik/{id}/reject | Odrzuć autoryzację BLIK |
| GET | /blik/transactions | Historia autoryzacji BLIK |
| POST | /klik/webhook/authorize | Webhook przychodzący z KLIK (autoryzacja płatności) |
| POST | /klik/webhook/ping | Webhook ping od KLIK (keepalive) |
=======
| POST | /accounts/{id}/cards | Rejestracja karty (`type: "debit"` lub `"prepaid"`) |
| GET | /accounts/{id}/cards/{cardId} | Szczegóły karty (synchronizuje status z payment-gateway) |
| PATCH | /accounts/{id}/cards/{cardId}/status | Zmiana statusu (`blocked` / `active`) |
| PATCH | /accounts/{id}/cards/{cardId}/limits | Ustawienie limitów dziennego/miesięcznego |
| POST | /accounts/{id}/cards/{cardId}/topup | Doładowanie karty prepaid |
| GET | /accounts/{id}/cards/{cardId}/external-status | Status karty w payment-gateway (saldo, limity) |
| POST | /capture | Webhook od card-provider po settlement transakcji kartowej |
>>>>>>> origin/main

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

## Integracja z Karty-Platnicze

Projekt integruje się z **Karty-Platnicze-Aplikacje-Biznesowe** — zewnętrznym systemem obsługi kart płatniczych składającym się z payment-gateway i card-providera.

### Wymagania

Projekt **Karty-Platnicze-Aplikacje-Biznesowe** musi być uruchomiony przed startem us-bank-system. Sklonuj i uruchom go według jego własnej dokumentacji.

Po uruchomieniu dostępny jest pod:

| Serwis | URL |
|---|---|
| Payment-gateway API | http://localhost:8072 |
| Payment-gateway docs | http://localhost:8072/docs |
| POS emulator (UI) | http://localhost:8072/pos |
| Admin panel | http://localhost:3072 |

### Konfiguracja sieci Docker

Kontenery us-bank-system i Karty-Platnicze działają w osobnych sieciach. Żeby card-provider mógł wysyłać settlement (`/capture`) do banku, po każdym uruchomieniu us-bank-system wykonaj:

```bash
docker network connect cards-backend us-bank-a
```

Bez tego płatności kartowe będą zatwierdzane przez POS, ale **nie pojawią się w historii transakcji konta**.

### Klucze API

W `.env` projektu us-bank-system skonfigurowane są klucze do komunikacji z payment-gateway:

```env
CARDS_API_KEY=bank-key-us-a          # klucz banku do wystawiania i blokowania kart
CARDS_HMAC_SECRET=secret-us-a-hmac   # sekret do podpisywania żądań HMAC-SHA256
CARDS_ADMIN_KEY=admin-secret-key-2026 # klucz admina (lifecycle prepaid, full-pan w testach)
```

### Jak wykonać płatność kartą (testowo)

1. Zaloguj się do frontendu i zarejestruj kartę (debit lub prepaid)
2. Dla prepaid: poczekaj ~6s na aktywację, następnie doładuj kartę przyciskiem "Top up card" w szczegółach karty
3. Skopiuj "Payment token" z szczegółów karty
4. Pobierz pełne dane karty (numer, CVV, data ważności):
   ```bash
   curl http://localhost:8072/api/v1/cards/<TOKEN>/full-pan \
     -H "X-Admin-Key: admin-secret-key-2026"
   ```
5. Wejdź na http://localhost:8072/pos i wpisz dane karty, lub użyj curl:
   ```bash
   curl -X POST http://localhost:8072/api/v1/payments/authorize \
     -H "Content-Type: application/json" \
     -d '{"card_number":"<PAN>","expiry_month":<M>,"expiry_year":<YY>,"cvv":"<CVV>","amount":50.00,"currency":"USD"}'
   ```
6. Po ~30 sekundach transakcja pojawi się w historii konta (settlement przez `POST /capture`)

### Przepływ techniczny

```
Frontend → POST /accounts/{id}/cards          # rejestracja karty
         ← externalCardToken (tok_...)        # token do dalszej komunikacji z gateway

POST /api/v1/payments/authorize (POS)         # autoryzacja płatności przez terminal
         → APPROVED / DECLINED

card-provider (po ~30s) → POST /capture       # settlement do banku
         → transakcja zapisana w DB           # widoczna w historii konta
```

---

## Testy

Projekt zawiera skrypty do testowania przepływu end-to-end przez curl.

### Happy path — pełny przepływ

```bash
bash test-flow.sh
```

Skrypt wykonuje: rejestrację użytkownika → login → utworzenie konta → rejestrację karty debitowej i prepaid → topup prepaid → płatność przez POS → zablokowanie karty debitowej. Na końcu wypisuje tokeny kart.

### Edge cases

```bash
bash test-edge-cases.sh
```

Pokrywa 25 przypadków brzegowych:

| Kategoria | Przykłady |
|---|---|
| Auth | Duplikat emaila, złe hasło, nieistniejący użytkownik |
| Karty | Duplikat aktywnej karty, nieprawidłowy typ, monthly < daily |
| Autoryzacja | Dostęp do cudzego konta → 403 |
| Block/unblock | Cooldown 24h po zablokowaniu, próba ustawienia statusu `expired` |
| Topup | Topup karty debitowej → 400, kwota ujemna/zerowa → 400 |
| Limity | Puste body → 400, monthly < daily → 400 |
| Capture webhook | Nieznany token → 200 SETTLED, kwota ujemna → 400 |
| POS | Zablokowana karta → DECLINED, zły CVV → DECLINED, błędny PAN (Luhn) → 422 |
| No-auth | Brak tokenu JWT → 401 |

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
