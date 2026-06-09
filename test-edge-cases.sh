#!/usr/bin/env bash
# Edge case tests
# Użycie: bash test-edge-cases.sh

BASE="http://localhost:5100"
POS="http://localhost:8072"

PASS="Test1234!"
PASS_SHORT="abc"
PASS_WEAK="password"

j() { curl -s -H "Content-Type: application/json" "$@"; }
a() { j -H "Authorization: Bearer $JWT" "$@"; }
field() { echo "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }

PASS="Test1234!"
ok=0; fail=0

check() {
    local desc="$1" got="$2" want="$3"
    if echo "$got" | grep -q "$want"; then
        echo "  PASS: $desc"
        ((ok++))
    else
        echo "  FAIL: $desc"
        echo "        got:  $got"
        echo "        want: $want"
        ((fail++))
    fi
}

# Setup: jeden user ze sprawnym kontem i kartami
EMAIL="edge_$RANDOM@example.com"
j -X POST "$BASE/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"firstName\":\"Edge\",\"lastName\":\"Test\"}" > /dev/null
LOGIN=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
JWT=$(field "$LOGIN" "token")
ACCOUNT=$(a -X POST "$BASE/accounts" -d '{"type":"checking","currency":"USD"}')
ACCOUNT_ID=$(field "$ACCOUNT" "id")

echo "Setup: user=$EMAIL account=$ACCOUNT_ID"
echo ""

# --- AUTH ---
echo "=== AUTH ==="

R=$(j -X POST "$BASE/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"firstName\":\"A\",\"lastName\":\"B\"}")
check "duplicate email → 409" "$R" "409\|already\|conflict\|duplicate\|exists"

R=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"wrongpass\"}")
check "wrong password → 401" "$R" "401\|nvalid\|Unauthorized\|unauthorized"

R=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"notexist@x.com\",\"password\":\"$PASS\"}")
check "non-existent user → 401" "$R" "401\|nvalid\|Unauthorized\|not found"

# --- CARDS: duplicate ---
echo ""
echo "=== CARDS: duplicate ==="

CARD=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"debit"}')
CARD_ID=$(field "$CARD" "id")
CARD_TOKEN=$(field "$CARD" "externalCardToken")

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"debit"}')
check "duplicate active debit → 409/400" "$R" "409\|400\|already\|active\|duplicate"

# --- CARDS: invalid type ---
echo ""
echo "=== CARDS: validation ==="

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"credit"}')
check "invalid card type → 400" "$R" "400\|nvalid\|credit\|Allowed"

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"debit","dailyLimit":1000,"monthlyLimit":100}')
check "monthly < daily → 400" "$R" "400\|monthly\|daily\|less than"

# --- CARDS: wrong account ---
echo ""
echo "=== CARDS: authorization ==="

EMAIL2="edge2_$RANDOM@example.com"
j -X POST "$BASE/auth/register" -d "{\"email\":\"$EMAIL2\",\"password\":\"$PASS\",\"firstName\":\"B\",\"lastName\":\"B\"}" > /dev/null
LOGIN2=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL2\",\"password\":\"$PASS\"}")
JWT2=$(field "$LOGIN2" "token")

R=$(j -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    -X GET "$BASE/accounts/$ACCOUNT_ID/cards")
check "other user cannot list cards → 403/404" "$R" "403\|404\|denied\|not found\|Access"

R=$(j -H "Authorization: Bearer $JWT2" -H "Content-Type: application/json" \
    -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"prepaid"}')
check "other user cannot register card → 403/404" "$R" "403\|404\|denied\|Access"

# --- CARDS: block/unblock ---
echo ""
echo "=== CARDS: block/unblock ==="

R=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/status" -d '{"status":"blocked"}')
check "block active card → ok (blocked)" "$R" "blocked"

R=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/status" -d '{"status":"active"}')
check "unblock immediately → 400 cooldown" "$R" "400\|cooldown\|cannot be unblocked\|24h\|until"

R=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/status" -d '{"status":"expired"}')
check "set status=expired via API → 400" "$R" "400\|nvalid\|Allowed\|expired"

