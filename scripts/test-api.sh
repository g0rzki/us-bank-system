#!/usr/bin/env bash
# test-api.sh — komprehensywny test wszystkich endpointów us-bank-system
# Wymaga: curl, python3 (lub python)
# Użycie: bash test-api.sh [BASE_URL] [WEBHOOK_SECRET]

set -uo pipefail

# Załaduj .env jeśli istnieje (strip \r dla Windows CRLF; obsłuż wartości ze spacją)
if [ -f "$(dirname "$0")/../.env" ]; then
  while IFS= read -r line; do
    line="${line//$'\r'/}"
    [[ "$line" =~ ^[[:space:]]*(#|$) ]] && continue
    [[ "$line" != *=* ]] && continue
    key="${line%%=*}"
    value="${line#*=}"
    export "$key=$value"
  done < "$(dirname "$0")/../.env"
fi

BASE_URL="${1:-${API_URL:-http://localhost:5100}}"
WEBHOOK_SECRET="${2:-${WEBHOOK_SECRET:-dev_webhook_secret}}"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; CYAN='\033[0;36m'; BOLD='\033[1m'; NC='\033[0m'

PASS=0; FAIL=0; SKIP=0

# ─── Helpery ─────────────────────────────────────────────────────────────────

section() { echo -e "\n${BLUE}${BOLD}━━━━━━  $1  ━━━━━━${NC}"; }
ok()   { echo -e "  ${GREEN}✓${NC} $1"; PASS=$((PASS+1)); }
fail() { echo -e "  ${RED}✗${NC} $1"; [ -n "${2:-}" ] && echo -e "    ${YELLOW}↳ $(echo "$2" | head -c 200)${NC}"; FAIL=$((FAIL+1)); }
skip() { echo -e "  ${YELLOW}⊘${NC} $1 (pominięto: $2)"; SKIP=$((SKIP+1)); }
info() { echo -e "  ${CYAN}·${NC} $1"; }

check() {
  local desc="$1" expected="$2" actual="$3" body="${4:-}"
  if [ "$actual" = "$expected" ]; then
    ok "$desc (HTTP $actual)"
  else
    fail "$desc — oczekiwano HTTP $expected, dostano $actual" "$body"
  fi
}

check_any() {
  local desc="$1" expected="$2" actual="$3" body="${4:-}"
  if echo "$expected" | grep -qw "$actual"; then
    ok "$desc (HTTP $actual)"
  else
    fail "$desc — oczekiwano HTTP $expected, dostano $actual" "$body"
  fi
}

req() {
  local method="$1" path="$2"; shift 2
  local tmp; tmp=$(mktemp)
  local status
  status=$(curl -s -o "$tmp" -w "%{http_code}" -X "$method" \
    -H "Content-Type: application/json" "$@" \
    "${BASE_URL}${path}" 2>/dev/null) || status="000"
  local body; body=$(cat "$tmp"); rm -f "$tmp"
  printf '%s|%s' "$status" "$body"
}

status() { echo "${1%%|*}"; }
body()   { echo "${1#*|}"; }

if command -v python3 &>/dev/null; then PY=python3
elif command -v python &>/dev/null; then PY=python
else PY=""; fi

jget() {
  local json="$1" expr="$2"
  if [ -n "$PY" ]; then
    case "$expr" in
      'length') printf '%s' "$json" | $PY -c "import sys,json; d=json.load(sys.stdin); print(len(d))" 2>/dev/null || echo "0" ;;
      *) local key="${expr#.}"
         printf '%s' "$json" | $PY -c "import sys,json; d=json.load(sys.stdin); v=d.get('$key',''); print('' if v is None else v)" 2>/dev/null || echo "" ;;
    esac
    return
  fi
  # sed/grep fallback — works for scalar string and simple numeric values
  case "$expr" in
    'length')
      printf '%s' "$json" | grep -o '"id"' | wc -l | tr -d ' '
      ;;
    *)
      local key="${expr#.}" val
      val=$(printf '%s' "$json" | grep -oE "\"${key}\":\"[^\"]*\"" | head -1 | sed "s/\"${key}\"://;s/^\"\(.*\)\"$/\1/")
      if [ -z "$val" ]; then
        val=$(printf '%s' "$json" | grep -oE "\"${key}\":[0-9a-zA-Z._-]+" | head -1 | sed "s/\"${key}\"://")
      fi
      printf '%s\n' "$val"
      ;;
  esac
}

# ─── Środowisko ──────────────────────────────────────────────────────────────

echo -e "${BOLD}us-bank-system API Test${NC}"
echo -e "URL: ${CYAN}${BASE_URL}${NC}"
echo ""

if ! command -v curl &>/dev/null; then echo -e "${RED}BŁĄD: curl nie jest zainstalowane.${NC}"; exit 1; fi
[ -z "$PY" ] && echo -e "${YELLOW}UWAGA: brak python3/python — niektóre funkcje ograniczone${NC}"

R=$(req GET /health)
if [ "$(status "$R")" != "200" ]; then echo -e "${RED}API nie odpowiada pod ${BASE_URL}${NC}"; exit 1; fi
echo -e "${GREEN}API działa${NC}"

# Adresy integracji (z env lub z domyślnych)
ACH_URL="${INTEGRATIONS_ACH_URL:-http://localhost:8310}"
CARDS_URL="${INTEGRATIONS_CARDS_URL_HOST:-${INTEGRATIONS_CARDS_URL:-http://localhost:8072}}"
RTP_URL="${INTEGRATIONS_RTP_URL:-http://localhost:6002}"
FEDNOW_URL="${INTEGRATIONS_FEDNOW_URL:-http://localhost:6003}"
SWIFT_URL=$(echo "${INTEGRATIONS_SWIFT_URL:-http://localhost:3000}" | sed 's|host.docker.internal|localhost|')
SFTP_HOST="${Ach__Sftp__Host:-localhost}"
SFTP_PORT="${Ach__Sftp__Port:-2221}"

# ─── Stałe seededowe ID ──────────────────────────────────────────────────────

JOHN_CHECKING="aaaa1111-1111-1111-1111-111111111111"
JOHN_SAVINGS="aaaa1111-2222-2222-2222-222222222222"
JANE_CHECKING="bbbb2222-1111-1111-1111-111111111111"
BOB_CHECKING="cccc3333-1111-1111-1111-111111111111"

JOHN_CHECKING_NUM="1000000001"
JOHN_SAVINGS_NUM="1000000002"
JANE_CHECKING_NUM="2000000001"
BOB_CHECKING_NUM="3000000001"

JUNIOR_ACC_1="dddd4444-1111-1111-1111-111111111111"

TR_COMPLETED="bbbb0001-0000-0000-0000-000000000001"
TR_ACH_PENDING="bbbb0001-0000-0000-0000-000000000007"
TR_SWIFT_PENDING="bbbb0001-0000-0000-0000-000000000008"

TOKEN_JOHN=""; TOKEN_JANE=""; TOKEN_BOB=""; TOKEN_EMMA=""
TOKEN_TEST=""
TEST_ACCOUNT_ID=""; TEST_ACCOUNT_NUM=""
TEST_CARD_DEBIT_ID=""; TEST_CARD_PREPAID_ID=""
ACH_TR_ID=""; NEW_JUNIOR_ID=""
ACH_HELPER_UP=false; SFTP_UP=false; CARDS_GW_UP=false
JOHN_USER_ID=""
KLIK_SECRET="${KLIK_WEBHOOK_SECRET:-changeme_klik_webhook_secret}"

TS=$(date +%s)

# ═══════════════════════════════════════════════════════════════════════════════
section "1 · HEALTH"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req GET /health)
check "GET /health" 200 "$(status "$R")" "$(body "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "2 · CONNECTIVITY — zewnętrzne integracje"
# ═══════════════════════════════════════════════════════════════════════════════

# Sprawdź czy HTTP endpoint odpowiada (dowolny kod != 000 = UP)
http_up() {
  local url="$1"
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 3 --max-time 5 "${url}" 2>/dev/null) || code="000"
  [ "$code" != "000" ]
}

# Sprawdź port TCP przez Python socket
port_open() {
  local host="$1" port="$2"
  if [ -n "$PY" ]; then
    $PY -c "
import socket, sys
s = socket.socket()
s.settimeout(3)
try:
    s.connect(('$host', $port))
    s.close()
    sys.exit(0)
except Exception:
    sys.exit(1)
" 2>/dev/null
  else
    # bash /dev/tcp fallback (Git Bash on Windows)
    ( exec 3<>/dev/tcp/"$host"/"$port" ) 2>/dev/null
  fi
}

# --- ACH Helper ---
info "ACH Helper: ${ACH_URL}"
if http_up "${ACH_URL}"; then
  ok "ACH Helper — port/HTTP dostępny"
  ACH_HELPER_UP=true

  # Bezpośredni test POST /json-to-ach — struktura z AchGateway.cs
  TOMORROW_DATE=$($PY -c "from datetime import date,timedelta; print((date.today()+timedelta(days=1)).strftime('%y%m%d'))" 2>/dev/null || echo "260616")
  ACH_TEST_PAYLOAD="{\"data\":{\"header\":{\"immediate_destination\":\"090000515\",\"immediate_origin\":\"110000000\",\"immediate_destination_name\":\"FRB Tungsten\",\"immediate_origin_name\":\"US Bank A\",\"file_id_modifier\":\"T\"},\"batches\":[{\"header\":{\"company_name\":\"US Bank A\",\"company_identification\":\"110000000\",\"standard_entry_class_code\":\"PPD\",\"company_entry_description\":\"TRANSFER\",\"effective_entry_date\":\"${TOMORROW_DATE}\",\"originating_dfi_identification\":\"11000000\"},\"entries\":[{\"transaction_code\":\"22\",\"receiving_dfi_rtn\":\"021000021\",\"dfi_account_number\":\"987654321\",\"amount_cents\":100,\"individual_name\":\"CONN TEST\",\"trace_number\":\"110000000000001\"}]}]}}"
  ACH_TMP=$(mktemp)
  ACH_CODE=$(curl -s -o "$ACH_TMP" -w "%{http_code}" -X POST \
    -H "Content-Type: application/json" \
    -d "${ACH_TEST_PAYLOAD}" \
    "${ACH_URL}/json-to-ach" --connect-timeout 5 --max-time 10 2>/dev/null) || ACH_CODE="000"
  ACH_BODY=$(cat "$ACH_TMP"); rm -f "$ACH_TMP"

  if [ "$ACH_CODE" = "200" ]; then
    ok "ACH Helper POST /json-to-ach → 200 (NACHA wygenerowany)"
    # Pierwsza linia pliku NACHA zaczyna się od '1' (File Header Record)
    FIRST_CHAR=$(printf '%s' "$ACH_BODY" | head -c 1)
    if [ "$FIRST_CHAR" = "1" ]; then
      ok "ACH Helper — odpowiedź to poprawny plik NACHA (zaczyna się od '1')"
    else
      fail "ACH Helper — odpowiedź nie wygląda jak plik NACHA" "${ACH_BODY:0:100}"
    fi
  else
    fail "ACH Helper POST /json-to-ach — oczekiwano 200, dostano ${ACH_CODE}" "${ACH_BODY:0:200}"
  fi

  # Błędny payload → powinno zwrócić błąd (nie 200)
  ACH_TMP2=$(mktemp)
  ACH_BAD=$(curl -s -o "$ACH_TMP2" -w "%{http_code}" -X POST \
    -H "Content-Type: application/json" \
    -d '{"data":{}}' \
    "${ACH_URL}/json-to-ach" --connect-timeout 5 --max-time 10 2>/dev/null) || ACH_BAD="000"
  ACH_BAD_BODY=$(cat "$ACH_TMP2"); rm -f "$ACH_TMP2"
  if [ "$ACH_BAD" != "200" ] && [ "$ACH_BAD" != "000" ]; then
    ok "ACH Helper /json-to-ach — błędny payload → HTTP ${ACH_BAD} (walidacja działa)"
  elif [ "$ACH_BAD" = "000" ]; then
    fail "ACH Helper — brak odpowiedzi na błędny payload"
  else
    fail "ACH Helper — błędny payload zwrócił 200 (brak walidacji?)" "${ACH_BAD_BODY:0:100}"
  fi
