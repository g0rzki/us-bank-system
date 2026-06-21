#!/usr/bin/env bash
# Pełny test flow: register -> login -> account -> debit card -> prepaid card -> topup -> block
# Użycie: bash test-flow.sh

BASE="http://localhost:5100"
POS="http://localhost:8072"

EMAIL="testuser_$RANDOM@example.com"
PASS="Test1234!"

j() { curl -s -H "Content-Type: application/json" "$@"; }
a() { j -H "Authorization: Bearer $JWT" "$@"; }
field() { echo "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }

echo "=== 1. Register ($EMAIL) ==="
REG=$(j -X POST "$BASE/auth/register" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"firstName\":\"Test\",\"lastName\":\"User\"}")
echo "$REG"

echo -e "\n=== 2. Login ==="
LOGIN=$(j -X POST "$BASE/auth/login" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
JWT=$(field "$LOGIN" "token")
echo "JWT: ${JWT:0:50}..."

echo -e "\n=== 3. Create checking account ==="
ACCOUNT=$(a -X POST "$BASE/accounts" -d '{"type":"checking","currency":"USD"}')
ACCOUNT_ID=$(field "$ACCOUNT" "id")
echo "accountId: $ACCOUNT_ID"

echo -e "\n=== 4. Register debit card ==="
CARD=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"debit"}')
CARD_ID=$(field "$CARD" "id")
CARD_TOKEN=$(field "$CARD" "externalCardToken")
echo "cardId: $CARD_ID"
echo "externalCardToken: $CARD_TOKEN"

echo -e "\n=== 5. Register prepaid card ==="
PREPAID=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards" -d '{"type":"prepaid","dailyLimit":200,"monthlyLimit":1000}')
PREPAID_ID=$(field "$PREPAID" "id")
PREPAID_TOKEN=$(field "$PREPAID" "externalCardToken")
echo "prepaidId: $PREPAID_ID"
echo "externalCardToken: $PREPAID_TOKEN"

echo -e "\n=== 6. Czekam 6s na lifecycle prepaid (REQUESTED→ACTIVE) ==="
sleep 6
STATUS_RESP=$(a -X GET "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID")
PREPAID_STATUS=$(field "$STATUS_RESP" "status")
echo "Prepaid status po lifecycle: $PREPAID_STATUS"

echo -e "\n=== 7. Top-up prepaid card (\$50) ==="
TOPUP=$(a -X POST "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/topup" -d '{"amount":50}')
echo "$TOPUP"

echo -e "\n=== 8. List cards ==="
a -X GET "$BASE/accounts/$ACCOUNT_ID/cards"

echo -e "\n=== 9. Pobierz pełne dane prepaid (admin) + płatność POS ==="
if [ -n "$PREPAID_TOKEN" ]; then
    FULL=$(curl -s "$POS/api/v1/cards/$PREPAID_TOKEN/full-pan" -H "X-Admin-Key: admin-secret-key-2026")
    echo "Dane karty: $FULL"
    PAN=$(echo "$FULL" | grep -o '"full_pan":"[^"]*"' | cut -d'"' -f4)
    CVV=$(echo "$FULL" | grep -o '"cvv":"[^"]*"' | cut -d'"' -f4)
    EXP_M=$(echo "$FULL" | grep -o '"expiry_month":[0-9]*' | cut -d':' -f2)
    EXP_Y=$(echo "$FULL" | grep -o '"expiry_year":[0-9]*' | cut -d':' -f2)
    echo "PAN=$PAN CVV=$CVV EXP=$EXP_M/$EXP_Y"
    if [ -n "$PAN" ]; then
        POS_RESP=$(j -X POST "$POS/api/v1/payments/authorize" \
            -d "{\"card_number\":\"$PAN\",\"expiry_month\":$EXP_M,\"expiry_year\":$EXP_Y,\"cvv\":\"$CVV\",\"amount\":12.50,\"currency\":\"USD\",\"merchant_id\":\"TEST_SHOP\"}")
        echo "Płatność: $POS_RESP"
    fi
fi

echo -e "\n=== 10. Block debit card ==="
BLOCKED=$(a -X PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/status" -d '{"status":"blocked"}')
BLOCK_STATUS=$(field "$BLOCKED" "status")
echo "Debit status: $BLOCK_STATUS"

echo -e "\n=== 11. Transakcje ==="
a -X GET "$BASE/accounts/$ACCOUNT_ID/transactions"

echo -e "\n=== DONE ==="
echo "Debit token:   $CARD_TOKEN"
echo "Prepaid token: $PREPAID_TOKEN"
echo "POS UI:        $POS/pos"
