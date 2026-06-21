#!/usr/bin/env bash
# Quick connectivity check for the bank <-> SWIFT Middleware integration.
# Designed for the Jkwasnyy team — no bank API knowledge needed.
#
# All checks are read-only — no transfers are created, no state is modified.
#
# Usage:
#   bash verify-swift-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3

set -euo pipefail
cd "$(dirname "$0")/.."

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
SWIFT_URL="${INTEGRATIONS_SWIFT_URL:-http://localhost:3000}"
SWIFT_CLIENT_ID="${Swift__ClientId:?Swift__ClientId not set in .env}"
SWIFT_CLIENT_SECRET="${Swift__ClientSecret:?Swift__ClientSecret not set in .env}"
SWIFT_BIC="${Swift__Bic:-USBKUS01XXX}"
SWIFT_WEBHOOK_SECRET="${Swift__WebhookSecret:-dev_swift_webhook_secret}"

# SWIFT URL may use host.docker.internal — replace for host access
SWIFT_URL_HOST=$(echo "$SWIFT_URL" | sed 's|host.docker.internal|localhost|')

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
echo "║  Bank ↔ SWIFT Middleware  Integration Verification       ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

SWIFT_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$SWIFT_URL_HOST/" 2>/dev/null || echo "000")
check "SWIFT Middleware ($SWIFT_URL_HOST)" "$SWIFT_HEALTH" "200"

# ── 2. Configuration sanity ──────────────────────────────────────────────────
echo ""
echo "▶ 2. Configuration sanity"
if [[ "$SWIFT_CLIENT_ID" == "test-client" ]]; then
  total=$((total+1))
  printf "  \033[31mFAIL\033[0m  Swift__ClientId is 'test-client' (maps to BIC PLBKPL01XXX, not %s)\n" "$SWIFT_BIC"
  fail=$((fail+1))
else
  total=$((total+1))
  printf "  \033[32mPASS\033[0m  Swift__ClientId is '%s' (not test-client)\n" "$SWIFT_CLIENT_ID"
  ok=$((ok+1))
fi

SWIFT_PORT=$(echo "$SWIFT_URL" | grep -oE ':[0-9]+' | tail -1 | tr -d ':')
if [[ "${SWIFT_PORT:-}" == "6004" ]]; then
  total=$((total+1))
  printf "  \033[31mFAIL\033[0m  INTEGRATIONS_SWIFT_URL points to port 6004 (incompatible mock)\n"
  fail=$((fail+1))
else
  total=$((total+1))
  printf "  \033[32mPASS\033[0m  INTEGRATIONS_SWIFT_URL port is %s (not 6004 mock)\n" "${SWIFT_PORT:-default}"
  ok=$((ok+1))
fi

if [[ -n "${Swift__WebhookSecret:-}" ]]; then
  total=$((total+1))
  printf "  \033[32mPASS\033[0m  Swift__WebhookSecret is configured\n"
  ok=$((ok+1))
else
  total=$((total+1))
  printf "  \033[31mFAIL\033[0m  Swift__WebhookSecret is empty — webhook endpoint is unauthenticated\n"
  fail=$((fail+1))
fi

# ── 3. OAuth2 token acquisition ─────────────────────────────────────────────
echo ""
echo "▶ 3. OAuth2 token acquisition"
TOKEN_RESP=$(curl -s -w "\n%{http_code}" -X POST "$SWIFT_URL_HOST/auth/token" \
  -d "client_id=${SWIFT_CLIENT_ID}&client_secret=${SWIFT_CLIENT_SECRET}&grant_type=client_credentials" \
  2>/dev/null || echo -e "\n000")
TOKEN_BODY=$(echo "$TOKEN_RESP" | sed '$d')
TOKEN_HTTP=$(echo "$TOKEN_RESP" | tail -1)
check "POST /auth/token with valid credentials" "$TOKEN_HTTP" "200"

ACCESS_TOKEN=$(echo "$TOKEN_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token',''))" 2>/dev/null || echo "")
if [[ -n "$ACCESS_TOKEN" ]]; then
  total=$((total+1))
  printf "  \033[32mPASS\033[0m  Token received (length=%d)\n" "${#ACCESS_TOKEN}"
  ok=$((ok+1))
