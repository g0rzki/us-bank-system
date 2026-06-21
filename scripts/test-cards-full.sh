#!/usr/bin/env bash
# Pełny test kart płatniczych na koncie john.doe@example.com
# Tworzy nowe konto testowe, wykonuje wszystkie happy path + edge case
# Użycie: bash test-cards-full.sh

BASE="http://localhost:5100"
POS="http://localhost:8072"
ADMIN_KEY="admin-secret-key-2026"

EMAIL="john.doe@example.com"
PASS="Test123!"

ok=0; fail=0

# ── helpers ────────────────────────────────────────────────────────────────────
j()     { curl -s -H "Content-Type: application/json" "$@"; }
a()     { j -H "Authorization: Bearer $JWT" "$@"; }
field() { echo "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }
numfield() { echo "$1" | grep -o "\"$2\":[0-9.]*" | head -1 | cut -d':' -f2; }

check() {
    local desc="$1" got="$2" want="$3"
    if echo "$got" | grep -qi "$want"; then
        printf "  \e[32mPASS\e[0m %s\n" "$desc"; ((ok++))
    else
        printf "  \e[31mFAIL\e[0m %s\n        got:  %s\n        want: %s\n" "$desc" "$got" "$want"; ((fail++))
    fi
}

check_status() {
    local desc="$1" url="$2" want="$3"
    local code
    code=$(curl -s -o /dev/null -w "%{http_code}" "$url")
    check "$desc" "$code" "$want"
}

# ── login ──────────────────────────────────────────────────────────────────────
echo -e "\e[36m=== LOGIN ===\e[0m"
LOGIN=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
JWT=$(field "$LOGIN" "token")
if [ -z "$JWT" ]; then echo "BŁĄD: nie można zalogować jako $EMAIL"; exit 1; fi
echo "  Zalogowano jako $EMAIL"

# ── znajdź konto bez aktywnych kart (lub utwórz nowe) ─────────────────────────
echo -e "\n\e[36m=== SETUP: szukam konta testowego ===\e[0m"
ACCOUNTS=$(a -X GET "$BASE/accounts")
# Szukaj konta bez aktywnych kart
AID=""
for id in $(echo "$ACCOUNTS" | grep -o '"id":"[^"]*"' | cut -d'"' -f4); do
    CARDS=$(a -X GET "$BASE/accounts/$id/cards")
    ACTIVE=$(echo "$CARDS" | grep -c '"status":"active"' || true)
    if [ "$ACTIVE" -eq 0 ]; then
        AID=$id
        break
    fi
done
# Jeśli nie ma konta bez kart, użyj pierwszego i wyczyść stare karty
if [ -z "$AID" ]; then
    AID=$(echo "$ACCOUNTS" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    echo "  Wszystkie konta mają karty — używam pierwszego: $AID"
    # Zablokuj istniejące karty żeby testy duplikatów działały
    for CID in $(a -X GET "$BASE/accounts/$AID/cards" | grep -o '"id":"[^"]*"' | cut -d'"' -f4); do
        a -X PATCH "$BASE/accounts/$AID/cards/$CID/status" -d '{"status":"blocked"}' > /dev/null 2>&1 || true
    done
fi
if [ -z "$AID" ]; then echo "BŁĄD: nie można znaleźć konta"; exit 1; fi
echo "  accountId: $AID"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 1. REJESTRACJA KART ===\e[0m"

# 1a. karta debitowa
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit","dailyLimit":500,"monthlyLimit":5000}')
DEBIT_ID=$(field "$R" "id")
DEBIT_TOKEN=$(field "$R" "externalCardToken")
check "1a. rejestracja karty debit → 201 + token" "$R" "tok_"
echo "     debitId=$DEBIT_ID token=$DEBIT_TOKEN"

# 1b. karta prepaid
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"prepaid","dailyLimit":200,"monthlyLimit":1000}')
PREPAID_ID=$(field "$R" "id")
PREPAID_TOKEN=$(field "$R" "externalCardToken")
check "1b. rejestracja karty prepaid → 201 + token" "$R" "tok_"
echo "     prepaidId=$PREPAID_ID token=$PREPAID_TOKEN"

# 1c. duplikat debit → błąd
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit"}')
check "1c. duplikat aktywnej debit → 400/409" "$R" "400\|409\|already\|active"

# 1d. duplikat prepaid → błąd
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"prepaid"}')
check "1d. duplikat aktywnej prepaid → 400/409" "$R" "400\|409\|already\|active"

# 1e. nieprawidłowy typ
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"credit"}')
check "1e. nieprawidłowy typ karty → 400" "$R" "400\|nvalid\|Allowed"

