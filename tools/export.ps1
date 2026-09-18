# Aseprite 배치 익스포트: art/src/**/*.ase → art/out/**/*.png + .json
# 요구: aseprite CLI PATH 등록. 파일 규약: 레이어=방향(0,1,2,3,4), 태그=상태/애니
$src = (Resolve-Path "$PSScriptRoot/../art/src").Path
$out = "$PSScriptRoot/../art/out"
Get-ChildItem $src -Recurse -Filter *.ase | ForEach-Object {
  $rel = $_.FullName.Substring($src.Length + 1) -replace '\\','/' -replace '\.ase$',''
  New-Item -ItemType Directory -Force -Path (Split-Path "$out/$rel") | Out-Null
  aseprite -b $_.FullName `
    --split-layers --split-tags `
    --sheet "$out/$rel.png" `
    --data  "$out/$rel.json" --format json-hash `
    --filename-format '{title}_{layer}_{tag}_{frame}'
}