else
  fail "ACH Helper (${ACH_URL}) — NIEDOSTĘPNY (brak połączenia)"
  skip "ACH Helper POST /json-to-ach — poprawny payload" "serwis niedostępny"
  skip "ACH Helper POST /json-to-ach — błędny payload" "serwis niedostępny"
fi

# --- SFTP ---
info "SFTP: ${SFTP_HOST}:${SFTP_PORT}"
if port_open "${SFTP_HOST}" "${SFTP_PORT}"; then
  ok "SFTP (${SFTP_HOST}:${SFTP_PORT}) — port otwarty"
  SFTP_UP=true
else
  fail "SFTP (${SFTP_HOST}:${SFTP_PORT}) — port niedostępny"
fi

# --- Cards Gateway ---
info "Cards Gateway: ${CARDS_URL}"
if http_up "${CARDS_URL}"; then
  ok "Cards Gateway (${CARDS_URL}) — dostępny"
  CARDS_GW_UP=true
else
  fail "Cards Gateway (${CARDS_URL}) — NIEDOSTĘPNY"
fi

# --- Pozostałe gateways ---
for entry in "RTP|${RTP_URL}" "FedNow|${FEDNOW_URL}" "SWIFT|${SWIFT_URL}"; do
  GW_NAME="${entry%%|*}"
  GW_URL="${entry#*|}"
  info "Gateway ${GW_NAME}: ${GW_URL}"
  if http_up "${GW_URL}"; then
    ok "Gateway ${GW_NAME} (${GW_URL}) — dostępny"
  else
    fail "Gateway ${GW_NAME} (${GW_URL}) — NIEDOSTĘPNY"
  fi
done

# ═══════════════════════════════════════════════════════════════════════════════
section "3 · AUTH — register"
# ═══════════════════════════════════════════════════════════════════════════════

NEW_EMAIL="test.user.${TS}@example.com"

