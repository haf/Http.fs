#!/usr/bin/env sh

mono Tools/paket.bootstrapper.exe
mono Tools/paket.exe install
./build.fsx