else
  total=$((total+1))
  printf "  \033[31mFAIL\033[0m  No access_token in response\n"
  fail=$((fail+1))
fi

TOKEN_BANKS=$(echo "$TOKEN_BODY" | python3 -c "
import sys,json
data=json.load(sys.stdin)
banks=data.get('banks',[])
if isinstance(banks, list):
    bics=[b if isinstance(b,str) else b.get('bic','') for b in banks]
else:
    bics=[]
print(','.join(bics))
" 2>/dev/null || echo "")
check "Token includes our BIC ($SWIFT_BIC)" "$TOKEN_BANKS" "$SWIFT_BIC"

BAD_TOKEN_HTTP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$SWIFT_URL_HOST/auth/token" \
  -d "client_id=invalid&client_secret=invalid&grant_type=client_credentials" \
  2>/dev/null || echo "000")
check "Reject invalid credentials (expect 401)" "$BAD_TOKEN_HTTP" "401"

# ── 4. Bearer auth on /swift/message ─────────────────────────────────────────
echo ""
echo "▶ 4. Bearer auth on /swift/message"
NO_AUTH_HTTP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$SWIFT_URL_HOST/swift/message" \
  -H "Content-Type: application/xml" -d "<test/>" 2>/dev/null || echo "000")
check "POST /swift/message without token (expect 401/403)" "$NO_AUTH_HTTP" "40"

BAD_AUTH_HTTP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$SWIFT_URL_HOST/swift/message" \
  -H "Authorization: Bearer invalid-token-00000" \
  -H "Content-Type: application/xml" -d "<test/>" 2>/dev/null || echo "000")
check "POST /swift/message with invalid token (expect 401/403)" "$BAD_AUTH_HTTP" "40"

# ── 5. Bank list / BIC directory ─────────────────────────────────────────────
echo ""
echo "▶ 5. Bank directory"
BANKS_RESP=$(curl -s "$SWIFT_URL_HOST/api/banks" 2>/dev/null || echo "[]")
HAS_BANK=$(echo "$BANKS_RESP" | python3 -c "
import sys,json
banks=json.load(sys.stdin)
if isinstance(banks, list):
    match=[b for b in banks if b.get('bic')=='$SWIFT_BIC']
elif isinstance(banks, dict):
    match=[v for k,v in banks.items() if k=='$SWIFT_BIC' or (isinstance(v,dict) and v.get('bic')=='$SWIFT_BIC')]
else:
    match=[]
print('found' if match else 'not_found')
" 2>/dev/null || echo "error")
check "Our BIC ($SWIFT_BIC) in middleware bank directory" "$HAS_BANK" "found"

# ── 6. Bank webhook endpoint ─────────────────────────────────────────────────
echo ""
echo "▶ 6. Bank webhook endpoint (/transfers/swift/receive)"
NO_UETR_XML='<?xml version="1.0" encoding="UTF-8"?><Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"><FIToFICstmrCdtTrf><CdtTrfTxInf><PmtId><InstrId>VERIFY-TEST</InstrId></PmtId></CdtTrfTxInf></FIToFICstmrCdtTrf></Document>'

WEBHOOK_NO_SECRET=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BANK_URL/transfers/swift/receive" \
  -H "Content-Type: application/xml" \
  -d "$NO_UETR_XML" 2>/dev/null || echo "000")
check "POST /transfers/swift/receive — no secret header → 401" "$WEBHOOK_NO_SECRET" "401"

WEBHOOK_WITH_SECRET=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BANK_URL/transfers/swift/receive" \
  -H "Content-Type: application/xml" \
  -H "X-SWIFT-Webhook-Secret: ${SWIFT_WEBHOOK_SECRET}" \
  -d "$NO_UETR_XML" 2>/dev/null || echo "000")
check "POST /transfers/swift/receive — valid secret, missing UETR → 400" "$WEBHOOK_WITH_SECRET" "400"

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
