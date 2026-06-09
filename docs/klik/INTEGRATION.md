# Integracja us-bank-system ↔ KLIK (moduł C2B) — plan + prompty dla Claude Code

Data: 2026-06-09 · Bank: us-bank-system (Bank B / USA, zone **US**, waluta **USD**)
Repo banku: https://github.com/g0rzki/us-bank-system
Repo KLIK: https://github.com/MarshallBjorn/KLIK-payments
Dokumentacja integracyjna KLIK (C2B): https://github.com/MarshallBjorn/KLIK-payments/blob/main/docs/c2b/integration/INFO.md

---

## 1. Jak naprawdę wygląda flow (vs pierwotne taski Trello)

Pierwotne taski zakładały, że bank generuje i weryfikuje kody. W modelu KLIK jest odwrotnie —
**KLIK jest operatorem kodów**, bank jest klientem API + wystawia webhook.

```
KLIENT (UI banku)          BANK (us-bank-system)            KLIK                    AGENT (terminal)
      |                          |                            |                          |
      |-- "Generuj kod" -------->|                            |                          |
      |                          |-- POST /codes/generate --->|  (user_id, zone=US)      |
      |                          |<-- code "123456", 120s ----|                          |
      |<-- kod + timer 120s -----|                            |                          |
      |                          |                            |<-- POST /payments/initiate (code, amount, merchant)
      |                          |<-- POST {webhook}/authorize|  (transaction_id, user_id, amount, merchant_name, is_on_us)
      |                          |-- 200 {received:true} ---->|  (max 30s!)              |
      |<-- push/modal "Zatwierdź"|                            |                          |
      |-- Approve/Reject ------->|                            |                          |
      |                          |-- POST /payments/confirm ->|  (ACCEPTED/REJECTED, max 60s!)
      |                          |   + debit konta klienta    |                          |
      |                          |<-- 200 {COMPLETED, fees} --|--> agent widzi COMPLETED |
```

Rozliczenie międzybankowe: robi **KLIK** (netting + RTGS FedNow dla strefy US). Bank NIE inicjuje
RTP per transakcja. Po stronie banku "settlement" = obciążenie konta klienta (i uznanie merchanta,
jeśli `is_on_us=true` i merchant ma konto w naszym banku).

## 2. Mapowanie tasków Trello → rzeczywista implementacja

| Task | Pierwotne założenie | Co naprawdę robimy |
|---|---|---|
| US-48 | POST /blik/generate, hash + expires 2 min lokalnie | `POST /blik/generate` w naszym API → proxy do KLIK `POST /codes/generate` (user_id = nasz userId, zone=US). Zapis lokalnego rekordu BlikCode (kod, expires_at, status) do historii/korelacji. Kod przechowuje KLIK (Redis TTL 120s). |
| US-49 | POST /blik/verify dla zewnętrznej grupy | **Webhook** `POST /klik/webhook/authorize` (wywoływany przez KLIK). Odbiera transaction_id, user_id, amount, currency, merchant_name, is_on_us, expiry_time. Tworzy lokalną pending autoryzację BLIK, odpowiada `{"received": true, "will_prompt_user": true}` synchronicznie (<30s). Dodatkowo `POST /klik/webhook/ping` (echo timestamp+nonce, `pong: true`) — wymagany przy onboardingu. |
| US-50 | Po verify inicjacja RTP | Po decyzji użytkownika: bank woła KLIK `POST /payments/confirm` (ACCEPTED → debit konta klienta jako Transaction; REJECTED z reject_reason np. INSUFFICIENT_FUNDS / USER_DECLINED → nic nie księgujemy). Brak środków = automatyczny REJECTED + INSUFFICIENT_FUNDS. Opcjonalnie: przy `is_on_us=true` credit merchant_net na konto merchanta u nas. |
| US-51 | Mock stub BLIK | **KlikMockGateway** w UsBankSystem.MockGateways (port 6005): implementuje `/codes/generate`, `/payments/confirm`, `/payments/status/{id}` + endpoint symulacji agenta `POST /simulate/initiate` (przyjmuje code+amount+merchant i woła nasz webhook /authorize). Pozwala testować cały flow bez deployu drugiej grupy. |
| US-52 | Panel BLIK: kod + timer + status | BlikView (React): przycisk "Generate KLIK code", duży kod, countdown 120s, po wykryciu pending autoryzacji (polling `GET /blik/pending`) modal Approve/Reject z kwotą i merchantem, status końcowy z toasta. |
| US-53 | Historia transakcji BLIK | `GET /blik/transactions` + sekcja w dashboardzie (data, merchant, kwota, status). |

