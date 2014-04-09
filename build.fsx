// include Fake lib
#r @"packages\FAKE.2.13.3\tools\FakeLib.dll"
open Fake 

// Paths

let httpClientDir = "./HttpClient/"
let unitTestsDir = "./HttpClient.UnitTests/"
let integrationTestsDir = "./HttpClient.IntegrationTests/"
let sampleApplicationDir = "./HttpClient.SampleApplication/"

let nUnitToolPath = "Tools/NUnit-2.6.3/bin"

// Helper Functions

let outputFolder baseDir =
    baseDir + "bin/Debug/"

let projectFolder baseDir =
    baseDir + "*.fsproj"

// Does a standard project build using MSBuild and outputting to /bin/debug
let BuildTarget targetName baseDirectory =
    Target targetName (fun _ ->
        !! (baseDirectory |> projectFolder)
        |> MSBuildRelease (baseDirectory |> outputFolder) "Build"
        |> Log (targetName + "-Output: ")
    )

// Targets

Target "Clean" (fun _ ->
    CleanDirs [
        httpClientDir |> outputFolder
        unitTestsDir |> outputFolder
        integrationTestsDir |> outputFolder
        sampleApplicationDir |> outputFolder
    ]
)

BuildTarget "BuildClient" httpClientDir

BuildTarget "BuildUnitTests" unitTestsDir

BuildTarget "BuildIntegrationTests" integrationTestsDir

BuildTarget "BuildSampleApplication" sampleApplicationDir

Target "Run Unit Tests" (fun _ ->
    let unitTestOutputFolder = unitTestsDir |> outputFolder

    !! (unitTestOutputFolder + "/*.UnitTests.dll")
    |> NUnit (fun p ->
        {p with
            ToolPath = nUnitToolPath;
            DisableShadowCopy = true;
            OutputFile = unitTestOutputFolder + "TestResults.xml"})
)

// If these fail, it might be because the test server URL isn't registered - see RegisterURL.bat
Target "Run Integration Tests" (fun _ ->
    let integrationTestOutputFolder = integrationTestsDir |> outputFolder

    !! (integrationTestOutputFolder + "/*.IntegrationTests.dll")
    |> NUnit (fun p ->
        {p with
            ToolPath = nUnitToolPath;
            DisableShadowCopy = true;
            OutputFile = integrationTestOutputFolder + "TestResults.xml"})
)

Target "Build Everything" (fun _ ->
    // A dummy target so I can build everything easily
    ()
)

// Dependencies
"Clean" ==> "BuildClient"
"BuildClient" ==> "BuildUnitTests"
"BuildClient" ==> "BuildIntegrationTests"
"BuildClient" ==> "BuildSampleApplication"
"BuildUnitTests" ==> "Run Unit Tests"
"BuildIntegrationTests" ==> "Run Integration Tests"

// Make "Build Everything" depend on all targets, so I can call that to build everything
"Clean" ==> "Build Everything"
"BuildClient" ==> "Build Everything"
"BuildUnitTests" ==> "Build Everything"
"BuildIntegrationTests" ==> "Build Everything"
"BuildSampleApplication" ==> "Build Everything"
"Run Unit Tests" ==> "Build Everything"
"Run Integration Tests" ==> "Build Everything"

// start build
RunTargetOrDefault "Build Everything"
