param(
    [string]$ScreenshotRoot = (Join-Path $PSScriptRoot '../build/ui-review/after')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ScreenshotRoot).Path
$files = @(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.png' |
    Where-Object { $_.Directory.Name -in @('light', 'dark') } | Sort-Object FullName)
if ($files.Count -eq 0) { throw "No screenshots found in $root" }
$cards = foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    $label = [Net.WebUtility]::HtmlEncode($relative)
    $url = ($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    "<a class='card' href='$url' data-name='$label'><img loading='lazy' src='$url' alt='$label'><span>$label</span></a>"
}
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Toucan Windows UI review</title>
<style>
body{margin:0;background:#101416;color:#edf2f3;font:15px system-ui,sans-serif}
header{position:sticky;top:0;background:#182125;padding:18px 24px;z-index:1;border-bottom:1px solid #35464c}
h1{font-size:22px;margin:0 0 8px}p{margin:6px 0;color:#bbc8cd}
input,select{font:inherit;padding:9px 12px;margin:12px 8px 0 0;border:1px solid #52656e;border-radius:6px;background:#101416;color:inherit}
main{padding:24px;display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:18px}
.card{display:flex;flex-direction:column;background:#202b30;border:1px solid #35464c;border-radius:8px;overflow:hidden;color:inherit;text-decoration:none}
.card img{width:100%;height:260px;object-fit:contain;background:#313c42}.card span{padding:12px;font-size:13px;overflow-wrap:anywhere}
.card:hover{border-color:#00becb}.card[hidden]{display:none}
</style><header><h1>Toucan Windows UI review</h1>
<p>$($files.Count) captures · Light and dark themes · Click a capture to inspect it at full resolution.</p>
<p>These captures use Avalonia Headless with Skia on Windows. Native title bars, OS pickers and compositor behavior require a separate desktop check.</p>
<input id="search" aria-label="Filter screenshots" placeholder="Filter: options, import, minimum…">
<select id="theme" aria-label="Theme"><option value="">Both themes</option><option>light</option><option>dark</option></select>
<span id="count"></span></header><main>$($cards -join "`n")</main>
<script>
const search=document.querySelector('#search'),theme=document.querySelector('#theme'),cards=[...document.querySelectorAll('.card')];
function filter(){let count=0;for(const card of cards){const name=card.dataset.name.toLowerCase();card.hidden=!(name.includes(search.value.toLowerCase())&&(!theme.value||name.startsWith(theme.value+'/')));if(!card.hidden)count++;}document.querySelector('#count').textContent=count+' visible';}
search.addEventListener('input',filter);theme.addEventListener('change',filter);filter();
</script></html>
"@
$destination = Join-Path $root 'index.html'
Set-Content -LiteralPath $destination -Value $html -Encoding utf8
Write-Output $destination
