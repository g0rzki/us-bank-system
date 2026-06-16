#!/usr/bin/env pwsh
# Pełny test flow: register → login → account → card → topup → payment
# Użycie: .\test-flow.ps1

$BASE = "http://localhost:5100"
$POS  = "http://localhost:8072"

$EMAIL    = "testuser_$(Get-Random)@example.com"
$PASSWORD = "Test1234!"

function Invoke-Api {
    param($Method, $Url, $Body, $Token)
    $headers = @{ "Content-Type" = "application/json" }
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $json = if ($Body) { $Body | ConvertTo-Json -Compress } else { $null }
    $resp = Invoke-WebRequest -Method $Method -Uri $Url -Headers $headers -Body $json -UseBasicParsing -ErrorAction Stop
    return $resp.Content | ConvertFrom-Json
}

Write-Host "`n=== 1. Register ===" -ForegroundColor Cyan
$reg = Invoke-Api POST "$BASE/auth/register" @{ email = $EMAIL; password = $PASSWORD; firstName = "Test"; lastName = "User" }
Write-Host "OK — userId: $($reg.userId)" -ForegroundColor Green

Write-Host "`n=== 2. Login ===" -ForegroundColor Cyan
$login = Invoke-Api POST "$BASE/auth/login" @{ email = $EMAIL; password = $PASSWORD }
$JWT = $login.token
Write-Host "OK — token: $($JWT.Substring(0,30))..." -ForegroundColor Green

Write-Host "`n=== 3. Create checking account ===" -ForegroundColor Cyan
$account = Invoke-Api POST "$BASE/accounts" @{ type = "checking"; currency = "USD" } -Token $JWT
$ACCOUNT_ID = $account.id
Write-Host "OK — accountId: $ACCOUNT_ID" -ForegroundColor Green

Write-Host "`n=== 4. Register debit card ===" -ForegroundColor Cyan
$card = Invoke-Api POST "$BASE/accounts/$ACCOUNT_ID/cards" @{ type = "debit" } -Token $JWT
$CARD_ID    = $card.id
$CARD_TOKEN = $card.externalCardToken
Write-Host "OK — cardId: $CARD_ID" -ForegroundColor Green
Write-Host "     externalCardToken: $CARD_TOKEN" -ForegroundColor Yellow

Write-Host "`n=== 5. Register prepaid card ===" -ForegroundColor Cyan
$prepaid = Invoke-Api POST "$BASE/accounts/$ACCOUNT_ID/cards" @{ type = "prepaid"; dailyLimit = 200; monthlyLimit = 1000 } -Token $JWT
$PREPAID_ID    = $prepaid.id
$PREPAID_TOKEN = $prepaid.externalCardToken
Write-Host "OK — prepaidId: $PREPAID_ID" -ForegroundColor Green
Write-Host "     externalCardToken: $PREPAID_TOKEN" -ForegroundColor Yellow

Write-Host "`n=== 6. Top-up prepaid card ($50) ===" -ForegroundColor Cyan
$topup = Invoke-Api POST "$BASE/accounts/$ACCOUNT_ID/cards/$PREPAID_ID/topup" @{ amount = 50 } -Token $JWT
Write-Host "OK — card status: $($topup.status)" -ForegroundColor Green

Write-Host "`n=== 7. Sprawdź karty ===" -ForegroundColor Cyan
$cards = Invoke-Api GET "$BASE/accounts/$ACCOUNT_ID/cards" -Token $JWT
foreach ($c in $cards) {
    $pan = if ($c.maskedPan) { $c.maskedPan } else { "•••• $($c.last4)" }
    Write-Host "  [$($c.type)] $pan status=$($c.status) token=$($c.externalCardToken)" -ForegroundColor White
}

Write-Host "`n=== 8. Symulacja płatności kartą PREPAID przez POS ===" -ForegroundColor Cyan
if ($PREPAID_TOKEN) {
    $posBody = @{ cardToken = $PREPAID_TOKEN; amount = 12.50; merchantId = "SKLEP_TEST" } | ConvertTo-Json -Compress
    $posResp = Invoke-WebRequest -Method POST -Uri "$POS/api/payment" -Headers @{ "Content-Type" = "application/json" } -Body $posBody -UseBasicParsing -ErrorAction SilentlyContinue
    if ($posResp) {
        Write-Host "POS response ($($posResp.StatusCode)): $($posResp.Content)" -ForegroundColor Green
    } else {
        Write-Host "POS niedostępny lub inny endpoint — sprawdź ręcznie na $POS/pos" -ForegroundColor Yellow
    }
} else {
    Write-Host "Brak tokenu prepaid — payment-gateway niedostępny podczas rejestracji" -ForegroundColor Red
}

Write-Host "`n=== 9. Zablokuj kartę debitową ===" -ForegroundColor Cyan
$blocked = Invoke-Api PATCH "$BASE/accounts/$ACCOUNT_ID/cards/$CARD_ID/status" @{ status = "blocked" } -Token $JWT
Write-Host "OK — status: $($blocked.status), blockedAt: $($blocked.blockedAt)" -ForegroundColor Green

Write-Host "`n=== 10. Sprawdź transakcje ===" -ForegroundColor Cyan
$txs = Invoke-Api GET "$BASE/accounts/$ACCOUNT_ID/transactions" -Token $JWT
Write-Host "Transakcji: $($txs.total)" -ForegroundColor White
foreach ($tx in $txs.items | Select-Object -First 5) {
    Write-Host "  [$($tx.type)] $($tx.amount) — $($tx.description)" -ForegroundColor White
}

Write-Host "`n=== DONE ===" -ForegroundColor Cyan
Write-Host "Debit  token: $CARD_TOKEN"
Write-Host "Prepaid token: $PREPAID_TOKEN"
Write-Host "Wklej token na: $POS/pos" -ForegroundColor Yellow
