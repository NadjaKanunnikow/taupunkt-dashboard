# Отправляет 10 тестовых пар измерений в локальный бэкенд
# Запусти: .\send-test-data.ps1

$API = "http://localhost:5000"
$KEY = "dev-key"

function DewPoint($temp, $hum) {
    $a = 17.62; $b = 243.12
    $g = ($a * $temp) / ($b + $temp) + [Math]::Log($hum / 100.0)
    return [Math]::Round(($b * $g) / ($a - $g), 2)
}

function Send($loc, $temp, $hum, $dp, $diff, $fan, $time) {
    $body = @{
        deviceId                    = "test-pi"
        measurementLocation         = $loc
        temperature                 = $temp
        humidity                    = $hum
        measuredAt                  = $time
        dewPointC                   = $dp
        dewPointDifferenceC         = $diff
        controlDewPointDifferenceC  = $diff
        fanOnThresholdC             = 4.0
        fanOffThresholdC            = 3.0
        fanOn                       = $fan
        controlMode                 = "automatic"
        displayTimeSource           = "raspberry-pi"
    } | ConvertTo-Json

    try {
        $r = Invoke-WebRequest -Uri "$API/api/measurements" -Method POST `
            -Headers @{ "X-API-Key" = $KEY; "Content-Type" = "application/json" } `
            -Body $body -UseBasicParsing
        return $r.StatusCode
    } catch { return $_.Exception.Response.StatusCode.value__ }
}

Write-Host "Отправляю тестовые данные..." -ForegroundColor Cyan

for ($i = 0; $i -lt 10; $i++) {
    $minsAgo = 55 - ($i * 6)
    $time = (Get-Date).ToUniversalTime().AddMinutes(-$minsAgo).ToString("yyyy-MM-ddTHH:mm:ssZ")

    $tempIn  = [Math]::Round(21.5 + $i * 0.1 + ($i % 3) * 0.2, 1)
    $humIn   = [Math]::Round(58 + $i * 0.5, 1)
    $dpIn    = DewPoint $tempIn $humIn

    $tempOut = [Math]::Round(7.5 + $i * 0.05, 1)
    $humOut  = [Math]::Round(79 + $i * 0.3, 1)
    $dpOut   = DewPoint $tempOut $humOut

    $diff = [Math]::Round($dpIn - $dpOut, 2)
    $fan  = $diff -ge 4.0

    $cIn  = Send "inside"  $tempIn  $humIn  $dpIn  $diff $fan $time
    $cOut = Send "outside" $tempOut $humOut $dpOut $diff $fan $time

    Write-Host "[$i] $time  innen=$($tempIn)°C  außen=$($tempOut)°C  diff=$diff°C  Lüfter=$fan  → $cIn/$cOut"
}

Write-Host "`nFertig! Seite neu laden." -ForegroundColor Green
