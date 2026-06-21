#!/usr/bin/env bash
# Edge case tests — karty płatnicze
# Używa konta john.doe@example.com (seed user)
# Użycie: bash test-edge-cases.sh

BASE="http://localhost:5100"
POS="http://localhost:8072"
ADMIN_KEY="admin-secret-key-2026"

EMAIL="john.doe@example.com"
PASS="Test123!"

ok=0; fail=0

j()     { curl -s -H "Content-Type: application/json" "$@"; }
a()     { j -H "Authorization: Bearer $JWT" "$@"; }
field() { echo "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }

check() {
    local desc="$1" got="$2" want="$3"
    if echo "$got" | grep -qi "$want"; then
        printf "  \e[32mPASS\e[0m %s\n" "$desc"; ((ok++))
    else
        printf "  \e[31mFAIL\e[0m %s\n        got:  %s\n        want: %s\n" "$desc" "$got" "$want"; ((fail++))
    fi
}

# ── setup ─────────────────────────────────────────────────────────────────────
LOGIN=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
JWT=$(field "$LOGIN" "token")
if [ -z "$JWT" ]; then echo "BŁĄD: nie można zalogować jako $EMAIL"; exit 1; fi

ACCOUNTS=$(a -X GET "$BASE/accounts")
AID=""
for id in $(echo "$ACCOUNTS" | grep -o '"id":"[^"]*"' | cut -d'"' -f4); do
    CARDS=$(a -X GET "$BASE/accounts/$id/cards")
    ACTIVE=$(echo "$CARDS" | grep -c '"status":"active"' || true)
    if [ "$ACTIVE" -eq 0 ]; then AID=$id; break; fi
done
if [ -z "$AID" ]; then
    AID=$(echo "$ACCOUNTS" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
fi
if [ -z "$AID" ]; then echo "BŁĄD: brak konta"; exit 1; fi

DEBIT=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit","dailyLimit":500,"monthlyLimit":5000}')
DEBIT_ID=$(field "$DEBIT" "id")
DEBIT_TOKEN=$(field "$DEBIT" "externalCardToken")

PREPAID=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"prepaid","dailyLimit":200,"monthlyLimit":1000}')
PREPAID_ID=$(field "$PREPAID" "id")
PREPAID_TOKEN=$(field "$PREPAID" "externalCardToken")

echo "Setup: $EMAIL | account=$AID"
echo "       debit=$DEBIT_ID | prepaid=$PREPAID_ID"
echo ""

# ── 1. AUTH ────────────────────────────────────────────────────────────────────
echo "=== 1. AUTH ==="

R=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"wrongpass\"}")
check "1a. złe hasło → 401" "$R" "401\|nvalid\|Unauthorized"

R=$(j -X POST "$BASE/auth/login" -d '{"email":"nobody@example.com","password":"Test123!"}')
check "1b. nieistniejący user → 401" "$R" "401\|nvalid\|not found"

R=$(j -X POST "$BASE/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"firstName\":\"A\",\"lastName\":\"B\"}")
check "1c. rejestracja duplikatu email → 409" "$R" "409\|already\|conflict\|duplicate\|exists"

# ── 2. WALIDACJA REJESTRACJI KART ─────────────────────────────────────────────
echo ""
echo "=== 2. REJESTRACJA — walidacja ==="

R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit"}')
check "2a. duplikat aktywnej debit → 400/409" "$R" "400\|409\|already\|active"

R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"prepaid"}')
check "2b. duplikat aktywnej prepaid → 400/409" "$R" "400\|409\|already\|active"

R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"credit"}')
check "2c. nieprawidłowy typ → 400" "$R" "400\|nvalid\|Allowed"

R=$(a -X POST "$BASE/accounts/$AID/cards" -d '{"type":"debit","dailyLimit":1000,"monthlyLimit":100}')
check "2d. monthlyLimit < dailyLimit → 400" "$R" "400\|monthly\|daily\|less"

# ── 3. AUTORYZACJA ─────────────────────────────────────────────────────────────
echo ""
echo "=== 3. AUTORYZACJA ==="

OTHER_EMAIL="edgeother_$$@example.com"
j -X POST "$BASE/auth/register" \
    -d "{\"email\":\"$OTHER_EMAIL\",\"password\":\"Test1234!\",\"firstName\":\"Other\",\"lastName\":\"User\"}" > /dev/null
OTHER_LOGIN=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$OTHER_EMAIL\",\"password\":\"Test1234!\"}")
JWT2=$(field "$OTHER_LOGIN" "token")