> UWAGA: README KLIK wymaga integracji **C2B i P2P** (aliasy telefonów). P2P nie jest objęte tym
> planem — dodać osobne taski (rejestracja aliasu przy włączeniu funkcji, lookup przed przelewem
> "na telefon", delete przy zamknięciu konta) i zrobić w kolejnym PR.

## 3. Wyciąg ze specyfikacji KLIK API (źródło: docs/c2b/integration/INFO.md)

Baza: `{KLIK_BASE_URL}/api/v1` (dev: mock na http://localhost:6005; prod: instancja grupy KLIK).

Nagłówki każdego żądania do KLIK:
```
X-KLIK-Api-Key: <z onboardingu>
Content-Type: application/json
Idempotency-Key: <uuid-v4>     # tylko żądania mutujące
```

### POST /codes/generate  (woła nasz bank)
Request:  `{ "user_id": "<nasz userId>", "zone": "US" }`
Response: `{ "code": "123456", "expires_in": 120, "expires_at": "..." }`
Błędy: 401, 403_BANK_INACTIVE, 422_CURRENCY_MISMATCH, 500, 503_REDIS_UNAVAILABLE

### POST /payments/confirm  (woła nasz bank po decyzji klienta, w ciągu 60s)
Request (akceptacja):  `{ "transaction_id": "...", "status": "ACCEPTED" }`
Request (odrzucenie):  `{ "transaction_id": "...", "status": "REJECTED", "reject_reason": "INSUFFICIENT_FUNDS" }`
Dozwolone reject_reason: INSUFFICIENT_FUNDS, USER_DECLINED, PIN_FAILED, AML_BLOCK, OTHER
Response 200: `{ transaction_id, status: "COMPLETED", amount_gross, klik_fee, agent_fee, merchant_net, currency, completed_at }`
Idempotentny: ten sam status → 200 bez efektu; inna decyzja → 409.
Błędy: 400, 401, 404_TRANSACTION_NOT_FOUND, 409_PREMATURE_CONFIRM, 409_CONFIRM_DECISION_CONFLICT, 409_TRANSACTION_ALREADY_CLOSED

### GET /payments/status/{transaction_id}
Statusy: PENDING, AUTHORIZED, COMPLETED, REJECTED, TIMEOUT. Polling 1–2s.

### Webhook, który MY wystawiamy: POST {bank_webhook_url}/authorize  (woła KLIK)
Payload:
```json
{
  "transaction_id": "550e8400-...",
  "user_id": "<nasz userId>",
  "amount": "150.00",
  "currency": "USD",
  "merchant_name": "Store X",
  "is_on_us": false,
  "expiry_time": "2026-04-23T14:01:00Z",
  "zone": "US"
}
```
Odpowiedź synchroniczna (HTTP 200, max 30s, KLIK retryuje 3x):
`{ "received": true, "will_prompt_user": true }`
Decyzja klienta idzie OSOBNO przez /payments/confirm (okno 60s).

### Webhook ping: POST {bank_webhook_url}/ping
Payload `{timestamp, nonce}` → odpowiedz `{timestamp, nonce, pong: true}` (echo 1:1).

### Format błędów KLIK
```json
{ "error": { "code": "404_CODE_EXPIRED", "message": "...", "transaction_id": "...", "timestamp": "..." } }
```
Retry: 4xx nie retryować; 5xx exponential backoff (1s, 5s, 30s, 2min, stop po 5).

### Onboarding (poza kodem, do zrobienia z grupą KLIK)
1. Dostajemy `api_key` od operatora KLIK.
2. Rejestrujemy webhook: `POST /banks/webhook-config` z naszym publicznym URL.
3. KLIK pinguje → `active=true`. Webhook musi być publicznie dostępny (np. Cloudflare Tunnel).

## 4. Konfiguracja w us-bank-system

Zmienne env / appsettings (sekcja `Klik`):
```
KLIK_BASE_URL=http://localhost:6005          # dev: mock; prod: URL instancji KLIK
KLIK_API_KEY=dev-key
KLIK_ZONE=US
KLIK_WEBHOOK_SHARED_SECRET=dev-secret        # opcjonalne zabezpieczenie naszego webhooka (MVP KLIK nie podpisuje)
```
Stałe domenowe do dodania: TransferChannel/`blik` (lub osobny typ), BlikAuthorizationStatus:
`pending`, `accepted`, `rejected`, `timeout`.

## 5. Git workflow

```bash
git checkout main && git pull
git checkout -b feature/US-48-51-klik-c2b-backend
# ... praca Claude Code, commity Feat:/Fix:/Refactor: ...
# PR → Squash and merge
git checkout main && git pull
git checkout -b feature/US-52-53-blik-frontend
```

Dwa PR-y: backend+mock (US-48–51), potem frontend (US-52–53). Mniejsze PR-y = łatwiejszy review Jakuba.

## 6. Ustawienia Claude Code (żeby nie przepalić tokenów)

- **Model: Sonnet 4.6** (`/model sonnet`). Do tej roboty Opus/Fable to przepał — plan już jest
  (ten dokument), Sonnet jest tańszy ~5x i spokojnie ogarnia implementację wg specyfikacji.
- **Plan Mode na start każdego promptu** (Shift+Tab dwa razy): Claude najpierw pokaże plan,
  zatwierdzasz, dopiero potem pisze kod. Wyłapiesz złe założenia zanim spali tokeny na kod.
- Ten plik wrzuć do repo jako `docs/klik/INTEGRATION.md` i w promptach odwołuj się przez
  `@docs/klik/INTEGRATION.md` — Claude nie będzie musiał sam eksplorować repo KLIK.
- **`/clear` między promptem 1 a 2** (backend → frontend) — stary kontekst backendu nie jest
  potrzebny przy froncie, a kosztuje.
- Jeśli sesja się rozrośnie: `/compact`.
- Nie każ mu klonować repo KLIK — wszystko co potrzebne jest w tym dokumencie.

## 7. Prompty do Claude Code

### Prompt 0 — przygotowanie (wykonaj sam w terminalu, nie przez agenta)

```bash
cd ~/path/do/us-bank-system
git checkout main && git pull
git checkout -b feature/US-48-51-klik-c2b-backend
mkdir -p docs/klik
cp ~/Downloads/klik-integration-claude-code.md docs/klik/INTEGRATION.md
git add docs/klik/INTEGRATION.md && git commit -m "Docs: KLIK C2B integration spec"
claude
```

### Prompt 1 — backend + mock (US-48, US-49, US-50, US-51) — uruchom w Plan Mode

```
Przeczytaj @docs/klik/INTEGRATION.md — to specyfikacja integracji naszego banku z systemem
KLIK (akademicki klon BLIK). Zaimplementuj taski US-48–US-51 (backend + mock), zgodnie
z konwencjami tego repo (architektura serwisów, ErrorHandlingMiddleware, wzorzec testów xUnit
z InMemoryDatabase opisany w README/testach).

Zakres:

1. KlikApiClient (Infrastructure): typowany HttpClient do KLIK API.
   - POST /api/v1/codes/generate i POST /api/v1/payments/confirm zgodnie ze spec w INTEGRATION.md.
   - Nagłówki X-KLIK-Api-Key i Idempotency-Key (uuid v4) na żądaniach mutujących.
   - Konfiguracja z env: KLIK_BASE_URL, KLIK_API_KEY, KLIK_ZONE (sekcja Klik w appsettings,
     fallback na env vars jak w AppDbContextFactory).
   - Mapowanie błędów KLIK (format {"error": {code, message}}) na nasze wyjątki zgodnie
     z ErrorHandlingMiddleware.

2. Encje + migracja:
   - Rozszerz/wykorzystaj istniejącą encję BlikCode (sprawdź co już ma): UserId, Code,
     ExpiresAt, Status.
   - Nowa encja BlikAuthorization: KlikTransactionId (uuid, unique), UserId, Amount, Currency,
     MerchantName, IsOnUs, ExpiryTime, Status (pending/accepted/rejected/timeout), CreatedAt,
     DecidedAt, LocalTransactionId (FK do Transaction, nullable).
   - Migracja EF: dotnet ef migrations add AddBlikIntegration -p src/UsBankSystem.Infrastructure
     -s src/UsBankSystem.Api (przeczytaj README jak ładujemy env do migracji).

3. BlikService (Core/serwisy, wzorzec jak TransferService):
   - GenerateCodeAsync(userId, accountId): woła KLIK codes/generate, zapisuje BlikCode, zwraca
     kod + expires.
   - HandleAuthorizeWebhookAsync(payload): waliduje user_id (musi istnieć), zapisuje
     BlikAuthorization (pending), zwraca received=true. NIE blokuj wątku — odpowiedź ma być
     natychmiastowa (limit KLIK: 30s).
   - DecideAsync(userId, authorizationId, accepted): sprawdza okno czasowe (expiry_time)
     i saldo. ACCEPTED → debit Transaction na koncie klienta (completed) + KLIK
     payments/confirm ACCEPTED; brak środków → confirm REJECTED z INSUFFICIENT_FUNDS;
     odrzucenie usera → REJECTED z USER_DECLINED. Jeśli is_on_us=true i merchant_iban/konto
     istnieje u nas — pomiń na razie (TODO komentarz).
   - GetPendingAsync(userId), GetHistoryAsync(userId).

4. Kontrolery:
   - BlikController ([Authorize]): POST /blik/generate, GET /blik/pending,
     POST /blik/{authorizationId}/approve, POST /blik/{authorizationId}/reject,
     GET /blik/transactions.
   - KlikWebhookController (AllowAnonymous, ale waliduj nagłówek X-Webhook-Secret ==
     KLIK_WEBHOOK_SHARED_SECRET jeśli ustawiony): POST /klik/webhook/authorize,
     POST /klik/webhook/ping (echo timestamp+nonce + pong:true).

5. US-51 Mock: nowy KlikMockGateway w UsBankSystem.MockGateways na porcie 6005, w stylu
   istniejących mock gatewayów (zobacz jak zrobione są ACH/RTP/FedNow/SWIFT na 6001-6004):
   - POST /api/v1/codes/generate → losowy 6-cyfrowy kod, in-memory store z TTL 120s.
   - POST /api/v1/payments/initiate (symulacja agenta — można też jako
     POST /simulate/initiate {code, amount, merchant_name}): waliduje kod (404_CODE_EXPIRED /
     409_CODE_ALREADY_USED), tworzy transakcję PENDING i woła webhook banku
     POST {BANK_WEBHOOK_URL}/authorize z payloadem jak w spec.
   - POST /api/v1/payments/confirm → przejście PENDING→COMPLETED/REJECTED, wylicz
     przykładowe fee (klik_fee 1%, agent_fee 0.5%), zwróć pełny response jak w spec.
   - GET /api/v1/payments/status/{id}.
   - BANK_WEBHOOK_URL mocka z env, default http://localhost:5100 (sprawdź port API w compose).
   - Dodaj serwis do docker-compose.

6. Testy xUnit wg wzorca z INTEGRATION.md/istniejących testów: generate (zapis BlikCode),
   webhook authorize (tworzy pending), approve happy path (debit + confirm ACCEPTED przez
   zmockowany IKlikApiClient), approve bez środków (REJECTED INSUFFICIENT_FUNDS, brak
   Transaction), reject, próba decyzji po expiry_time (timeout). Pamiętaj o ograniczeniu
   InMemory DB: ToLowerInvariant() poza zapytaniem EF.

Najpierw przedstaw plan plików do utworzenia/zmiany, poczekaj na akceptację. Po implementacji:
dotnet build + dotnet test, napraw błędy. Commity wg konwencji Feat:/Fix:/Refactor:, po jednym
na logiczny krok (klient API, encje+migracja, serwis+kontrolery, mock, testy).
```

### Prompt 2 — test end-to-end na mocku (ta sama sesja, po promptcie 1)

```
Uruchom środowisko (API + Postgres + KlikMockGateway, jak w docker-compose / README) i przetestuj
cały flow curl-ami na danych seedera (john.doe@example.com / Test123!):
1. Login → token.
2. POST /blik/generate → kod.
3. Symulacja agenta na mocku: initiate z tym kodem, amount 25.00 USD, merchant "Coffee Corner".
4. GET /blik/pending → powinna być autoryzacja.
5. POST /blik/{id}/approve → sprawdź: Transaction debit na koncie, status COMPLETED na mocku
   (GET /api/v1/payments/status/{id}).
6. Negatywne: kod po 120s (możesz skrócić TTL mocka env-em do 5s na czas testu), kod użyty
   2x, approve przy saldzie 0.
Wypisz wyniki każdego kroku. Jeśli coś nie działa — napraw i powtórz. Na koniec zaktualizuj
README (sekcja BLIK/KLIK: porty, env, flow) i przygotuj opis PR.
```

### Prompt 3 — frontend (US-52, US-53) — po `/clear`, na branchu feature/US-52-53-blik-frontend

```
Przeczytaj @docs/klik/INTEGRATION.md (sekcje 1-2) oraz istniejący frontend (React+Vite+TS,
ToastContext, useDarkMode, style przez CSS variables, max-width 900px na .db-view, osobny
kidClient dla juniora — BLIK robimy TYLKO dla zwykłego użytkownika, nie junior).

Zaimplementuj US-52 i US-53:

1. BlikView (nowa zakładka w dashboardzie):
   - Przycisk "Generate KLIK code" → POST /blik/generate.
   - Duży, czytelny kod (format "123 456"), okrągły/pasek countdown od expires_in (120s),
     po wygaśnięciu stan "Code expired" + przycisk ponownego generowania.
   - W trakcie życia kodu polling GET /blik/pending co 2s. Gdy pojawi się autoryzacja →
     modal: merchant_name, kwota+waluta, przyciski Approve / Reject →
     POST /blik/{id}/approve|reject. Wynik toastem (ToastContext). Pokaż licznik na okno
     decyzji (expiry_time z autoryzacji).
   - Obsłuż stany błędów (409 cooldown-style, 4xx z middleware) toastami.

2. US-53: sekcja "BLIK history" (GET /blik/transactions): data, merchant, kwota, status
   (completed/rejected/timeout) z kolorowym badge, spójnie z istniejącą listą transakcji.

3. Dark mode przez istniejące CSS variables, żadnych hardcodowanych kolorów. Uważaj na Safari
   przy animacjach (jak przy AccountCard flip).

Najpierw plan komponentów i plików, potem implementacja. Przetestuj flow ręcznie z mockiem
(instrukcja w README sekcja BLIK). Commity Feat:, na koniec opis PR.
```

### Prompt 4 — przejście z mocka na prawdziwy KLIK (kiedy dostaniecie api_key)

```
Mamy dostęp do prawdziwej instancji KLIK: {URL}, api_key: {KEY}. Nasz webhook jest publicznie
dostępny pod {PUBLIC_URL} (Cloudflare Tunnel).
1. Podmień konfigurację (env) na prawdziwy KLIK_BASE_URL i KLIK_API_KEY.
2. Sprawdź zgodność naszego klienta z docs: pobierz
   https://raw.githubusercontent.com/MarshallBjorn/KLIK-payments/main/docs/c2b/integration/INFO.md
   i porównaj z naszą implementacją (nagłówki, payloady, kody błędów). Wypisz różnice.
3. Przygotuj curl do rejestracji webhooka: POST /banks/webhook-config (sprawdź dokładny
   payload w docs KLIK; jeśli niejasny — wypisz pytania do zespołu KLIK).
4. Upewnij się, że /klik/webhook/ping poprawnie echo-uje timestamp+nonce — KLIK tym weryfikuje
   onboarding.
Nic nie deployuj bez mojego potwierdzenia.
```

## 8. Checklist przed PR

- [ ] `dotnet build` i `dotnet test` zielone
- [ ] Migracja działa na czystej bazie (docker compose down -v && up)
- [ ] Webhook /authorize odpowiada < 1s (KLIK limit 30s, ale nie ryzykujemy)
- [ ] Confirm wysyłany w oknie 60s; po oknie → lokalny status timeout, brak confirma
- [ ] Idempotency-Key na generate i confirm
- [ ] README zaktualizowane (porty: mock KLIK 6005)
- [ ] Junior NIE ma dostępu do BLIK (role=junior → 403)

## 9. Otwarte pytania do zespołu KLIK (zapisz na Trello)

1. Dokładny payload `POST /banks/webhook-config` (nie ma go w INFO.md).
2. Czy ich instancja (klik.on-labs.dev) jest już dostępna do testów i jak dostać api_key dla strefy US.
3. Czy webhook musi być HTTPS (Cloudflare Tunnel da radę).
4. Harmonogram integracji P2P — drugi wymagany moduł.