# 1f. monthly < daily → błąd
R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit","dailyLimit":1000,"monthlyLimit":100}')
check "1f. monthlyLimit < dailyLimit → 400" "$R" "400\|monthly\|daily\|less"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 2. POBIERANIE KART ===\e[0m"

R=$(a -X GET "$BASE/accounts/$AID/cards")
check "2a. lista kart → 2 karty" "$R" "debit.*prepaid\|prepaid.*debit"

R=$(a -X GET "$BASE/accounts/$AID/cards/$DEBIT_ID")
check "2b. pojedyncza karta debit → ok" "$R" "debit"

R=$(a -X GET "$BASE/accounts/$AID/cards/$PREPAID_ID")
check "2c. pojedyncza karta prepaid → ok" "$R" "prepaid"

R=$(a -X GET "$BASE/accounts/$AID/cards/00000000-0000-0000-0000-000000000000")
check "2d. nieistniejąca karta → 404" "$R" "404\|not found\|Not Found"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 3. EXTERNAL STATUS (payment-gateway) ===\e[0m"

R=$(a -X GET "$BASE/accounts/$AID/cards/$DEBIT_ID/external-status")
check "3a. external-status debit → status z gateway" "$R" "status\|card_token"

R=$(a -X GET "$BASE/accounts/$AID/cards/$PREPAID_ID/external-status")
check "3b. external-status prepaid → balance widoczne" "$R" "balance"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 4. TOPUP ===\e[0m"

echo "  (czekam 6s na lifecycle prepaid REQUESTED→ACTIVE...)"
sleep 6

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":100}')
check "4a. topup prepaid $100 → ok" "$R" "prepaid\|active\|id"

R=$(a -X GET "$BASE/accounts/$AID/cards/$PREPAID_ID/external-status")
BAL=$(numfield "$R" "balance")
check "4b. saldo po topup = 100" "$BAL" "100"

R=$(a -X POST "$BASE/accounts/$AID/cards/$DEBIT_ID/topup" -d '{"amount":50}')
check "4c. topup debit → 400 (tylko prepaid)" "$R" "400\|prepaid\|Only"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":-10}')
check "4d. topup ujemna kwota → 400" "$R" "400\|positive\|greater\|nvalid"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":0}')
check "4e. topup zero → 400" "$R" "400\|positive\|greater\|nvalid"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 5. LIMITY ===\e[0m"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{"dailyLimit":300,"monthlyLimit":2000}')
check "5a. zmiana limitów prepaid → ok" "$R" "300\|2000"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{"dailyLimit":500,"monthlyLimit":100}')
check "5b. monthlyLimit < dailyLimit → 400" "$R" "400\|monthly\|daily\|less"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{}')
check "5c. puste body limitów → 400" "$R" "400\|At least\|required"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/limits" -d '{"dailyLimit":1000}')
check "5d. zmiana limitu debit → ok" "$R" "1000"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 6. PŁATNOŚĆ POS — karta PREPAID ===\e[0m"

FULL=$(curl -s "$POS/api/v1/cards/$PREPAID_TOKEN/full-pan" -H "X-Admin-Key: $ADMIN_KEY")
PAN=$(echo "$FULL" | grep -o '"full_pan":"[^"]*"' | cut -d'"' -f4)
CVV=$(echo "$FULL" | grep -o '"cvv":"[^"]*"' | cut -d'"' -f4)
EXP_M=$(echo "$FULL" | grep -o '"expiry_month":[0-9]*' | cut -d':' -f2)
EXP_Y=$(echo "$FULL" | grep -o '"expiry_year":[0-9]*' | cut -d':' -f2)
check "6a. full-pan prepaid dostępne" "$PAN" "^[0-9]"

if [ -n "$PAN" ]; then
    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"$CVV\",\"amount\":25.00,\"currency\":\"USD\",\"merchant_id\":\"SUPERMARKET\"}")
    check "6b. płatność prepaid $25 → APPROVED" "$R" "true\|Approved\|approved"
    AUTH_CODE=$(field "$R" "authorization_code")
    echo "     authorization_code=$AUTH_CODE"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"$CVV\",\"amount\":999999.00,\"currency\":\"USD\",\"merchant_id\":\"SKLEP\"}")
    check "6c. płatność ponad saldo → DECLINED (insufficient funds)" "$R" "false\|DECLINED\|declined\|51"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"999\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_id\":\"SKLEP\"}")
    check "6d. zły CVV → DECLINED" "$R" "false\|DECLINED\|declined\|nvalid"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"1234567890123456\",\"expiry_month\":1,\"expiry_year\":30,\"cvv\":\"123\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_id\":\"SKLEP\"}")
    check "6e. zły PAN (Luhn fail) → 422/DECLINED" "$R" "422\|DECLINED\|declined\|nvalid"
fi

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 7. PŁATNOŚĆ POS — karta DEBIT ===\e[0m"

FULL_D=$(curl -s "$POS/api/v1/cards/$DEBIT_TOKEN/full-pan" -H "X-Admin-Key: $ADMIN_KEY")
PAN_D=$(echo "$FULL_D" | grep -o '"full_pan":"[^"]*"' | cut -d'"' -f4)
CVV_D=$(echo "$FULL_D" | grep -o '"cvv":"[^"]*"' | cut -d'"' -f4)
EXP_MD=$(echo "$FULL_D" | grep -o '"expiry_month":[0-9]*' | cut -d':' -f2)
EXP_YD=$(echo "$FULL_D" | grep -o '"expiry_year":[0-9]*' | cut -d':' -f2)
check "7a. full-pan debit dostępne" "$PAN_D" "^[0-9]"

if [ -n "$PAN_D" ]; then
    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN_D\",\"expiry_month\":$EXP_MD,\"expiry_year\":$EXP_YD,\"cvv\":\"$CVV_D\",\"amount\":15.00,\"currency\":\"USD\",\"merchant_id\":\"KAWIARNIA\"}")
    check "7b. płatność debit $15 → APPROVED" "$R" "true\|Approved\|approved"
fi

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 8. BLOKOWANIE / ODBLOKOWANIE ===\e[0m"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"blocked"}')
check "8a. zablokuj debit → blocked" "$R" "blocked"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"active"}')
check "8b. odblokuj debit od razu → 400 cooldown 24h" "$R" "400\|cooldown\|cannot be unblocked\|until\|24"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"expired"}')
check "8c. ustaw status=expired przez API → 400" "$R" "400\|nvalid\|Allowed\|expired"

