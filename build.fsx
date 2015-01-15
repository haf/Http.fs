// include Fake lib
#r @"packages\FAKE.3.4.0\tools\FakeLib.dll"
open Fake 

// Paths
let httpClientDir = "./HttpClient/"
let unitTestsDir = "./HttpClient.UnitTests/"
let integrationTestsDir = "./HttpClient.IntegrationTests/"
let sampleApplicationDir = "./HttpClient.SampleApplication/"

let releaseDir = "Release/"
let nuGetDir = releaseDir + "NuGet/"
let nuSpecFile = nuGetDir + "HttpClient.dll.nuspec"
let nuGetProjectDll = nuGetDir + "lib/net40/HttpClient.dll"
let nUnitToolPath = "Tools/NUnit-2.6.3/bin"

// Helper Functions
let outputFolder baseDir =
    baseDir + "bin/Debug/"

let binFolder baseDir =
    baseDir + "bin/"

let projectFolder baseDir =
    baseDir + "*.fsproj"

let BuildTarget targetName baseDirectory =
    Target targetName (fun _ ->
        !! (baseDirectory |> projectFolder)
        |> MSBuildReleaseExt (baseDirectory |> outputFolder) ["TreatWarningsAsErrors","true"] "Build"
        |> Log (targetName + "-Output: ")
    )

// Targets
Target "Clean" (fun _ ->
    CleanDirs [
        httpClientDir |> binFolder
        unitTestsDir |> binFolder
        integrationTestsDir |> binFolder
        sampleApplicationDir |> binFolder
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

// copy the distributable source files & dll into the Release folder 
Target "Copy Release Files" (fun _ ->

    CopyFiles 
        releaseDir 
        [
            httpClientDir + "HttpClient.fs"
            httpClientDir + "AsyncStreamReader.fs"
            (httpClientDir |> outputFolder) + "HttpClient.dll"
        ]
)

// note to self - call like this: 
// packages\FAKE.3.4.0\tools\fake.exe build.fsx nuget-version=1.1.0 nuget-api-key=(my api key) nuget-release-notes="latest release"
Target "Upload to NuGet" (fun _ ->
    // Copy the dll into the right place
    CopyFiles 
        (releaseDir + "NuGet/lib/net40")
        [(httpClientDir |> outputFolder) + "HttpClient.dll"]

    trace <| "buildParam nuget-version: " + getBuildParam "nuget-version"
    trace <| "buildParam nuget-api-key: " + getBuildParam "nuget-api-key"

    // Create and upload package
    NuGet (fun n -> 
        {n with          
            OutputPath = nuGetDir
            WorkingDir = nuGetDir
            Project = "Http.fs"
            Version = getBuildParam "nuget-version"
            AccessKey = getBuildParam "nuget-api-key"
            ReleaseNotes = getBuildParam "nuget-release-notes"
            PublishTrials = 3
            Publish = true }) 
        nuSpecFile
)

Target "All" (fun _ ->
    // A dummy target so I can build everything easily
    ()
)

// Dependencies
"Clean" 
    ==> "BuildClient"
    ==> "BuildUnitTests" <=> "BuildIntegrationTests" <=> "BuildSampleApplication"
    ==> "Run Unit Tests" <=> "Run Integration Tests"
    ==> "Copy Release Files"
    =?> ("Upload to NuGet", // run this if all params secified
        hasBuildParam "nuget-version" && 
        hasBuildParam "nuget-api-key" && 
        hasBuildParam "nuget-release-notes")
    ==> "All"

// start build
RunTargetOrDefault "All"