R=$(req POST /auth/register -d "{\"email\":\"${NEW_EMAIL}\",\"password\":\"SecurePass1!\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
check "POST /auth/register — nowy użytkownik → 201" 201 "$(status "$R")" "$(body "$R")"

R=$(req POST /auth/register -d "{\"email\":\"${NEW_EMAIL}\",\"password\":\"SecurePass1!\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
check "POST /auth/register — duplikat email → 409" 409 "$(status "$R")"

R=$(req POST /auth/register -d "{\"password\":\"SecurePass1!\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
check "POST /auth/register — brak email → 400" 400 "$(status "$R")"

R=$(req POST /auth/register -d "{\"email\":\"shortpw.${TS}@example.com\",\"password\":\"short\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
check "POST /auth/register — hasło < 8 znaków → 400" 400 "$(status "$R")"

R=$(req POST /auth/register -d "{\"email\":\"notanemail\",\"password\":\"SecurePass1!\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
check "POST /auth/register — zły format email → 400" 400 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "4 · AUTH — login"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req POST /auth/login -d '{"email":"john.doe@example.com","password":"Test123!"}')
check "POST /auth/login — john.doe → 200" 200 "$(status "$R")"
TOKEN_JOHN=$(jget "$(body "$R")" '.token')

R=$(req POST /auth/login -d '{"email":"jane.smith@example.com","password":"Test123!"}')
check "POST /auth/login — jane.smith → 200" 200 "$(status "$R")"
TOKEN_JANE=$(jget "$(body "$R")" '.token')

R=$(req POST /auth/login -d '{"email":"bob.wilson@example.com","password":"Test123!"}')
check "POST /auth/login — bob.wilson → 200" 200 "$(status "$R")"
TOKEN_BOB=$(jget "$(body "$R")" '.token')

R=$(req POST /auth/login -d '{"email":"emma.doe@example.com","password":"Test123!"}')
check "POST /auth/login — emma.doe (junior) → 200" 200 "$(status "$R")"
TOKEN_EMMA=$(jget "$(body "$R")" '.token')

R=$(req POST /auth/login -d "{\"email\":\"${NEW_EMAIL}\",\"password\":\"SecurePass1!\"}")
check "POST /auth/login — nowy user z rejestracji → 200" 200 "$(status "$R")"
TOKEN_TEST=$(jget "$(body "$R")" '.token')

R=$(req POST /auth/login -d '{"email":"john.doe@example.com","password":"WrongPassword1"}')
check "POST /auth/login — złe hasło → 401" 401 "$(status "$R")"

R=$(req POST /auth/login -d '{"email":"nobody@example.com","password":"Test123!"}')
check "POST /auth/login — nieznany email → 401" 401 "$(status "$R")"

R=$(req POST /auth/login -d '{"email":"john.doe@example.com"}')
check "POST /auth/login — brak hasła → 400" 400 "$(status "$R")"

R=$(req POST /auth/login -d '{}')
check "POST /auth/login — puste ciało → 400" 400 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "5 · AUTH — me"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req GET /auth/me -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /auth/me — poprawny token → 200" 200 "$(status "$R")"
JOHN_USER_ID=$(jget "$(body "$R")" '.id')

R=$(req GET /auth/me)
check "GET /auth/me — brak tokenu → 401" 401 "$(status "$R")"

R=$(req GET /auth/me -H "Authorization: Bearer invalid.token.here")
check "GET /auth/me — niepoprawny token → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "6 · SETUP — konto testowego użytkownika"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req POST /accounts -H "Authorization: Bearer ${TOKEN_TEST}" \
  -d '{"type":"checking","currency":"USD"}')
check "POST /accounts — test user checking → 201" 201 "$(status "$R")" "$(body "$R")"
TEST_ACCOUNT_ID=$(jget "$(body "$R")" '.id')
TEST_ACCOUNT_NUM=$(jget "$(body "$R")" '.accountNumber')
info "Test account ID: ${TEST_ACCOUNT_ID}"

# ═══════════════════════════════════════════════════════════════════════════════
section "7 · ACCOUNTS — list & create"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req GET /accounts -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts — john → 200" 200 "$(status "$R")"
info "Konta john: $(jget "$(body "$R")" 'length')"

R=$(req GET /accounts)
check "GET /accounts — brak tokenu → 401" 401 "$(status "$R")"

R=$(req POST /accounts -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"savings","currency":"USD"}')
check "POST /accounts — test user savings → 201" 201 "$(status "$R")"

R=$(req POST /accounts -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"checking","currency":"USD"}')
check "POST /accounts — test user checking #2 → 201" 201 "$(status "$R")"

R=$(req POST /accounts -H "Authorization: Bearer ${TOKEN_BOB}" -d '{"type":"investment"}')
check "POST /accounts — typ 'investment' → 400" 400 "$(status "$R")"

R=$(req POST /accounts -H "Authorization: Bearer ${TOKEN_BOB}" -d '{}')
check "POST /accounts — brak type → 400" 400 "$(status "$R")"

R=$(req POST /accounts)
check "POST /accounts — brak tokenu → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "8 · ACCOUNTS — get / balance / transactions"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req GET "/accounts/${JOHN_CHECKING}" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id} — własne → 200" 200 "$(status "$R")"

R=$(req GET "/accounts/${JANE_CHECKING}" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id} — cudze → 401" 401 "$(status "$R")"

R=$(req GET "/accounts/00000000-0000-0000-0000-000000000000" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id} — nie istnieje → 404" 404 "$(status "$R")"

R=$(req GET "/accounts/${JOHN_CHECKING}")
check "GET /accounts/{id} — brak tokenu → 401" 401 "$(status "$R")"

R=$(req GET "/accounts/${JOHN_CHECKING}/balance" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/balance — własne → 200" 200 "$(status "$R")"
info "Saldo john checking: \$$(jget "$(body "$R")" '.balance')"

R=$(req GET "/accounts/${JANE_CHECKING}/balance" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/balance — cudze → 401" 401 "$(status "$R")"

R=$(req GET "/accounts/00000000-0000-0000-0000-000000000000/balance" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/balance — nie istnieje → 404" 404 "$(status "$R")"

R=$(req GET "/accounts/${JOHN_CHECKING}/transactions" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/transactions — strona 1 → 200" 200 "$(status "$R")"
info "Transakcji john checking: $(jget "$(body "$R")" '.total')"

R=$(req GET "/accounts/${JOHN_CHECKING}/transactions?page=2&pageSize=5" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/transactions — page=2, pageSize=5 → 200" 200 "$(status "$R")"

R=$(req GET "/accounts/${JANE_CHECKING}/transactions" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/transactions — cudze → 401" 401 "$(status "$R")"

R=$(req GET "/accounts/${JOHN_CHECKING}/junior-accounts" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /accounts/{id}/junior-accounts — john → 200" 200 "$(status "$R")"
info "Junior accounts pod john: $(jget "$(body "$R")" 'length')"

R=$(req GET "/accounts/${JOHN_CHECKING}/junior-accounts" -H "Authorization: Bearer ${TOKEN_JANE}")
check "GET /accounts/{id}/junior-accounts — cudze → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "9 · ACCOUNTS — junior"
# ═══════════════════════════════════════════════════════════════════════════════

JR_EMAIL="junior.${TS}@example.com"
R=$(req POST /accounts/junior -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"parentAccountId\":\"${JOHN_CHECKING}\",
  \"email\":\"${JR_EMAIL}\",
  \"password\":\"Junior123!\",
  \"firstName\":\"Junior\",\"lastName\":\"Test\",\"dateOfBirth\":\"2015-06-15\"
}")
check "POST /accounts/junior — utwórz → 201" 201 "$(status "$R")" "$(body "$R")"
NEW_JUNIOR_ID=$(jget "$(body "$R")" '.accountId')
info "Nowy junior ID: ${NEW_JUNIOR_ID}"

R=$(req POST /accounts/junior -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"parentAccountId\":\"${JOHN_CHECKING}\",
  \"email\":\"${JR_EMAIL}\",
  \"password\":\"Junior123!\",
  \"firstName\":\"Junior\",\"lastName\":\"Test\",\"dateOfBirth\":\"2015-06-15\"
}")
check "POST /accounts/junior — duplikat email → 409" 409 "$(status "$R")"

R=$(req POST /accounts/junior -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"parentAccountId\":\"${JANE_CHECKING}\",
  \"email\":\"jrnoaccess.${TS}@example.com\",
  \"password\":\"Junior123!\",\"firstName\":\"X\",\"lastName\":\"Y\",\"dateOfBirth\":\"2015-01-01\"
}")
check "POST /accounts/junior — cudzy parent → 404" 404 "$(status "$R")"

R=$(req POST /accounts/junior -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"parentAccountId\":\"${JOHN_CHECKING}\",
  \"email\":\"jrnodob.${TS}@example.com\",
  \"password\":\"Junior123!\",\"firstName\":\"X\",\"lastName\":\"Y\"
}")
check "POST /accounts/junior — brak dateOfBirth → 400" 400 "$(status "$R")"

R=$(req PATCH "/accounts/${JUNIOR_ACC_1}/junior-limit" -H "Authorization: Bearer ${TOKEN_JOHN}" \
  -d '{"dailyLimit":100.00,"monthlyLimit":500.00}')
check "PATCH /accounts/{id}/junior-limit — update → 200" 200 "$(status "$R")"

R=$(req PATCH "/accounts/${JUNIOR_ACC_1}/junior-limit" -H "Authorization: Bearer ${TOKEN_JOHN}" \
  -d '{"dailyLimit":0.001}')
check "PATCH /accounts/{id}/junior-limit — dailyLimit < 0.01 → 400" 400 "$(status "$R")"

R=$(req PATCH "/accounts/${JUNIOR_ACC_1}/junior-limit" -H "Authorization: Bearer ${TOKEN_JANE}" \
  -d '{"dailyLimit":50.00}')
check "PATCH /accounts/{id}/junior-limit — cudzy junior → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "10 · TRANSFERS — list & status"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req GET /transfers -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /transfers — john → 200" 200 "$(status "$R")"
info "Transfery john: $(jget "$(body "$R")" 'length')"

R=$(req GET /transfers)
check "GET /transfers — brak tokenu → 401" 401 "$(status "$R")"

R=$(req GET /transfers/pending-approval -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /transfers/pending-approval — john → 200" 200 "$(status "$R")"

R=$(req GET /transfers/pending-approval -H "Authorization: Bearer ${TOKEN_BOB}")
check "GET /transfers/pending-approval — bob (brak juniorów) → 200 []" 200 "$(status "$R")"

R=$(req GET "/transfers/${TR_ACH_PENDING}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /transfers/{id}/status — własny → 200" 200 "$(status "$R")"

R=$(req GET "/transfers/${TR_ACH_PENDING}/status" -H "Authorization: Bearer ${TOKEN_JANE}")
check "GET /transfers/{id}/status — cudzy → 404" 404 "$(status "$R")"

R=$(req GET "/transfers/00000000-0000-0000-0000-000000000000/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /transfers/{id}/status — nie istnieje → 404" 404 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "11 · TRANSFERS — internal"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":10.00,\"currency\":\"USD\",\"description\":\"Test\"
}")
check "POST /transfers/internal — happy path → 201" 201 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",\"toAccountId\":\"${JOHN_CHECKING}\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — to samo konto → 400" 400 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":9999999.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — brak środków → 400" 400 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":10.00,\"currency\":\"EUR\"
}")
check "POST /transfers/internal — waluta EUR → 400" 400 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":-5.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — ujemna kwota → 400" 400 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":0,\"currency\":\"USD\"
}")
check "POST /transfers/internal — kwota = 0 → 400" 400 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"9999999999\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — nieznany numer konta → 404" 404 "$(status "$R")"

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JANE_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — cudze konto źródłowe → 404" 404 "$(status "$R")"

R=$(req POST /transfers/internal -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — brak tokenu → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "12 · TRANSFERS — ACH (pełne pokrycie)"
# ═══════════════════════════════════════════════════════════════════════════════
# AchPaymentService: waliduje walutę, saldo, konto źródłowe (→ 400/404 bez gateway).
# AchGateway: konwertuje do NACHA (POST /json-to-ach) → upload SFTP.
# Jeśli ACH helper lub SFTP niedostępny → transfer.Status=Failed → API zwraca 400.

if $ACH_HELPER_UP && $SFTP_UP; then
  info "ACH Helper i SFTP UP — happy path 201|400 (400 = NACHA daily limit 36/dzień)"
else
  info "ACH Helper=$ACH_HELPER_UP / SFTP=$SFTP_UP — offline, happy path = 201|400"
fi

# --- Happy path: checking ---
R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\",\"description\":\"ACH test\"
}")
check_any "POST /transfers/ach — checking → 201|400 (daily limit max 36/dzień)" "201|400" "$(status "$R")" "$(body "$R")"
ACH_TR_ID=$(jget "$(body "$R")" '.id')
info "ACH transfer ID: ${ACH_TR_ID:-brak (daily limit wyczerpany)}"

# --- Happy path: savings ---
R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_SAVINGS}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"111222333\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":25.00,\"currency\":\"USD\",\"description\":\"ACH savings\"
}")
check_any "POST /transfers/ach — savings account → 201|400" "201|400" "$(status "$R")"

# --- Minimalny amount (0.01) ---
R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"111222333\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":0.01,\"currency\":\"USD\"
}")
check_any "POST /transfers/ach — amount=0.01 (minimum) → 201|400" "201|400" "$(status "$R")"

# --- Bez description (opcjonalne) ---
R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"555666777\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check_any "POST /transfers/ach — bez description → 201|400" "201|400" "$(status "$R")"

# --- Długi description (AchGateway truncuje do 22 znaków w NACHA) ---
R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"555666777\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":5.00,\"currency\":\"USD\",
  \"description\":\"Very long description exceeding the 22-char NACHA field limit\"
}")
check_any "POST /transfers/ach — długi description → 201|400" "201|400" "$(status "$R")"

# --- Junior tworzy ACH → pending_approval (nie idzie do SFTP, zawsze 201) ---
if [ -n "$TOKEN_EMMA" ]; then
  R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_EMMA}" -d "{
    \"fromAccountId\":\"${JUNIOR_ACC_1}\",
    \"toRoutingNumber\":\"021000021\",
    \"toAccountNumber\":\"987654321\",
    \"recipientName\":\"Jane Doe\",
    \"amount\":1.00,\"currency\":\"USD\",\"description\":\"Junior ACH\"
  }")
  check "POST /transfers/ach — junior → 409 (zewnętrzny ACH zablokowany)" 409 "$(status "$R")" "$(body "$R")"
  info "Junior ACH status: $(jget "$(body "$R")" '.status')"
else
  skip "POST /transfers/ach — junior pending_approval" "brak TOKEN_EMMA"
fi

# === Walidacje pól (zawsze 400, bez gateway) ===

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — brak toRoutingNumber → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — brak toAccountNumber → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check_any "POST /transfers/ach — brak fromAccountId → 400 lub 404" "400|404" "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"currency\":\"USD\"
}")
check "POST /transfers/ach — brak amount → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":0,\"currency\":\"USD\"
}")
check "POST /transfers/ach — amount = 0 → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":-10.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — ujemna kwota → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":0.001,\"currency\":\"USD\"
}")
check "POST /transfers/ach — amount=0.001 (poniżej minimum) → 400" 400 "$(status "$R")"

# === Walidacja waluty ===

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"GBP\"
}")
check "POST /transfers/ach — waluta GBP → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"EUR\"
}")
check "POST /transfers/ach — waluta EUR → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"\"
}")
check "POST /transfers/ach — pusta waluta → 400" 400 "$(status "$R")"

# === Walidacja konta źródłowego ===

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_BOB}" -d "{
  \"fromAccountId\":\"${BOB_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":99999.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — brak środków → 400" 400 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"00000000-0000-0000-0000-000000000000\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — fromAccountId nie istnieje → 404" 404 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JANE_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — cudze fromAccountId → 404" 404 "$(status "$R")"

R=$(req POST /transfers/ach -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"021000021\",
  \"toAccountNumber\":\"987654321\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":50.00,\"currency\":\"USD\"
}")
check "POST /transfers/ach — brak tokenu → 401" 401 "$(status "$R")"

R=$(req POST /transfers/ach -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/ach — brak body → 400" 400 "$(status "$R")"

# === Sprawdź że transfer widoczny w historii ===
if [ -n "$ACH_TR_ID" ]; then
  R=$(req GET "/transfers/${ACH_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /transfers/{achId}/status — widoczny po utworzeniu → 200" 200 "$(status "$R")"
  info "Status ACH transferu: $(jget "$(body "$R")" '.status')"
else
  skip "GET /transfers/{achId}/status" "brak ACH_TR_ID"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "13 · TRANSFERS — RTP"
# ═══════════════════════════════════════════════════════════════════════════════
# RTP ma dwa tryby:
#   internal — brak toRoutingNumber → trafia do RtpGateway (mock port 6002)
#   external — z toRoutingNumber   → trafia do RtpTchGateway (TCH, localhost:8200)

RTP_TR_ID=""

# ── Happy path internal (do konta w banku, przez mock) ────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"USD\",\"description\":\"RTP test\"
}")
check "POST /transfers/rtp — internal happy path → 201" 201 "$(status "$R")"
RTP_TR_ID=$(jget "$(body "$R")" '.id')
info "RTP transfer ID: ${RTP_TR_ID:-brak}"

# ── Happy path external (z toRoutingNumber → TCH, może być niedostępne) ──────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"987654321\",
  \"toRoutingNumber\":\"021000021\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":2.00,\"currency\":\"USD\",\"description\":\"RTP external\"
}")
check_any "POST /transfers/rtp — external (TCH) → 201|400 (TCH może być off)" "201|400" "$(status "$R")"

# ── Walidacja kwoty ───────────────────────────────────────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":-1.00
}")
check "POST /transfers/rtp — ujemna kwota → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":0
}")
check "POST /transfers/rtp — kwota = 0 → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":0.001
}")
check "POST /transfers/rtp — amount=0.001 (poniżej minimum) → 400" 400 "$(status "$R")"

