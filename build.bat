@rem builds the solution and runs the tests using FAKE (F# Make tool)
@rem NOTE! If the integration tests fail, you might have to register the URL - use RegisterURL.bat (run as administrator)

@echo off
cls
"Tools\FAKE\Fake.exe" build.fsx
pause