# płatność zablokowaną kartą
if [ -n "$PAN_D" ]; then
    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN_D\",\"expiry_month\":$EXP_MD,\"expiry_year\":$EXP_YD,\"cvv\":\"$CVV_D\",\"amount\":10.00,\"currency\":\"USD\",\"merchant_id\":\"SKLEP\"}")
    check "8d. płatność zablokowaną kartą → DECLINED" "$R" "false\|DECLINED\|declined\|blocked"
fi

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/status" -d '{"status":"blocked"}')
check "8e. zablokuj prepaid → blocked" "$R" "blocked"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/status" -d '{"status":"active"}')
check "8f. odblokuj prepaid od razu → 400 cooldown" "$R" "400\|cooldown\|cannot be unblocked\|until\|24"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 9. TOPUP ZABLOKOWANEJ KARTY ===\e[0m"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":50}')
check "9a. topup zablokowanej prepaid → 503 (gateway odrzuca topup na blocked)" "$R" "503\|unavailable\|blocked"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 10. CAPTURE WEBHOOK (settlement) ===\e[0m"

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":15.00,\"currency\":\"USD\",\"merchant_id\":\"KAWIARNIA\",\"authorization_code\":\"TEST01\"}")
check "10a. capture prawidłowy token → SETTLED" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" \
    -d '{"card_token":"tok_nieistnieje_000","amount":10.0,"merchant_id":"SKLEP"}')