# ── Walidacja waluty ──────────────────────────────────────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"EUR\"
}")
check "POST /transfers/rtp — waluta EUR → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"GBP\"
}")
check "POST /transfers/rtp — waluta GBP → 400" 400 "$(status "$R")"

# ── Saldo / konto źródłowe ────────────────────────────────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_BOB}" -d "{
  \"fromAccountId\":\"${BOB_CHECKING}\",
  \"toAccountNumber\":\"${JOHN_CHECKING_NUM}\",
  \"amount\":99999.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — brak środków → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"00000000-0000-0000-0000-000000000000\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — fromAccountId nie istnieje → 404" 404 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JANE_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — cudze fromAccountId → 404" 404 "$(status "$R")"

# ── Internal — błędy na koncie docelowym ─────────────────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JOHN_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — internal to samo konto → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"9999999999\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — internal nieznany numer konta → 404" 404 "$(status "$R")"

# ── Pola wymagane ─────────────────────────────────────────────────────────────
R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",\"amount\":5.00
}")
check "POST /transfers/rtp — brak toAccountNumber → 400" 400 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",\"amount\":5.00
}")
check_any "POST /transfers/rtp — brak fromAccountId → 400|404" "400|404" "$(status "$R")"

# ── Auth ──────────────────────────────────────────────────────────────────────
R=$(req POST /transfers/rtp -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":5.00,\"currency\":\"USD\"
}")
check "POST /transfers/rtp — brak tokenu → 401" 401 "$(status "$R")"

R=$(req POST /transfers/rtp -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/rtp — brak body → 400" 400 "$(status "$R")"

# ── Status po utworzeniu ──────────────────────────────────────────────────────
if [ -n "$RTP_TR_ID" ]; then
  R=$(req GET "/transfers/${RTP_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /transfers/{rtpId}/status — po wysłaniu → 200" 200 "$(status "$R")"
  info "RTP status: $(jget "$(body "$R")" '.status')"
else
  skip "GET /transfers/{rtpId}/status" "brak RTP_TR_ID"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "14 · TRANSFERS — FedNow"
# ═══════════════════════════════════════════════════════════════════════════════
# FedNow wysyła pacs.008 przez FedNow MQ Gateway (INTEGRATIONS_FEDNOW_MQ_URL).
# Jeśli zewnętrzny FedSystems niedostępny → transfer.Status=Failed → 400.

FEDNOW_TR_ID=""

fednow_check() {
  local desc="$1" st="$2" bd="${3:-}"
  check_any "${desc} → 201|400 (MQ może być off)" "201|400" "${st}" "${bd}"
}

# ── Happy path ────────────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":15.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — happy path → 201" 201 "$(status "$R")"
FEDNOW_TR_ID=$(jget "$(body "$R")" '.id')
info "FedNow transfer ID: ${FEDNOW_TR_ID:-brak}"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"987654321\",
  \"toRoutingNumber\":\"021000021\",
  \"recipientName\":\"Jane Doe\",
  \"amount\":0.01,\"currency\":\"USD\",\"description\":\"FedNow min amount\"
}")
fednow_check "POST /transfers/fednow — amount=0.01 (min) z recipientName+opis" "$(status "$R")"

# ── Saldo ─────────────────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_BOB}" -d "{
  \"fromAccountId\":\"${BOB_CHECKING}\",
  \"toAccountNumber\":\"${JOHN_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":99999.00
}")
check "POST /transfers/fednow — brak środków → 400" 400 "$(status "$R")"

# ── Walidacja pól wymaganych ──────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — brak toRoutingNumber → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — brak toAccountNumber → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check_any "POST /transfers/fednow — brak fromAccountId → 400|404" "400|404" "$(status "$R")"

# ── Walidacja kwoty ───────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":0,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — kwota = 0 → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":-5.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — ujemna kwota → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":0.001,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — amount=0.001 (poniżej minimum) → 400" 400 "$(status "$R")"

# ── Walidacja waluty ──────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"EUR\"
}")
check "POST /transfers/fednow — waluta EUR → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"GBP\"
}")
check "POST /transfers/fednow — waluta GBP → 400" 400 "$(status "$R")"

# ── Konto źródłowe ────────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"00000000-0000-0000-0000-000000000000\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — fromAccountId nie istnieje → 404" 404 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JANE_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — cudze fromAccountId → 404" 404 "$(status "$R")"

# ── Auth ──────────────────────────────────────────────────────────────────────
R=$(req POST /transfers/fednow -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountNumber\":\"${BOB_CHECKING_NUM}\",
  \"toRoutingNumber\":\"010101012\",
  \"amount\":10.00,\"currency\":\"USD\"
}")
check "POST /transfers/fednow — brak tokenu → 401" 401 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/fednow — brak body → 400" 400 "$(status "$R")"

# ── Status po utworzeniu ──────────────────────────────────────────────────────
if [ -n "$FEDNOW_TR_ID" ]; then
  R=$(req GET "/transfers/${FEDNOW_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /transfers/{fednowId}/status — po wysłaniu → 200" 200 "$(status "$R")"
  info "FedNow status: $(jget "$(body "$R")" '.status')"
else
  skip "GET /transfers/{fednowId}/status" "brak FEDNOW_TR_ID"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "15 · TRANSFERS — SWIFT"
# ═══════════════════════════════════════════════════════════════════════════════
# BIC-i i IBAN-y zgodne z ACCOUNT_DIRECTORY SWIFT Middleware (istniejące konta)

SWIFT_BIC_PL="PLBKPL01XXX"; SWIFT_IBAN_PL="PL61109010140000071219812874"
SWIFT_BIC_DE="DEBKDE01XXX"; SWIFT_IBAN_DE="DE89370400440532013000"
SWIFT_BIC_UK="UKBKGB01XXX"; SWIFT_IBAN_UK="GB29NWBK60161331926819"
SWIFT_IBAN_CLOSED_UK="GB00CLOSED0000000000000000"
SWIFT_TR_ID=""; SWIFT_UETR=""

SWIFT_GW_URL=$(echo "${INTEGRATIONS_SWIFT_URL:-http://localhost:3000}" | sed 's|host.docker.internal|localhost|')
SWIFT_GW_UP=false
if http_up "${SWIFT_GW_URL}"; then
  ok "SWIFT Middleware (${SWIFT_GW_URL}) — dostępny"
  SWIFT_GW_UP=true
else
  fail "SWIFT Middleware (${SWIFT_GW_URL}) — NIEDOSTĘPNY (happy path → 400)"
fi

swift_check() {
  local desc="$1" st="$2" bd="${3:-}"
  if $SWIFT_GW_UP; then
    check "${desc} → 201" 201 "${st}" "${bd}"
  else
    check_any "${desc} → 201|400 (gw off)" "201|400" "${st}" "${bd}"
  fi
}

NEXT_YEAR=$($PY -c "from datetime import date; print((date.today().replace(year=date.today().year+1)).strftime('%Y-%m-%d'))" 2>/dev/null || echo "2027-06-19")

# ── Walidacja pól — zawsze 400, niezależnie od gateway ────────────────────────

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"bic\":\"${SWIFT_BIC_PL}\",\"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — brak iban → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"NOTANIBAN\",\"bic\":\"${SWIFT_BIC_PL}\",\"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — nieprawidłowy IBAN → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — brak bic → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"INVALID\",\"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — nieprawidłowy BIC (za krótki) → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"12345678\",\"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — BIC zaczyna się od cyfr → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — brak beneficiaryName → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":0,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — kwota = 0 → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":-1.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — kwota ujemna → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SPLIT\"
}")
check "POST /transfers/swift — nieprawidłowy chargeBearer → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"CRYPTO\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — nieobsługiwana waluta → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"PLN\",\"chargeBearer\":\"SHA\",
  \"valueDate\":\"2020-01-01\"
}")
check "POST /transfers/swift — valueDate w przeszłości → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_BOB}" -d "{
  \"fromAccountId\":\"${BOB_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":999999.00,\"currency\":\"USD\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — brak środków → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":100000.00,\"currency\":\"USD\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — dzienny limit przekroczony (100k>50k) → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"00000000-0000-0000-0000-000000000000\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"USD\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — fromAccountId nie istnieje → 404" 404 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JANE_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"USD\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — cudze fromAccountId → 404" 404 "$(status "$R")"

R=$(req POST /transfers/swift -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",
  \"amount\":50.00,\"currency\":\"USD\",\"chargeBearer\":\"SHA\"
}")
check "POST /transfers/swift — brak tokenu → 401" 401 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/swift — brak body → 400" 400 "$(status "$R")"

# ── Happy path + warianty ─────────────────────────────────────────────────────

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_PL}\",\"bic\":\"${SWIFT_BIC_PL}\",
  \"beneficiaryName\":\"Jan Kowalski\",\"chargeBearer\":\"SHA\",
  \"amount\":6.00,\"currency\":\"USD\",
  \"remittanceInfo\":\"Faktura 123/2026\"
}")
swift_check "POST /transfers/swift — USD+SHA+remittance" "$(status "$R")" "$(body "$R")"
SWIFT_TR_ID=$(jget "$(body "$R")" '.id')
SWIFT_UETR=$(jget "$(body "$R")" '.externalReferenceId')
info "Transfer ID=${SWIFT_TR_ID:-brak}, UETR=${SWIFT_UETR:-brak}"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_DE}\",\"bic\":\"${SWIFT_BIC_DE}\",
  \"beneficiaryName\":\"Hans Mueller\",\"chargeBearer\":\"OUR\",
  \"amount\":5.00,\"currency\":\"USD\",\"valueDate\":\"${NEXT_YEAR}\"
}")
swift_check "POST /transfers/swift — USD+OUR do DE+valueDate" "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_UK}\",\"bic\":\"${SWIFT_BIC_UK}\",
  \"beneficiaryName\":\"John Smith\",\"chargeBearer\":\"BEN\",
  \"amount\":4.00,\"currency\":\"USD\"
}")
swift_check "POST /transfers/swift — USD+BEN do UK" "$(status "$R")"

