@rem builds the solution and runs the tests using FAKE (F# Make tool)
@rem NOTE! If you get asked if Command can make a change, it's because Nancy has to register the URL, which needs administrator privileges

@echo off

if not exist Tools\paket.exe (
  Tools\paket.bootstrapper.exe
)

Tools\paket.exe restore

set encoding=utf-8
packages\FAKE\tools\FAKE.exe build.fsx %*