R=$(curl -s -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards")
check "3a. inny user GET /cards → 403/404" "$R" "403\|404\|denied\|Access\|not found"

R=$(curl -s -X POST -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards" -d '{"type":"prepaid"}')
check "3b. inny user POST /cards → 403/404" "$R" "403\|404\|denied\|Access"

R=$(curl -s -X POST -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":10}')
check "3c. inny user topup → 403/404" "$R" "403\|404\|denied\|Access"

S=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/accounts/$AID/cards")
check "3d. GET /cards bez tokenu → 401" "$S" "401"

S=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/accounts/$AID/cards" \
    -H "Content-Type: application/json" -d '{"type":"debit"}')
check "3e. POST /cards bez tokenu → 401" "$S" "401"

# ── 4. LIMITY ─────────────────────────────────────────────────────────────────
echo ""
echo "=== 4. LIMITY ==="

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{}')
check "4a. puste body → 400" "$R" "400\|At least\|required"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{"dailyLimit":500,"monthlyLimit":100}')
check "4b. monthly < daily → 400" "$R" "400\|monthly\|daily\|less"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/limits" -d '{"dailyLimit":200,"monthlyLimit":2000}')
check "4c. prawidłowe limity → ok" "$R" "200\|2000"

# ── 5. TOPUP — edge cases ──────────────────────────────────────────────────────
echo ""
echo "=== 5. TOPUP ==="
echo "  (czekam 6s na lifecycle prepaid...)"
sleep 6

R=$(a -X POST "$BASE/accounts/$AID/cards/$DEBIT_ID/topup" -d '{"amount":50}')
check "5a. topup karty debit → 400" "$R" "400\|prepaid\|Only"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":-10}')
check "5b. topup ujemna kwota → 400" "$R" "400\|positive\|greater\|nvalid"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":0}')
check "5c. topup zero → 400" "$R" "400\|positive\|greater\|nvalid"

# ── 6. BLOKADA / COOLDOWN ─────────────────────────────────────────────────────
echo ""
echo "=== 6. BLOKADA / COOLDOWN ==="

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"blocked"}')
check "6a. zablokuj debit → blocked" "$R" "blocked"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"active"}')
check "6b. natychmiastowe odblokowanie → 400 (cooldown 24h)" "$R" "400\|cooldown\|cannot be unblocked\|until"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$DEBIT_ID/status" -d '{"status":"expired"}')
check "6c. ustaw expired przez API → 400" "$R" "400\|nvalid\|Allowed\|expired"

R=$(a -X PATCH "$BASE/accounts/$AID/cards/$PREPAID_ID/status" -d '{"status":"blocked"}')
check "6d. zablokuj prepaid → blocked" "$R" "blocked"

R=$(a -X POST "$BASE/accounts/$AID/cards/$PREPAID_ID/topup" -d '{"amount":50}')
check "6e. topup zablokowanej prepaid → 503 (gateway odmawia)" "$R" "503\|unavailable\|blocked"

# ── 7. CAPTURE WEBHOOK ────────────────────────────────────────────────────────
echo ""
echo "=== 7. CAPTURE webhook ==="

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":15.00,\"currency\":\"USD\",\"merchant_id\":\"SHOP\",\"authorization_code\":\"EC01\"}")
check "7a. capture prawidłowy token → SETTLED" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" \
    -d '{"card_token":"tok_nieistnieje_000","amount":10.0,"merchant_id":"SHOP"}')
check "7b. capture nieznany token → SETTLED (nie blokuje settlement)" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":-5.0}")
check "7c. capture ujemna kwota → 400" "$R" "400\|greater\|nvalid\|positive"

R=$(j -X POST "$BASE/capture" \
    -d "{\"card_token\":\"$DEBIT_TOKEN\",\"amount\":0}")
check "7d. capture zero → 400" "$R" "400\|greater\|nvalid\|positive"

# ── 8. POS — edge cases ───────────────────────────────────────────────────────
echo ""
echo "=== 8. POS — edge cases ==="

FULL_D=$(curl -s "$POS/api/v1/cards/$DEBIT_TOKEN/full-pan" -H "X-Admin-Key: $ADMIN_KEY")
PAN_D=$(echo "$FULL_D" | grep -o '"full_pan":"[^"]*"' | cut -d'"' -f4)
CVV_D=$(echo "$FULL_D" | grep -o '"cvv":"[^"]*"' | cut -d'"' -f4)
EXP_MD=$(echo "$FULL_D" | grep -o '"expiry_month":[0-9]*' | cut -d':' -f2)
EXP_YD=$(echo "$FULL_D" | grep -o '"expiry_year":[0-9]*' | cut -d':' -f2)

if [ -n "$PAN_D" ]; then
    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN_D\",\"expiry_month\":$EXP_MD,\"expiry_year\":$EXP_YD,\"cvv\":\"$CVV_D\",\"amount\":10.00,\"currency\":\"USD\"}")
    check "8a. płatność zablokowaną kartą → DECLINED" "$R" "false\|DECLINED\|declined\|blocked"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN_D\",\"expiry_month\":$EXP_MD,\"expiry_year\":$EXP_YD,\"cvv\":\"999\",\"amount\":10.00,\"currency\":\"USD\"}")
    check "8b. zły CVV → DECLINED" "$R" "false\|DECLINED\|declined\|nvalid"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"1234567890123456\",\"expiry_month\":1,\"expiry_year\":30,\"cvv\":\"123\",\"amount\":10.00,\"currency\":\"USD\"}")
    check "8c. nieważny PAN (Luhn fail) → 422/DECLINED" "$R" "422\|DECLINED\|declined\|nvalid"
else
    echo "  SKIP: POS testy (brak pełnych danych karty — sprawdź połączenie z cards-backend)"
fi

# ── summary ───────────────────────────────────────────────────────────────────
echo ""
echo "==================================="
printf "  \e[32mPASSED: %d\e[0m   \e[31mFAILED: %d\e[0m\n" "$ok" "$fail"
echo "==================================="