# Outgoing non-USD must be rejected (auto-conversion only for incoming)
R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_DE}\",\"bic\":\"${SWIFT_BIC_DE}\",
  \"beneficiaryName\":\"Hans Mueller\",\"chargeBearer\":\"SHA\",
  \"amount\":5.00,\"currency\":\"EUR\"
}")
check "POST /transfers/swift — waluta EUR (nie USD) → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"iban\":\"${SWIFT_IBAN_UK}\",\"bic\":\"${SWIFT_BIC_UK}\",
  \"beneficiaryName\":\"John Smith\",\"chargeBearer\":\"SHA\",
  \"amount\":4.00,\"currency\":\"GBP\"
}")
check "POST /transfers/swift — waluta GBP (nie USD) → 400" 400 "$(status "$R")"

# Status po wysłaniu — pobieramy też UETR, bo odpowiedź POST go nie zawiera
if [ -n "$SWIFT_TR_ID" ]; then
  R=$(req GET "/transfers/${SWIFT_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /transfers/{swiftId}/status — po wysłaniu → 200" 200 "$(status "$R")"
  SWIFT_UETR=$(jget "$(body "$R")" '.externalReferenceId')
  info "SWIFT status=$(jget "$(body "$R")" '.status'), UETR=${SWIFT_UETR:-brak}"
else
  skip "GET /transfers/{swiftId}/status" "brak SWIFT_TR_ID"
fi

# Zamknięte konto odbiorcy — middleware odrzuca (422) → gateway error → 400
if $SWIFT_GW_UP; then
  R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
    \"fromAccountId\":\"${JOHN_CHECKING}\",
    \"iban\":\"${SWIFT_IBAN_CLOSED_UK}\",\"bic\":\"${SWIFT_BIC_UK}\",
    \"beneficiaryName\":\"Closed Account\",\"chargeBearer\":\"SHA\",
    \"amount\":10.00,\"currency\":\"USD\"
  }")
  check "POST /transfers/swift — zamknięte konto odbiorcy → 400" 400 "$(status "$R")" "$(body "$R")"
else
  skip "POST /transfers/swift — zamknięte konto" "SWIFT gw off"
fi

# ── POST /transfers/swift/receive ─────────────────────────────────────────────
# Endpoint wymaga X-SWIFT-Webhook-Secret — ustawiony w .env jako Swift__WebhookSecret

FAKE_UETR="11111111-2222-4333-8444-555555555555"
RECEIVE_URL="${BASE_URL}/transfers/swift/receive"
SWIFT_WEBHOOK_SECRET="${Swift__WebhookSecret:-dev_swift_webhook_secret}"
SWIFT_SECRET_CONFIGURED=false
[ -n "${Swift__WebhookSecret:-}" ] && SWIFT_SECRET_CONFIGURED=true

xml_post() {
  # xml_post <extra-headers-as-separate-args> -- <xml-body>
  # Automatycznie dodaje X-SWIFT-Webhook-Secret z env.
  # Usage: xml_post -H "Foo: bar" -- "<xml..."
  local args=() xml=""
  while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do
    args+=("$1")
    shift
  done
  shift 2>/dev/null || true  # skip --
  xml="$*"
  local tmp; tmp=$(mktemp)
  local code
  code=$(curl -s -o "$tmp" -w "%{http_code}" -X POST \
    -H "Content-Type: application/xml" \
    -H "X-SWIFT-Webhook-Secret: ${SWIFT_WEBHOOK_SECRET}" \
    ${args[@]+"${args[@]}"} \
    -d "${xml}" \
    "${RECEIVE_URL}" 2>/dev/null) || code="000"
  local b; b=$(cat "$tmp"); rm -f "$tmp"
  printf '%s|%s' "$code" "$b"
}

PLAIN_XML='<?xml version="1.0" encoding="UTF-8"?><Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"><FIToFICstmrCdtTrf><GrpHdr><MsgId>MSG-TEST</MsgId><CreDtTm>2026-06-19T12:00:00Z</CreDtTm><NbOfTxs>1</NbOfTxs></GrpHdr><CdtTrfTxInf><PmtId><InstrId>INST-TEST</InstrId><UETR>'"${FAKE_UETR}"'</UETR></PmtId><IntrBkSttlmAmt Ccy="USD">100.00</IntrBkSttlmAmt><InstdAmt Ccy="USD">100.00</InstdAmt><ChrgBr>SHAR</ChrgBr><Dbtr><Nm>Test Sender</Nm></Dbtr><DbtrAcct><Id><IBAN>'"${SWIFT_IBAN_PL}"'</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BICFI>'"${SWIFT_BIC_PL}"'</BICFI></FinInstnId></DbtrAgt><Cdtr><Nm>Test Receiver</Nm></Cdtr><CdtrAgt><FinInstnId><BICFI>USBKUS01XXX</BICFI></FinInstnId></CdtrAgt><CdtrAcct><Id><Othr><Id>US123456789012345678901234</Id></Othr></Id></CdtrAcct><RmtInf><Ustrd>Test incoming</Ustrd></RmtInf></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>'

RETURN_XML='<?xml version="1.0" encoding="UTF-8"?><Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"><FIToFICstmrCdtTrf><GrpHdr><MsgId>RETURN-TEST</MsgId><CreDtTm>2026-06-19T12:00:00Z</CreDtTm></GrpHdr><CdtTrfTxInf><PmtId><InstrId>RET-INST</InstrId><UETR>'"${FAKE_UETR}"'</UETR></PmtId><IntrBkSttlmAmt Ccy="USD">100.00</IntrBkSttlmAmt><RmtInf><Ustrd>Zwrot</Ustrd></RmtInf><DbtrAgt><FinInstnId><BICFI>'"${SWIFT_BIC_PL}"'</BICFI></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BICFI>USBKUS01XXX</BICFI></FinInstnId></CdtrAgt></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>'

NO_UETR_XML='<?xml version="1.0" encoding="UTF-8"?><Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"><FIToFICstmrCdtTrf><CdtTrfTxInf><PmtId><InstrId>NO-UETR</InstrId></PmtId></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>'

# Brak sekretu → 401 (tylko gdy Swift__WebhookSecret skonfigurowany)
if $SWIFT_SECRET_CONFIGURED; then
  R=$(curl -s -o /dev/null -w "%{http_code}|" -X POST \
    -H "Content-Type: application/xml" \
    -H "X-SWIFT-UETR: ${FAKE_UETR}" \
    -d "${PLAIN_XML}" \
    "${RECEIVE_URL}" 2>/dev/null)
  check "POST /transfers/swift/receive — brak sekretu → 401" 401 "$(status "$R|")"
else
  skip "POST /transfers/swift/receive — brak sekretu → 401" "Swift__WebhookSecret nie skonfigurowany (middleware nie wysyła sekretu)"
fi

# Normalny przychodzący — nieznany UETR → graceful, 200
R=$(xml_post -H "X-SWIFT-UETR: ${FAKE_UETR}" -- "${PLAIN_XML}")
check "POST /transfers/swift/receive — nieznany UETR (graceful) → 200" 200 "$(status "$R")" "$(body "$R")"
info "Receive body: $(body "$R" | head -c 120)"

# RETURN przez nagłówek X-SWIFT-Message-Type: RETURN
R=$(xml_post -H "X-SWIFT-Message-Type: RETURN" -H "X-SWIFT-UETR: ${FAKE_UETR}" -- "${PLAIN_XML}")
check "POST /transfers/swift/receive — RETURN (nagłówek) → 200" 200 "$(status "$R")"

# Brak UETR (w XML i w nagłówku) → 400
R=$(xml_post -- "${NO_UETR_XML}")
check "POST /transfers/swift/receive — brak UETR w XML → 400" 400 "$(status "$R")"

