#!/usr/bin/env fsharpi
#I @"packages/FAKE/tools"
#r @"FakeLib.dll"
open Fake
open System.IO
open Fake.AssemblyInfoFile

// Paths
let httpClientDir = "./HttpFs/"
let unitTestsDir = "HttpFs.UnitTests/"
let integrationTestsDir = "./HttpFs.IntegrationTests/"
let sampleApplicationDir = "./HttpFs.SampleApplication/"

let releaseDir = "Release/"
let nuGetDir = releaseDir + "NuGet/"
let nuGetProjectDll = nuGetDir + "lib/net45/HttpFs.dll"
let nUnitToolPath = "packages/NUnit.Runners/tools/"

// Helper Functions
let outputFolder baseDir =
    baseDir + "bin/Debug/"

let projectFolder baseDir =
    baseDir + "*.fsproj"

let binFolder baseDir =
    baseDir + "bin/"

let assemblyInfo baseDir =
    baseDir + "AssemblyInfo.fs"

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

Target "Update Assembly Version" (fun _ ->
    CreateFSharpAssemblyInfo (httpClientDir |> assemblyInfo) [
         Attribute.Title "HttpFs"
         Attribute.Description "An HTTP client for F#"
         Attribute.Guid "4ead3524-8220-4f0b-b77d-edd088597fcf"
         Attribute.Product "Http.fs"
         Attribute.Version (getBuildParamOrDefault "nuget-version" "2.0.0")
         Attribute.FileVersion (getBuildParamOrDefault "nuget-version" "2.0.0")
    ]
)

BuildTarget "BuildClient" httpClientDir

BuildTarget "BuildUnitTests" unitTestsDir

BuildTarget "BuildIntegrationTests" integrationTestsDir

BuildTarget "BuildSampleApplication" sampleApplicationDir

Target "Run Unit Tests" (fun _ ->
    let result =
        ExecProcess (fun info ->
            info.FileName <- (unitTestsDir |> outputFolder) @@ "HttpFs.UnitTests.exe"
        ) (System.TimeSpan.FromSeconds(30.))

    match result with
    | 0 -> ()
    | _ -> failwith "Unit-tests failed."
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
            httpClientDir + "Prelude.fs"
            httpClientDir + "AsyncStreamReader.fs"
            httpClientDir + "Client.fs"
            (httpClientDir |> outputFolder) + "HttpFs.dll"
        ]
)

// note to self - call like this: 
// .\build.bat nuget-version=1.1.0 nuget-api-key=(my api key) nuget-release-notes="latest release"
Target "NuGetPackage" (fun _ ->
    // Copy the dll into the right place
    CopyFiles 
        (releaseDir + "NuGet/lib/net45")
        [(httpClientDir |> outputFolder) + "HttpFs.dll"]

    trace <| "buildParam nuget-version: " + getBuildParam "nuget-version"
    trace <| "buildParam nuget-api-key: " + getBuildParam "nuget-api-key"

    let version = getBuildParam "nuget-version"
    let nuspec = Path.Combine(nuGetDir, "Http.fs.nuspec")
    File.WriteAllText(nuspec,
                      """<?xml version="1.0" encoding="utf-8"?>
<package xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <metadata xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
    <id>@project@</id>
    <version>@build.number@</version>
    <authors>@authors@</authors>
    <owners>@authors@</owners>
    <summary>@summary@</summary>
    <licenseUrl>https://raw.githubusercontent.com/relentless/Http.fs/master/Licence/License.md</licenseUrl>
    <projectUrl>https://github.com/relentless/Http.fs</projectUrl>
    <iconUrl>https://raw.githubusercontent.com/relentless/http.fs/master/docs/files/img/logo.png</iconUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>@description@</description>
    <releaseNotes>@releaseNotes@</releaseNotes>
    <copyright>Copyright G Crofton 2014</copyright>
    <tags>http client fsharp f# request response</tags>
    @dependencies@
    @references@
  </metadata>
  @files@
</package>""")

    let description = "A gloriously functional HTTP client library for F#!"
    
    // Create and upload package
    NuGet (fun n ->
        {n with
            Authors = ["Grant Crofton"]
            Summary = description
            Description = description
            OutputPath = nuGetDir
            WorkingDir = nuGetDir
            Project = "Http.fs"
            Version = version
            AccessKey = getBuildParam "nuget-api-key"
            ReleaseNotes = getBuildParam "nuget-release-notes"
            PublishTrials = 3
            Publish = bool.Parse(getBuildParamOrDefault "nuget-publish" "false")
            ToolPath = FullName "./packages/NuGet.CommandLine/tools/NuGet.exe"
            Files =
                [ "lib\\net45\\*.dll", Some "lib\\net45", None
                  "lib\\net45\\*.mdb", Some "lib\\net45", None 
                  "lib\\net45\\*.xml", Some "lib\\net45", None ]
            Dependencies =
                [   "FSharp.Core", GetPackageVersion "./packages" "FSharp.Core" ] })
        nuspec
)

Target "All" (fun _ ->
    // A dummy target so I can build everything easily
    ()
)

// Dependencies
"Clean" 
    ==> "Update Assembly Version"
    ==> "BuildClient"
    ==> "BuildUnitTests" <=> "BuildIntegrationTests" <=> "BuildSampleApplication"
    ==> "Run Unit Tests" <=> "Run Integration Tests"
    ==> "Copy Release Files"
    =?> ("NuGetPackage", // run this if all params secified
        hasBuildParam "nuget-version")
    ==> "All"

// start build
RunTargetOrDefault "All"