# --- PREPAID: topup edge cases ---
echo ""
echo "=== PREPAID: topup ==="

PREPAID=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"prepaid"}')
PREPAID_ID=$(field "$PREPAID" "id")
echo "  (waiting 6s for prepaid lifecycle...)"
sleep 6
a -X GET "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID" > /dev/null

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/topup" -d '{"amount":50}')
check "topup debit card → 400" "$R" "400\|prepaid\|Only prepaid"

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/topup" -d '{"amount":-10}')
check "topup negative amount → 400" "$R" "400\|positive\|greater\|nvalid"

R=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/topup" -d '{"amount":0}')
check "topup zero amount → 400" "$R" "400\|positive\|greater\|nvalid"

# --- LIMITS ---
echo ""
echo "=== LIMITS ==="

R=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/limits" -d '{}')
check "update limits with empty body → 400" "$R" "400\|At least one\|required"

R=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/limits" -d '{"dailyLimit":500,"monthlyLimit":100}')
check "monthly < daily → 400" "$R" "400\|monthly\|daily\|less than"

# --- CAPTURE (webhook) ---
echo ""
echo "=== CAPTURE webhook ==="

R=$(j -X POST "$BASE/capture" -d "{\"cardToken\":\"$CARD_TOKEN\",\"amount\":25.0,\"merchantId\":\"SHOP\"}")
check "capture valid token → 200 SETTLED" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" -d '{"cardToken":"tok_nonexistent_000","amount":10.0}')
check "capture unknown token → 200 SETTLED (nie blokuje settlement)" "$R" "SETTLED"

R=$(j -X POST "$BASE/capture" -d "{\"cardToken\":\"$CARD_TOKEN\",\"amount\":-5.0}")
check "capture negative amount → 400" "$R" "400\|greater\|nvalid\|positive"

R=$(j -X POST "$BASE/capture" -d "{\"cardToken\":\"$CARD_TOKEN\",\"amount\":0}")
check "capture zero amount → 400" "$R" "400\|greater\|nvalid\|positive"

# --- POS payment ---
echo ""
echo "=== POS: payment edge cases ==="

FULL=$(curl -s "$POS/api/v1/cards/$CARD_TOKEN/full-pan" -H "X-Admin-Key: admin-secret-key-2026")
PAN=$(echo "$FULL" | grep -o '"full_pan":"[^"]*"' | cut -d'"' -f4)
CVV=$(echo "$FULL" | grep -o '"cvv":"[^"]*"' | cut -d'"' -f4)
EXP_M=$(echo "$FULL" | grep -o '"expiry_month":[0-9]*' | cut -d':' -f2)
EXP_Y=$(echo "$FULL" | grep -o '"expiry_year":[0-9]*' | cut -d':' -f2)

if [ -n "$PAN" ]; then
    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"$CVV\",\"amount\":10.0,\"currency\":\"USD\"}")
    check "POS: blocked debit card → DECLINED" "$R" "DECLINED\|declined\|blocked\|Blocked\|false"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"999\",\"amount\":10.0}")
    check "POS: wrong CVV → DECLINED" "$R" "DECLINED\|declined\|false\|nvalid"

    R=$(j -X POST "$POS/api/v1/payments/authorize" \
        -d "{\"card_number\":\"1234567890123456\",\"expiry_month\":1,\"expiry_year\":30,\"cvv\":\"123\",\"amount\":10.0}")
    check "POS: invalid PAN (Luhn fail) → 422/DECLINED" "$R" "422\|DECLINED\|declined\|nvalid\|false"
else
    echo "  SKIP: POS tests (brak pełnych danych karty)"
fi

# --- TRANSFERS: no-auth ---
echo ""
echo "=== NO AUTH ==="

STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/accounts")
check "GET /accounts without token → 401" "$STATUS" "401"

STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/accounts/$ACCOUNT_ID/cards")
check "GET /cards without token → 401" "$STATUS" "401"

# --- SUMMARY ---
echo ""
echo "==================================="
echo "PASSED: $ok   FAILED: $fail"
echo "==================================="