# E2E complete — jeśli mamy prawdziwy transfer z UETR z SWIFT Middleware
if $SWIFT_GW_UP && [ -n "$SWIFT_TR_ID" ] && [ -n "$SWIFT_UETR" ] && [ "$SWIFT_UETR" != "null" ] && [ "$SWIFT_UETR" != "" ]; then
  info "E2E complete: symulacja forwardu od middleware z UETR=${SWIFT_UETR}"
  COMPLETE_XML='<?xml version="1.0" encoding="UTF-8"?><Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"><FIToFICstmrCdtTrf><GrpHdr><MsgId>MSG-DONE</MsgId><CreDtTm>2026-06-19T13:00:00Z</CreDtTm><NbOfTxs>1</NbOfTxs></GrpHdr><CdtTrfTxInf><PmtId><InstrId>INST-DONE</InstrId><UETR>'"${SWIFT_UETR}"'</UETR></PmtId><IntrBkSttlmAmt Ccy="PLN">50.00</IntrBkSttlmAmt><InstdAmt Ccy="PLN">50.00</InstdAmt><ChrgBr>SHAR</ChrgBr><Dbtr><Nm>John Doe</Nm></Dbtr><DbtrAcct><Id><IBAN>US0000000000000000001</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BICFI>USBKUS01XXX</BICFI></FinInstnId></DbtrAgt><Cdtr><Nm>Jan Kowalski</Nm></Cdtr><CdtrAgt><FinInstnId><BICFI>'"${SWIFT_BIC_PL}"'</BICFI></FinInstnId></CdtrAgt><CdtrAcct><Id><Othr><Id>'"${SWIFT_IBAN_PL}"'</Id></Othr></Id></CdtrAcct><RmtInf><Ustrd>Faktura 123/2026</Ustrd></RmtInf></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>'

  R=$(xml_post -H "X-SWIFT-UETR: ${SWIFT_UETR}" -- "${COMPLETE_XML}")
  check "POST /transfers/swift/receive — E2E complete z prawdziwym UETR → 200" 200 "$(status "$R")" "$(body "$R")"

  R=$(req GET "/transfers/${SWIFT_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /transfers/{swiftId}/status — po /receive → 200" 200 "$(status "$R")"
  FINAL_STATUS=$(jget "$(body "$R")" '.status')
  if [ "${FINAL_STATUS}" = "completed" ]; then
    ok "SWIFT transfer status = completed po /receive (E2E sukces)"
  else
    fail "SWIFT transfer status = '${FINAL_STATUS}' (oczekiwano 'completed')" "$(body "$R")"
  fi
else
  skip "POST /transfers/swift/receive — E2E complete" "SWIFT gw off lub brak UETR"
  skip "GET status po /receive — E2E complete" "SWIFT gw off lub brak UETR"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "16 · TRANSFERS — approve / reject"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$TOKEN_EMMA" ]; then
  R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_EMMA}" -d "{
    \"fromAccountId\":\"${JUNIOR_ACC_1}\",
    \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
    \"amount\":1.00,\"currency\":\"USD\",\"description\":\"Junior approve test\"
  }")
  APPROVE_TR_ID=$(jget "$(body "$R")" '.id')
  if [ "$(status "$R")" = "201" ] && [ -n "$APPROVE_TR_ID" ]; then
    info "Pending transfer #1 (approve): ${APPROVE_TR_ID}"
    R=$(req POST "/transfers/${APPROVE_TR_ID}/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "POST /transfers/{id}/approve — john zatwierdza → 200" 200 "$(status "$R")"
    R=$(req POST "/transfers/${APPROVE_TR_ID}/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "POST /transfers/{id}/approve — ponowne zatwierdzenie → 409" 409 "$(status "$R")"
  else
    skip "approve — happy path" "nie udało się utworzyć pending transfer ($(status "$R"))"
    skip "approve — ponowny" "brak pending transfer"
  fi

  R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_EMMA}" -d "{
    \"fromAccountId\":\"${JUNIOR_ACC_1}\",
    \"toAccountNumber\":\"${JOHN_SAVINGS_NUM}\",
    \"amount\":1.00,\"currency\":\"USD\",\"description\":\"Junior reject test\"
  }")
  REJECT_TR_ID=$(jget "$(body "$R")" '.id')
  if [ "$(status "$R")" = "201" ] && [ -n "$REJECT_TR_ID" ]; then
    info "Pending transfer #2 (reject): ${REJECT_TR_ID}"
    R=$(req POST "/transfers/${REJECT_TR_ID}/approve" -H "Authorization: Bearer ${TOKEN_JANE}")
    check "POST /transfers/{id}/approve — zły parent → 401" 401 "$(status "$R")"
    R=$(req POST "/transfers/${REJECT_TR_ID}/reject" -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "POST /transfers/{id}/reject — john odrzuca → 200" 200 "$(status "$R")"
    R=$(req POST "/transfers/${REJECT_TR_ID}/reject" -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "POST /transfers/{id}/reject — już odrzucony → 409" 409 "$(status "$R")"
  else
    skip "reject — happy path" "nie udało się utworzyć pending transfer ($(status "$R"))"
    skip "reject — zły parent" "brak pending transfer"
    skip "reject — ponowny" "brak pending transfer"
  fi
else
  skip "approve/reject — wszystkie" "brak TOKEN_EMMA"
fi

R=$(req POST "/transfers/${TR_COMPLETED}/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/{id}/approve — stan != pending_approval → 409" 409 "$(status "$R")"

R=$(req POST "/transfers/00000000-0000-0000-0000-000000000000/reject" -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/{id}/reject — nie istnieje → 404" 404 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "17 · TRANSFERS — webhook"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$ACH_TR_ID" ]; then
  WH_STATUS_R=$(req GET "/transfers/${ACH_TR_ID}/status" -H "Authorization: Bearer ${TOKEN_JOHN}")
  WH_STATUS=$(jget "$(body "$WH_STATUS_R")" '.status')
  if [ "$WH_STATUS" = "pending" ] || [ "$WH_STATUS" = "processing" ]; then
    R=$(req POST "/transfers/${ACH_TR_ID}/webhook" \
      -H "X-Webhook-Secret: ${WEBHOOK_SECRET}" \
      -d "{\"status\":\"completed\",\"referenceId\":\"ACH-DONE-${TS}\"}")
    check "POST /transfers/{id}/webhook — completed → 200" 200 "$(status "$R")" "$(body "$R")"
    R=$(req POST "/transfers/${ACH_TR_ID}/webhook" \
      -H "X-Webhook-Secret: ${WEBHOOK_SECRET}" \
      -d "{\"status\":\"completed\",\"referenceId\":\"ACH-DONE-${TS}\"}")
    check "POST /transfers/{id}/webhook — już sfinalizowany → 400" 400 "$(status "$R")"
  else
    skip "webhook — completed" "ACH transfer w stanie '${WH_STATUS}' (nie pending)"
    skip "webhook — już sfinalizowany" "ACH transfer już sfinalizowany"
  fi
else
  skip "webhook — completed" "brak ACH_TR_ID"
  skip "webhook — już sfinalizowany" "brak ACH_TR_ID"
fi

R=$(req POST "/transfers/${TR_SWIFT_PENDING}/webhook" \
  -H "X-Webhook-Secret: wrong_secret" -d '{"status":"completed"}')
check "POST /transfers/{id}/webhook — zły secret → 401" 401 "$(status "$R")"

R=$(req POST "/transfers/${TR_SWIFT_PENDING}/webhook" -d '{"status":"completed"}')
check "POST /transfers/{id}/webhook — brak X-Webhook-Secret → 401" 401 "$(status "$R")"

R=$(req POST "/transfers/${TR_SWIFT_PENDING}/webhook" \
  -H "X-Webhook-Secret: ${WEBHOOK_SECRET}" -d '{"status":"invalid_status"}')
check "POST /transfers/{id}/webhook — nieprawidłowy status → 400" 400 "$(status "$R")"

R=$(req POST "/transfers/${TR_SWIFT_PENDING}/webhook" \
  -H "X-Webhook-Secret: ${WEBHOOK_SECRET}" -d '{"status":"failed"}')
check_any "POST /transfers/{id}/webhook — failed → 200 lub 400" "200|400" "$(status "$R")"

R=$(req POST "/transfers/00000000-0000-0000-0000-000000000000/webhook" \
  -H "X-Webhook-Secret: ${WEBHOOK_SECRET}" -d '{"status":"completed"}')
check "POST /transfers/{id}/webhook — nie istnieje → 404" 404 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "18 · CARDS — rejestracja"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$TEST_ACCOUNT_ID" ]; then
  R=$(req POST "/accounts/${TEST_ACCOUNT_ID}/cards" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"debit"}')
  if $CARDS_GW_UP; then
    check "POST /accounts/{id}/cards — debit → 201" 201 "$(status "$R")" "$(body "$R")"
  else
    check_any "POST /accounts/{id}/cards — debit (gateway off) → 201|503" "201|503" "$(status "$R")" "$(body "$R")"
  fi
  TEST_CARD_DEBIT_ID=$(jget "$(body "$R")" '.id')
  info "Debit card ID: ${TEST_CARD_DEBIT_ID:-brak (gateway niedostępny?)}"

  R=$(req POST "/accounts/${TEST_ACCOUNT_ID}/cards" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"debit"}')
  if [ -n "$TEST_CARD_DEBIT_ID" ]; then
    check "POST /accounts/{id}/cards — debit duplikat → 409" 409 "$(status "$R")"
  else
    check_any "POST /accounts/{id}/cards — debit duplikat → 409|503" "409|503" "$(status "$R")"
  fi

  R=$(req POST "/accounts/${TEST_ACCOUNT_ID}/cards" \
    -H "Authorization: Bearer ${TOKEN_TEST}" \
    -d '{"type":"prepaid","dailyLimit":200.00,"monthlyLimit":1000.00}')
  if $CARDS_GW_UP; then
    check "POST /accounts/{id}/cards — prepaid z limitami → 201" 201 "$(status "$R")" "$(body "$R")"
  else
    check_any "POST /accounts/{id}/cards — prepaid (gateway off) → 201|503" "201|503" "$(status "$R")" "$(body "$R")"
  fi
  TEST_CARD_PREPAID_ID=$(jget "$(body "$R")" '.id')
  info "Prepaid card ID: ${TEST_CARD_PREPAID_ID:-brak}"
else
  skip "card register — debit/prepaid" "brak TEST_ACCOUNT_ID"
fi

R=$(req POST "/accounts/${TEST_ACCOUNT_ID:-00000000-0000-0000-0000-000000000000}/cards" \
  -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"credit"}')
check "POST /accounts/{id}/cards — typ 'credit' → 400" 400 "$(status "$R")"

R=$(req POST "/accounts/${TEST_ACCOUNT_ID:-00000000-0000-0000-0000-000000000000}/cards" \
  -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"prepaid","dailyLimit":5.00}')
check "POST /accounts/{id}/cards — dailyLimit < 10 → 400" 400 "$(status "$R")"

R=$(req POST "/accounts/${TEST_ACCOUNT_ID:-00000000-0000-0000-0000-000000000000}/cards" \
  -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"type":"prepaid","dailyLimit":99999.00}')
check "POST /accounts/{id}/cards — dailyLimit > 10000 → 400" 400 "$(status "$R")"

R=$(req POST "/accounts/${JANE_CHECKING}/cards" \
  -H "Authorization: Bearer ${TOKEN_JOHN}" -d '{"type":"prepaid"}')
check "POST /accounts/{id}/cards — cudze konto → 401" 401 "$(status "$R")"

R=$(req POST "/accounts/${TEST_ACCOUNT_ID:-00000000-0000-0000-0000-000000000000}/cards" \
  -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"dailyLimit":100.00}')
check "POST /accounts/{id}/cards — brak type → 400" 400 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "19 · CARDS — get"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$TEST_ACCOUNT_ID" ]; then
  R=$(req GET "/accounts/${TEST_ACCOUNT_ID}/cards" -H "Authorization: Bearer ${TOKEN_TEST}")
  check "GET /accounts/{id}/cards — własne → 200" 200 "$(status "$R")"
  info "Karty test usera: $(jget "$(body "$R")" 'length')"

  R=$(req GET "/accounts/${JANE_CHECKING}/cards" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /accounts/{id}/cards — cudze → 401" 401 "$(status "$R")"

  if [ -n "$TEST_CARD_DEBIT_ID" ]; then
    R=$(req GET "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}" \
      -H "Authorization: Bearer ${TOKEN_TEST}")
    check "GET /accounts/{id}/cards/{cardId} — własna debit → 200" 200 "$(status "$R")"

    R=$(req GET "/accounts/${TEST_ACCOUNT_ID}/cards/00000000-0000-0000-0000-000000000000" \
      -H "Authorization: Bearer ${TOKEN_TEST}")
    check "GET /accounts/{id}/cards/{cardId} — nie istnieje → 404" 404 "$(status "$R")"

    R=$(req GET "/accounts/${JOHN_SAVINGS}/cards/${TEST_CARD_DEBIT_ID}" \
      -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "GET /accounts/{id}/cards/{cardId} — karta z innego konta → 404" 404 "$(status "$R")"

    R=$(req GET "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}" \
      -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "GET /accounts/{id}/cards/{cardId} — cudzy właściciel → 401" 401 "$(status "$R")"
  else
    skip "GET card — szczegóły/not-found/inne-konto/cudzy" "gateway niedostępny (brak TEST_CARD_DEBIT_ID)"
  fi
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "20 · CARDS — update status"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$TEST_CARD_DEBIT_ID" ] && [ -n "$TEST_ACCOUNT_ID" ]; then
  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"blocked"}')
  check "PATCH /cards/{id}/status — block → 200" 200 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"blocked"}')
  check "PATCH /cards/{id}/status — block ponownie → 409" 409 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"active"}')
  check "PATCH /cards/{id}/status — active w ciągu 24h → 409 (cooldown)" 409 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"active"}')
  check "PATCH /cards/{id}/status — block ponownie (nadal zablokowana) → 409" 409 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"expired"}')
  check "PATCH /cards/{id}/status — expired (niedozwolone) → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"status":"frozen"}')
  check "PATCH /cards/{id}/status — nieznany status → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_JOHN}" -d '{"status":"blocked"}')
  check "PATCH /cards/{id}/status — cudzy właściciel → 401" 401 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/status" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{}')
  check "PATCH /cards/{id}/status — brak status → 400" 400 "$(status "$R")"
else
  skip "update status — wszystkie" "brak TEST_CARD_DEBIT_ID (gateway niedostępny)"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "21 · CARDS — update limits"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$TEST_CARD_DEBIT_ID" ] && [ -n "$TEST_ACCOUNT_ID" ]; then
  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"dailyLimit":200.00,"monthlyLimit":3000.00}')
  check "PATCH /cards/{id}/limits — ustaw oba → 200" 200 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"dailyLimit":150.00}')
  check "PATCH /cards/{id}/limits — tylko dailyLimit → 200" 200 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"monthlyLimit":5000.00}')
  check "PATCH /cards/{id}/limits — tylko monthlyLimit → 200" 200 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"dailyLimit":1.00}')
  check "PATCH /cards/{id}/limits — dailyLimit < 10 → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"dailyLimit":50000.00}')
  check "PATCH /cards/{id}/limits — dailyLimit > 10000 → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"monthlyLimit":10.00}')
  check "PATCH /cards/{id}/limits — monthlyLimit < 50 → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_TEST}" -d '{"monthlyLimit":200000.00}')
  check "PATCH /cards/{id}/limits — monthlyLimit > 100000 → 400" 400 "$(status "$R")"

  R=$(req PATCH "/accounts/${TEST_ACCOUNT_ID}/cards/${TEST_CARD_DEBIT_ID}/limits" \
    -H "Authorization: Bearer ${TOKEN_JOHN}" -d '{"dailyLimit":100.00}')
  check "PATCH /cards/{id}/limits — cudzy właściciel → 401" 401 "$(status "$R")"
