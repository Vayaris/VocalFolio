param([switch]$Capture,[switch]$Playback)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $testWork = Join-Path $PSScriptRoot 'test-work'
    New-Item -ItemType Directory -Force $testWork | Out-Null
    # Only public sample audio is downloaded. No user audio is uploaded.
    Invoke-WebRequest 'https://raw.githubusercontent.com/NVIDIA/NeMo-Speech.cpp/main/test_files/asr/wav/test/jfk.wav' -OutFile (Join-Path $testWork 'test.wav')
    Invoke-WebRequest 'https://raw.githubusercontent.com/NVIDIA/NeMo-Speech.cpp/main/test_files/diar/ami_en2002d_2132.wav' -OutFile (Join-Path $testWork 'multi.wav')
    $row = (Invoke-RestMethod 'https://datasets-server.huggingface.co/rows?dataset=PolyAI%2Fminds14&config=fr-FR&split=train&offset=0&length=1').rows[0].row
    Invoke-WebRequest $row.audio[0].src -OutFile (Join-Path $testWork 'french.wav')
    dotnet build tests/Checks.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Échec de compilation des contrôles.' }
    $engine = @(& dotnet tests/bin/Debug/net10.0-windows/Checks.dll --bootstrap (Join-Path $testWork 'v2-profile'))[-1]
    if ($LASTEXITCODE -ne 0) { throw 'Échec de préparation du moteur officiel.' }
    $modelFolder = Join-Path $testWork 'test-models'
    New-Item -ItemType Directory -Force $modelFolder | Out-Null
    foreach ($model in (Get-Content (Join-Path $PSScriptRoot 'model-manifest.json') -Raw | ConvertFrom-Json)) {
        $target = Join-Path $modelFolder $model.path
        $cached = Join-Path (Join-Path $env:LOCALAPPDATA 'ReunionLocale/models') $model.path
        if (-not (Test-Path -LiteralPath $target)) {
            if ((Test-Path -LiteralPath $cached) -and (Get-FileHash -LiteralPath $cached).Hash.ToLower() -eq $model.sha256) {
                Copy-Item -LiteralPath $cached -Destination $target
            } else {
                Invoke-WebRequest "https://huggingface.co/$($model.repo)/resolve/$($model.revision)/$($model.path)" -OutFile $target
            }
        }
        if ((Get-FileHash -LiteralPath $target).Hash.ToLower() -ne $model.sha256) { throw "Empreinte invalide : $($model.path)" }
    }
    $asr = Join-Path $modelFolder 'nemotron-3.5-asr-streaming-0.6b.q8_0.gguf'
    $diar = Join-Path $modelFolder 'diar_streaming_sortformer_4spk-v2.q8_0.gguf'
    & $engine transcribe (Join-Path $testWork 'test.wav') --model $asr --diar-model $diar --device cpu --format json --output (Join-Path $testWork 'real-result.json') --force --stream
    if ($LASTEXITCODE -ne 0) { throw 'Échec de la transcription de référence.' }
    & $engine transcribe (Join-Path $testWork 'multi.wav') --model $asr --diar-model $diar --device cpu --format json --output (Join-Path $testWork 'multi-result.json') --force --stream
    if ($LASTEXITCODE -ne 0) { throw 'Échec de la transcription multi-intervenants.' }
    & $engine transcribe (Join-Path $testWork 'french.wav') --model $asr --language fr --device cpu --format json --output (Join-Path $testWork 'french-result.json') --force --stream
    if ($LASTEXITCODE -ne 0) { throw 'Échec de la transcription française.' }
    $arguments = @('tests/bin/Debug/net10.0-windows/Checks.dll', $testWork, '--v2')
    if ($Capture) { $arguments += '--capture' } # Records a few seconds from your default devices locally.
    if ($Playback) { $arguments += '--playback' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Un contrôle a échoué.' }
} finally { Pop-Location }
