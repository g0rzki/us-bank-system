#!/usr/bin/env bash
# Quick connectivity & HMAC verification for the bank ↔ karty-platnicze integration.
# Designed for the card team (Filip et al.) — no bank API knowledge needed.
#
# Usage:
#   bash verify-cards-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3, docker

set -euo pipefail
cd "$(dirname "$0")/.."

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
GATEWAY_URL="http://localhost:8072"
API_KEY="${CARDS_API_KEY:?CARDS_API_KEY not set in .env}"
HMAC_SECRET="${CARDS_HMAC_SECRET:?CARDS_HMAC_SECRET not set in .env}"
ADMIN_KEY="${CARDS_ADMIN_KEY:?CARDS_ADMIN_KEY not set in .env}"
TEST_EMAIL="${TEST_USER_EMAIL:-bob.wilson@example.com}"
TEST_PASSWORD="${TEST_USER_PASSWORD:-Test123!}"

ok=0; fail=0; total=0

check() {
  local desc="$1" got="$2" want="$3"
  total=$((total+1))
  if echo "$got" | grep -qi "$want"; then
    printf "  \033[32mPASS\033[0m  %s\n" "$desc"; ok=$((ok+1))
  else
    printf "  \033[31mFAIL\033[0m  %s\n         got:  %s\n         want: %s\n" "$desc" "$got" "$want"; fail=$((fail+1))
  fi
}

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  Bank ↔ Karty-Platnicze  Integration Verification       ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

GW_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/api/v1/cards" \
  -H "X-Admin-Key: $ADMIN_KEY" 2>/dev/null || echo "000")
check "Payment Gateway ($GATEWAY_URL)" "$GW_HEALTH" "200"

# ── 2. Docker network ─────────────────────────────────────────────────────────
echo ""
echo "▶ 2. Docker network (clearing-us-a-karty)"
NET_CONTAINERS=$(docker network inspect clearing-us-a-karty 2>/dev/null \
  | python3 -c "import sys,json; d=json.load(sys.stdin)[0]; print(' '.join(v['Name'] for v in d.get('Containers',{}).values()))" 2>/dev/null || echo "")
check "us-bank-a on network" "$NET_CONTAINERS" "us-bank-a"
check "cards_gateway_app on network" "$NET_CONTAINERS" "cards_gateway_app"
check "cards_provider_app on network" "$NET_CONTAINERS" "cards_provider_app"

# ── 3. HMAC handshake (issue test card) ───────────────────────────────────────
echo ""
echo "▶ 3. HMAC handshake (card issue via signed request)"
# NOTE: The only HMAC-verified endpoint on the gateway is POST /cards/issue,
# which creates a card. On first run a test prepaid card will be created for
# the test user. Subsequent runs hit the 409 duplicate guard — no new resources
# are created. Both 201 and 409 confirm that HMAC authentication passed
# (a broken signature would return 401/503 instead).

JWT=$(curl -s -X POST "$BANK_URL/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\"}" 2>/dev/null \
  | python3 -c "import sys,json; print(json.load(sys.stdin).get('token',''))" 2>/dev/null || echo "")

if [[ -z "$JWT" ]]; then
  check "Bank login ($TEST_EMAIL)" "" "token"
else
  check "Bank login ($TEST_EMAIL)" "token present" "token present"

  ACCT=$(curl -s "$BANK_URL/accounts" -H "Authorization: Bearer $JWT" 2>/dev/null \
    | python3 -c "import sys,json; accs=json.load(sys.stdin); print(next((a['id'] for a in accs if a['type']=='checking'),''))" 2>/dev/null || echo "")

  if [[ -n "$ACCT" ]]; then
    ISSUE=$(curl -s -w "\n%{http_code}" -X POST "$BANK_URL/accounts/$ACCT/cards" \
      -H "Authorization: Bearer $JWT" \
      -H "Content-Type: application/json" \
      -d '{"type":"prepaid"}' 2>/dev/null)
    ISSUE_HTTP=$(echo "$ISSUE" | tail -1)
    ISSUE_BODY=$(echo "$ISSUE" | sed '$d')

    if [[ "$ISSUE_HTTP" == "201" ]]; then
      MASKED=$(echo "$ISSUE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('maskedPan',''))" 2>/dev/null)
      check "HMAC-signed card issue (201, first run)" "$MASKED" "4700"
    elif [[ "$ISSUE_HTTP" == "409" ]]; then
      check "HMAC-signed request accepted (409 = card exists, HMAC OK)" "$ISSUE_HTTP" "409"
    else
      check "HMAC-signed card issue" "HTTP $ISSUE_HTTP: $ISSUE_BODY" "201 or 409"
    fi
  fi
fi

# ── 4. Capture endpoint reachable ─────────────────────────────────────────────
echo ""
echo "▶ 4. Capture webhook (card-provider → bank)"
CAPTURE=$(curl -s -w "\n%{http_code}" -X POST "$BANK_URL/capture" \
  -H "Content-Type: application/json" \
  -d '{"transaction_id":"verify-test","authorization_code":"000000","amount":0.01,"currency":"USD","merchant_id":"VERIFY","card_token":"tok_nonexistent"}' 2>/dev/null)
CAPTURE_HTTP=$(echo "$CAPTURE" | tail -1)
check "POST /capture reachable" "$CAPTURE_HTTP" "200"

INTERNAL=$(docker exec cards_provider_app python3 -c "
import urllib.request, json
try:
    req = urllib.request.Request('http://us-bank-a:8000/capture',
        data=json.dumps({'transaction_id':'net-test','authorization_code':'000000','amount':0.01,'currency':'USD','merchant_id':'VERIFY','card_token':'tok_none'}).encode(),
        headers={'Content-Type':'application/json'}, method='POST')
    resp = urllib.request.urlopen(req, timeout=5)
    print(resp.status)
except Exception as e:
    print(f'ERR: {e}')
" 2>/dev/null || echo "ERR: container not reachable")
check "Internal capture (cards_provider → us-bank-a:8000)" "$INTERNAL" "200"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [[ $fail -eq 0 ]]; then
  printf "\033[32m  ALL %d CHECKS PASSED\033[0m\n" "$total"
else
  printf "\033[31m  %d/%d FAILED\033[0m\n" "$fail" "$total"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
exit $fail