else
  skip "update limits — wszystkie" "brak TEST_CARD_DEBIT_ID (gateway niedostępny)"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "22 · JUNIOR CARD"
# ═══════════════════════════════════════════════════════════════════════════════

if [ -n "$NEW_JUNIOR_ID" ]; then
  R=$(req POST "/accounts/junior/${NEW_JUNIOR_ID}/card" \
    -H "Authorization: Bearer ${TOKEN_JOHN}" \
    -d '{"dailyLimit":50.00,"monthlyLimit":300.00}')
  check "POST /accounts/junior/{id}/card — pierwsza karta → 201" 201 "$(status "$R")"

  R=$(req POST "/accounts/junior/${NEW_JUNIOR_ID}/card" \
    -H "Authorization: Bearer ${TOKEN_JOHN}" \
    -d '{"dailyLimit":50.00,"monthlyLimit":300.00}')
  check "POST /accounts/junior/{id}/card — duplikat → 409" 409 "$(status "$R")"
else
  skip "junior card — pierwsza/duplikat" "brak NEW_JUNIOR_ID"
fi

R=$(req POST "/accounts/junior/${JUNIOR_ACC_1}/card" \
  -H "Authorization: Bearer ${TOKEN_JANE}" -d '{"dailyLimit":50.00}')
check "POST /accounts/junior/{id}/card — cudzy junior → 401" 401 "$(status "$R")"

R=$(req POST "/accounts/junior/${NEW_JUNIOR_ID:-dddd4444-1111-1111-1111-111111111111}/card" \
  -H "Authorization: Bearer ${TOKEN_JOHN}" -d '{"dailyLimit":0.50}')
check_any "POST /accounts/junior/{id}/card — dailyLimit < 10 → 400 lub 409" "400|409" "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "23 · EDGE CASES"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}" -d "{
  \"fromAccountId\":\"${JOHN_CHECKING}\",
  \"toAccountId\":\"${JANE_CHECKING}\",
  \"toAccountNumber\":\"${JANE_CHECKING_NUM}\",
  \"amount\":1.00,\"currency\":\"USD\"
}")
check "POST /transfers/internal — toAccountId + toAccountNumber razem → 201" 201 "$(status "$R")"

R=$(req GET "/accounts/not-a-guid/balance" -H "Authorization: Bearer ${TOKEN_JOHN}")
S=$(status "$R")
if [ "$S" = "400" ] || [ "$S" = "404" ]; then
  ok "GET /accounts/{not-a-guid}/balance — invalid GUID → ${S} (4xx)"
else
  fail "GET /accounts/{not-a-guid}/balance — oczekiwano 4xx, dostano ${S}"
fi

