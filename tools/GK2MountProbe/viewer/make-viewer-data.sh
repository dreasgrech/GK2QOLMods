#!/usr/bin/env bash
# Run inside the probe's dump folder (BepInEx/GK2-sprites) after a probe run; produces the two data files the
# viewer needs, then copy keeper-animations.html next to them and open it in a browser.
set -e
awk -F, 'NR>1 && ($1 ~ /^90[0-9][0-9]_/ || $1 ~ /^0301_/) && $1 !~ /\(Clone\)/ && $1 !~ /_palette$/ { printf "%s[\"%s\",\"%s\",%s,%s,%s,%s,%s,%s,%s,%s]", (n++?",\n":""), $1, ($2 ~ /MainCharacter/ ? "M" : ($2 ~ /sactx-1/ ? "C1" : "C0")), $3,$4,$5,$6,$10,$11,$12,$13 } BEGIN{print "window.SPRITES = ["} END{print "\n];"}' sprites.csv > sprites-data.js
{
  printf 'window.ATLAS_DATA = {\n'
  for k in "M:sactx-0-2048x2048-Uncompressed-MainCharacter-8554cb72.png" "C0:sactx-0-2048x2048-Uncompressed-Characters-a31f6aed.png" "C1:sactx-1-2048x2048-Uncompressed-Characters-a31f6aed.png"; do
    printf '  %s: "data:image/png;base64,%s",\n' "${k%%:*}" "$(base64 -w0 "${k#*:}")"
  done
  printf '};\nwindow.PALETTE_DATA = {\n'
  for k in body arms hair beard; do printf '  %s: "data:image/png;base64,%s",\n' "$k" "$(base64 -w0 "palette_keeper_${k}_unnamed.png")"; done
  printf '};\n'
} > atlases-data.js
echo "wrote sprites-data.js and atlases-data.js"
