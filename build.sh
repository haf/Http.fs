#!/usr/bin/env bash
set -e
if [[ ! -f .paket/paket.exe ]]; then
  mono Tools/paket.bootstrapper.exe
fi
mono Tools/paket.exe restore
mono packages/FAKE/tools/FAKE.exe $@ --fsiargs -d:MONO build.fsx