R=$(req POST /transfers/internal -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/internal — brak body → 400" 400 "$(status "$R")"

R=$(req POST /transfers/swift -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/swift — brak body → 400" 400 "$(status "$R")"

R=$(req POST /transfers/fednow -H "Authorization: Bearer ${TOKEN_JOHN}")
check "POST /transfers/fednow — brak body → 400" 400 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "24 · BLIK — generowanie kodu i lista"
# ═══════════════════════════════════════════════════════════════════════════════

R=$(req POST /blik/generate -H "Authorization: Bearer ${TOKEN_JOHN}" \
  -d "{\"accountId\":\"${JOHN_CHECKING}\"}")
check "POST /blik/generate — happy path → 201" 201 "$(status "$R")" "$(body "$R")"
info "BLIK code: $(jget "$(body "$R")" '.code')"

R=$(req POST /blik/generate -H "Authorization: Bearer ${TOKEN_JOHN}" \
  -d "{\"accountId\":\"${JANE_CHECKING}\"}")
check "POST /blik/generate — cudze konto → 401" 401 "$(status "$R")"

R=$(req POST /blik/generate -H "Authorization: Bearer ${TOKEN_JOHN}" \
  -d "{\"accountId\":\"00000000-0000-0000-0000-000000000000\"}")
check "POST /blik/generate — konto nie istnieje → 404" 404 "$(status "$R")"

R=$(req POST /blik/generate -d "{\"accountId\":\"${JOHN_CHECKING}\"}")
check "POST /blik/generate — brak tokenu → 401" 401 "$(status "$R")"

R=$(req GET /blik/pending -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /blik/pending — john → 200" 200 "$(status "$R")"

R=$(req GET /blik/pending)
check "GET /blik/pending — brak tokenu → 401" 401 "$(status "$R")"

R=$(req GET /blik/transactions -H "Authorization: Bearer ${TOKEN_JOHN}")
check "GET /blik/transactions — john → 200" 200 "$(status "$R")"

R=$(req GET /blik/transactions)
check "GET /blik/transactions — brak tokenu → 401" 401 "$(status "$R")"

# ═══════════════════════════════════════════════════════════════════════════════
section "25 · KLIK WEBHOOK"
# ═══════════════════════════════════════════════════════════════════════════════

EXPIRY_TIME=$($PY -c "from datetime import datetime,timedelta,timezone; print((datetime.now(timezone.utc)+timedelta(minutes=5)).strftime('%Y-%m-%dT%H:%M:%SZ'))" 2>/dev/null || echo "2099-12-31T00:00:00Z")
NOW_TS=$($PY -c "from datetime import datetime,timezone; print(datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'))" 2>/dev/null || echo "2026-06-18T12:00:00Z")
KLIK_TX_ID="tx-${TS}-1"

R=$(req POST /klik/webhook/authorize \
  -H "X-Webhook-Secret: ${KLIK_SECRET}" \
  -d "{\"transaction_id\":\"${KLIK_TX_ID}\",\"user_id\":\"${JOHN_USER_ID}\",\"amount\":15.00,\"currency\":\"USD\",\"merchant_name\":\"Test Shop\",\"is_on_us\":false,\"expiry_time\":\"${EXPIRY_TIME}\",\"zone\":\"US\"}")
check "POST /klik/webhook/authorize — happy path → 200" 200 "$(status "$R")"

R=$(req POST /klik/webhook/authorize \
  -H "X-Webhook-Secret: ${KLIK_SECRET}" \
  -d "{\"transaction_id\":\"${KLIK_TX_ID}\",\"user_id\":\"${JOHN_USER_ID}\",\"amount\":15.00,\"currency\":\"USD\",\"merchant_name\":\"Test Shop\",\"is_on_us\":false,\"expiry_time\":\"${EXPIRY_TIME}\",\"zone\":\"US\"}")
check "POST /klik/webhook/authorize — duplikat (idempotent) → 200" 200 "$(status "$R")"

R=$(req POST /klik/webhook/authorize \
  -H "X-Webhook-Secret: wrong_secret" \
  -d "{\"transaction_id\":\"tx-bad-${TS}\",\"user_id\":\"${JOHN_USER_ID}\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_name\":\"Shop\",\"is_on_us\":false,\"expiry_time\":\"${EXPIRY_TIME}\",\"zone\":\"US\"}")
check "POST /klik/webhook/authorize — zły secret → 401" 401 "$(status "$R")"

R=$(req POST /klik/webhook/authorize \
  -d "{\"transaction_id\":\"tx-nosec-${TS}\",\"user_id\":\"${JOHN_USER_ID}\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_name\":\"Shop\",\"is_on_us\":false,\"expiry_time\":\"${EXPIRY_TIME}\",\"zone\":\"US\"}")
check "POST /klik/webhook/authorize — brak X-Webhook-Secret → 401" 401 "$(status "$R")"

R=$(req POST /klik/webhook/authorize \
  -H "X-Webhook-Secret: ${KLIK_SECRET}" \
  -d "{\"transaction_id\":\"tx-nouser-${TS}\",\"user_id\":\"00000000-0000-0000-0000-000000000000\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_name\":\"Shop\",\"is_on_us\":false,\"expiry_time\":\"${EXPIRY_TIME}\",\"zone\":\"US\"}")
check "POST /klik/webhook/authorize — nieznany user_id → 404" 404 "$(status "$R")"

R=$(req POST /klik/webhook/ping \
  -H "X-Webhook-Secret: ${KLIK_SECRET}" \
  -d "{\"timestamp\":\"${NOW_TS}\",\"nonce\":\"nonce-${TS}\"}")
check "POST /klik/webhook/ping — happy path → 200" 200 "$(status "$R")"

R=$(req POST /klik/webhook/ping \
  -H "X-Webhook-Secret: wrong_secret" \
  -d "{\"timestamp\":\"${NOW_TS}\",\"nonce\":\"abc\"}")
check "POST /klik/webhook/ping — zły secret → 401" 401 "$(status "$R")"

# BLIK — autoryzacja bezpieczeństwa: cudzy użytkownik nie może zatwierdzić
# Webhook authorize dla john stworzył pending BlikAuthorization.
# DecideAsync filtruje po userId — więc jane dostaje 404 (nie ujawniamy że istnieje).
if [ -n "$JOHN_USER_ID" ]; then
  BLIK_PENDING_R=$(req GET /blik/pending -H "Authorization: Bearer ${TOKEN_JOHN}")
  if [ -n "$PY" ]; then
    JOHN_BLIK_AUTH=$($PY -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')" 2>/dev/null <<< "$(body "$BLIK_PENDING_R")")
  else
    JOHN_BLIK_AUTH=$(printf '%s' "$(body "$BLIK_PENDING_R")" | grep -oE '"id":"[^"]*"' | head -1 | sed 's/"id":"//;s/"//')
  fi
  if [ -n "$JOHN_BLIK_AUTH" ]; then
    R=$(req POST "/blik/${JOHN_BLIK_AUTH}/approve" -H "Authorization: Bearer ${TOKEN_JANE}")
    check "POST /blik/{john's auth}/approve — jane → 404 (nie jej autoryzacja)" 404 "$(status "$R")"
    R=$(req POST "/blik/${JOHN_BLIK_AUTH}/reject" -H "Authorization: Bearer ${TOKEN_JANE}")
    check "POST /blik/{john's auth}/reject — jane → 404 (nie jej autoryzacja)" 404 "$(status "$R")"
  else
    skip "BLIK cross-user approve" "brak oczekujących autoryzacji u john"
    skip "BLIK cross-user reject" "brak oczekujących autoryzacji u john"
  fi
else
  skip "BLIK cross-user approve" "brak JOHN_USER_ID"
  skip "BLIK cross-user reject" "brak JOHN_USER_ID"
fi

# ═══════════════════════════════════════════════════════════════════════════════
section "26 · BLIK — pełny flow (generate → simulate/initiate → pending → approve/reject)"
# ═══════════════════════════════════════════════════════════════════════════════

# Realny KLIK dostępny na porcie 8900 (KLIK-payments docker-compose.override.yml)
KLIK_URL="${KLIK_HOST_URL:-http://localhost:8900}"
KLIK_AGENT_KEY="${KLIK_AGENT_API_KEY:-}"
KLIK_MID="${KLIK_MERCHANT_ID:-}"

klik_up() {
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 3 "${KLIK_URL}/healthz/" 2>/dev/null) || code="000"
  [ "$code" = "200" ]
}

simulate_initiate() {
  local code="$1" amount="$2"
  local idem="idem-${TS}-$(date +%s%N 2>/dev/null || date +%s)"
  curl -s -X POST -H "Content-Type: application/json" \
    -H "X-KLIK-Agent-Api-Key: ${KLIK_AGENT_KEY}" \
    -H "Idempotency-Key: ${idem}" \
    -d "{\"code\":\"${code}\",\"amount\":\"${amount}\",\"currency\":\"USD\",\"merchant_id\":\"${KLIK_MID}\"}" \
    "${KLIK_URL}/api/v1/payments/initiate" 2>/dev/null
}

KLIK_READY=false
if [ -n "$KLIK_AGENT_KEY" ] && [ -n "$KLIK_MID" ] && klik_up; then
  KLIK_READY=true
fi
info "KLIK ready=${KLIK_READY} url=${KLIK_URL} agent_key=${KLIK_AGENT_KEY:0:12}... merchant=${KLIK_MID}"

if [ -n "$JOHN_USER_ID" ] && [ "$KLIK_READY" = "true" ]; then

  # --- Approve flow ---
  R=$(req POST /blik/generate -H "Authorization: Bearer ${TOKEN_JOHN}" \
    -d "{\"accountId\":\"${JOHN_CHECKING}\"}")
  FLOW_CODE=$(jget "$(body "$R")" '.code')
  info "Generated BLIK code: ${FLOW_CODE}"

  if [ -n "$FLOW_CODE" ]; then
    INIT_RESP=$(simulate_initiate "$FLOW_CODE" "5.00")
    info "KLIK initiate: $(echo "$INIT_RESP" | head -c 120)"
    sleep 3  # poczekaj na async webhook Celery → us-bank-system

    R=$(req GET /blik/pending -H "Authorization: Bearer ${TOKEN_JOHN}")
    check "Pełny flow — GET /blik/pending → 200" 200 "$(status "$R")"
    info "Oczekujące autoryzacje: $(jget "$(body "$R")" 'length')"

    if [ -n "$PY" ]; then
      BLIK_AUTH_ID=$($PY -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')" 2>/dev/null <<< "$(body "$R")")
    else
      BLIK_AUTH_ID=$(printf '%s' "$(body "$R")" | grep -oE '"id":"[^"]*"' | head -1 | sed 's/"id":"//;s/"//')
    fi

    if [ -n "$BLIK_AUTH_ID" ]; then
      R=$(req POST "/blik/${BLIK_AUTH_ID}/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
      check "Pełny flow — approve → 200" 200 "$(status "$R")" "$(body "$R")"
      info "Status po approve: $(jget "$(body "$R")" '.status')"

      R=$(req POST "/blik/${BLIK_AUTH_ID}/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
      check "Pełny flow — ponowny approve → 409" 409 "$(status "$R")"
    else
      skip "approve" "brak oczekującej autoryzacji (webhook nie dotarł?)"
      skip "ponowny approve" "brak oczekującej autoryzacji"
    fi
  else
    skip "approve flow" "nie udało się wygenerować kodu BLIK"
    skip "ponowny approve" "nie udało się wygenerować kodu BLIK"
  fi

  # --- Reject flow ---
  R=$(req POST /blik/generate -H "Authorization: Bearer ${TOKEN_JOHN}" \
    -d "{\"accountId\":\"${JOHN_CHECKING}\"}")
  REJECT_CODE=$(jget "$(body "$R")" '.code')

  if [ -n "$REJECT_CODE" ]; then
    simulate_initiate "$REJECT_CODE" "3.00" > /dev/null
    sleep 3  # poczekaj na async webhook

    R=$(req GET /blik/pending -H "Authorization: Bearer ${TOKEN_JOHN}")
    if [ -n "$PY" ]; then
      REJECT_AUTH_ID=$($PY -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')" 2>/dev/null <<< "$(body "$R")")
    else
      REJECT_AUTH_ID=$(printf '%s' "$(body "$R")" | grep -oE '"id":"[^"]*"' | head -1 | sed 's/"id":"//;s/"//')
    fi

    if [ -n "$REJECT_AUTH_ID" ]; then
      R=$(req POST "/blik/${REJECT_AUTH_ID}/reject" -H "Authorization: Bearer ${TOKEN_JOHN}")
      check "Pełny flow — reject → 200" 200 "$(status "$R")"
      info "Status po reject: $(jget "$(body "$R")" '.status')"
    else
      skip "reject" "brak oczekującej autoryzacji"
    fi
  else
    skip "reject flow" "nie udało się wygenerować kodu BLIK"
  fi

  # --- Not found ---
  R=$(req POST "/blik/00000000-0000-0000-0000-000000000000/approve" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "POST /blik/{id}/approve — nie istnieje → 404" 404 "$(status "$R")"

  R=$(req POST "/blik/00000000-0000-0000-0000-000000000000/reject" -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "POST /blik/{id}/reject — nie istnieje → 404" 404 "$(status "$R")"

  # --- Historia po flow ---
  R=$(req GET /blik/transactions -H "Authorization: Bearer ${TOKEN_JOHN}")
  check "GET /blik/transactions — po flow → 200" 200 "$(status "$R")"
  info "BLIK historia: $(jget "$(body "$R")" 'length') rekordów"

elif [ -z "$JOHN_USER_ID" ]; then
  skip "pełny BLIK flow — wszystkie" "brak JOHN_USER_ID"
elif [ -z "$KLIK_AGENT_KEY" ] || [ -z "$KLIK_MID" ]; then
  skip "pełny BLIK flow — wszystkie" "brak KLIK_AGENT_API_KEY lub KLIK_MERCHANT_ID w .env"
else
  skip "pełny BLIK flow — wszystkie" "KLIK (${KLIK_URL}) niedostępny"
fi

# ═══════════════════════════════════════════════════════════════════════════════
# PODSUMOWANIE
# ═══════════════════════════════════════════════════════════════════════════════

TOTAL=$((PASS + FAIL + SKIP))
echo ""
echo -e "${BOLD}════════════════════════════════════════${NC}"
echo -e "${BOLD}  Wyniki testów${NC}"
echo -e "${BOLD}════════════════════════════════════════${NC}"
echo -e "  ${GREEN}✓ Passed:${NC}  ${PASS}"
echo -e "  ${RED}✗ Failed:${NC}  ${FAIL}"
echo -e "  ${YELLOW}⊘ Skipped:${NC} ${SKIP}"
echo -e "  Łącznie:   ${TOTAL}"
echo ""

$ACH_HELPER_UP || echo -e "${YELLOW}  ACH Helper niedostępny (${ACH_URL}) — uruchom FedSystems${NC}"
$SFTP_UP       || echo -e "${YELLOW}  SFTP niedostępny (${SFTP_HOST}:${SFTP_PORT}) — uruchom FedSystems${NC}"
$CARDS_GW_UP   || echo -e "${YELLOW}  Cards Gateway niedostępny (${CARDS_URL}) — uruchom Karty-Platnicze${NC}"
echo ""

if [ "$FAIL" -eq 0 ]; then
  echo -e "${GREEN}${BOLD}Wszystkie testy przeszły!${NC}"
  exit 0
else
  echo -e "${RED}${BOLD}${FAIL} testów nie przeszło.${NC}"
  exit 1
fi
