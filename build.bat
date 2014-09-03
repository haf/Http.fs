@rem builds the solution and runs the tests using FAKE (F# Make tool)
@rem NOTE! If you get asked if Command can make a change, it's because Nancy has to register the URL, which needs administrator privileges

@echo off
cls
"packages\FAKE.3.4.0\tools\Fake.exe" build.fsx
