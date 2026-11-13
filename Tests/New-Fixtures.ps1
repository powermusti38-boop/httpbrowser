param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'fixtures'),
    [switch]$IncludeHlsJs
)

$ErrorActionPreference = 'Stop'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fixtureDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

# Generate synthetic video and audio; no third-party movie clips are needed.
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=640x360:rate=25' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 15 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -g 50 -c:a aac -movflags +faststart (Join-Path $fixtureDirectory 'sample.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Could not generate the MP4 fixture.' }

& $ffmpeg -hide_banner -loglevel error -y -i (Join-Path $fixtureDirectory 'sample.mp4') -c copy -f hls -hls_time 6 -hls_list_size 0 -hls_playlist_type vod -hls_segment_filename (Join-Path $fixtureDirectory 'chunk%03d.ts') (Join-Path $fixtureDirectory 'vod.m3u8')
if ($LASTEXITCODE -ne 0) { throw 'Could not generate the HLS fixture.' }

if ($IncludeHlsJs) {
    Invoke-WebRequest -Uri 'https://cdn.jsdelivr.net/npm/hls.js@1.6.13/dist/hls.min.js' -OutFile (Join-Path $fixtureDirectory 'hls.min.js')
}
Write-Output "Synthetic test fixtures generated in $fixtureDirectory"