check "10b. capture nieznany token → SETTLED (nie blokuje)" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":-5.0}")
check "10c. capture ujemna kwota → 400" "$R" "400\|greater\|nvalid\|positive"

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":0}")
check "10d. capture zero → 400" "$R" "400\|greater\|nvalid\|positive"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 11. AUTORYZACJA — brak tokenu JWT ===\e[0m"

S=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/accounts/$AID/cards")
check "11a. GET /cards bez tokenu → 401" "$S" "401"

S=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/accounts/$AID/cards" \
    -H "Content-Type: application/json" -d '{"type":"debit"}')
check "11b. POST /cards bez tokenu → 401" "$S" "401"

S=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" \
    -H "Content-Type: application/json" -d '{"amount":10}')
check "11c. POST /topup bez tokenu → 401" "$S" "401"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 12. DOSTĘP INNEGO UŻYTKOWNIKA ===\e[0m"

OTHER_REG=$(j -X POST "$BASE/auth/register" \
    -d "{\"email\":\"other_$$@example.com\",\"password\":\"Test1234!\",\"firstName\":\"Other\",\"lastName\":\"User\"}")
OTHER_LOGIN=$(j -X POST "$BASE/auth/login" \
    -d "{\"email\":\"other_$$@example.com\",\"password\":\"Test1234!\"}")
JWT2=$(field "$OTHER_LOGIN" "token")

R=$(curl -s -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards")
check "12a. inny user: GET /cards → 403/404" "$R" "403\|404\|denied\|Access\|not found"

R=$(curl -s -X POST -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards" -d '{"type":"prepaid"}')
check "12b. inny user: POST /cards → 403/404" "$R" "403\|404\|denied\|Access"

R=$(curl -s -X POST -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":10}')
check "12c. inny user: topup → 403/404" "$R" "403\|404\|denied\|Access"

# ══════════════════════════════════════════════════════════════════════════════
echo -e "\n\e[36m=== 13. SETTLEMENT W HISTORII (czekam 35s na card-provider) ===\e[0m"
echo "  (płatności z sekcji 6 i 7 powinny pojawić się jako transakcje...)"
sleep 35

R=$(a -X GET "$BASE/accounts/$AID/transactions?page=1&pageSize=20")
TX_COUNT=$(echo "$R" | grep -o '"total":[0-9]*' | cut -d':' -f2)
check "13a. historia zawiera transakcje kartowe" "$TX_COUNT" "[1-9]"
check "13b. transakcja zawiera opis karty" "$R" "Card payment\|Card settlement"

# ══════════════════════════════════════════════════════════════════════════════
echo ""
echo "═══════════════════════════════════════════"
printf "  \e[32mPASSED: %d\e[0m   \e[31mFAILED: %d\e[0m\n" "$ok" "$fail"
echo "═══════════════════════════════════════════"
echo ""
echo "Konto testowe: $AID"
echo "Debit  token:  $DEBIT_TOKEN"
echo "Prepaid token: $PREPAID_TOKEN"
echo "Historia konta: $BASE/accounts/$AID/transactions"
echo "POS UI:         $POS/pos"
