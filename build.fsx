#r @"packages/build/FAKE/tools/FakeLib.dll"

open System
open Fake

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

type ProjectInfo =
    { Release : ReleaseNotesHelper.ReleaseNotes
      Description : string
      Tags : string
      Authors : string
      Owners : string }

let httpFsCoreInfo =
    { Release = IO.File.ReadAllLines "HttpFs.Core/RELEASE_NOTES.md" |> ReleaseNotesHelper.parseReleaseNotes
      Description = "Core types used by HttpFs runners"
      Tags = "http fsharp client functional"
      Authors = "Henrik Feldt"
      Owners = "Henrik Feldt" }

let httpFsNetInfo =
    { Release = IO.File.ReadAllLines "HttpFs.Net/RELEASE_NOTES.md" |> ReleaseNotesHelper.parseReleaseNotes
      Description = "A simple, functional HTTP client library for F#"
      Tags = "http fsharp client functional"
      Authors = "Henrik Feldt"
      Owners = "Henrik Feldt" }

let configuration = environVarOrDefault "Configuration" "Release"
let projectUrl = "https://github.com/haf/Http.fs"
let iconUrl = "https://raw.githubusercontent.com/haf/Http.fs/releases/v4.x/docs/files/img/logo.png"
let licenceUrl = "https://github.com/haf/Http.fs/blob/master/Licence/License.md"
let copyright = sprintf "Copyright \169 %i" DateTime.Now.Year


Target "Clean" (fun _ -> !!"./**/bin/" ++ "./**/obj/" |> CleanDirs)

open AssemblyInfoFile
Target "AssemblyInfo" (fun _ ->

    [ "HttpFs.Core", httpFsCoreInfo
      "HttpFs.Net", httpFsNetInfo
      "HttpFs.UnitTests", httpFsCoreInfo
      "HttpFs.IntegrationTests", httpFsCoreInfo
    ]
    |> List.iter (fun (product, projectInfo) ->
        [ Attribute.Title product
          Attribute.Product product
          Attribute.Copyright copyright
          Attribute.Description projectInfo.Description
          Attribute.Version projectInfo.Release.AssemblyVersion
          Attribute.FileVersion projectInfo.Release.AssemblyVersion
        ] |> CreateFSharpAssemblyInfo (product+"/AssemblyInfo.fs")
    )
)

Target "PaketFiles" (fun _ ->
    FileHelper.ReplaceInFiles ["namespace Logary.Facade","namespace HttpFs.Logging"]
        ["paket-files/logary/logary/src/Logary.Facade/Facade.fs"]
)

Target "ProjectVersion" (fun _ ->
    [
        "HttpFs.Core/HttpFs.Core.fsproj", httpFsCoreInfo.Release.NugetVersion
        "HttpFs.Net/HttpFs.Net.fsproj", httpFsNetInfo.Release.NugetVersion
    ]
    |> List.iter (fun (file, version) ->
        XMLHelper.XmlPoke file "Project/PropertyGroup/Version/text()" version)
)

let build project framework =
    DotNetCli.Build (fun p ->
    { p with
        Configuration = configuration
        Framework = framework
        Project = project
    })

Target "BuildTest" (fun _ ->
    build "HttpFs.UnitTests/HttpFs.UnitTests.fsproj" "netcoreapp2.0"
    build "HttpFs.UnitTests/HttpFs.UnitTests.fsproj" "net461"
    build "HttpFs.IntegrationTests/HttpFs.IntegrationTests.fsproj" "netcoreapp2.0"
    build "HttpFs.IntegrationTests/HttpFs.IntegrationTests.fsproj" "net461"
)

Target "RunTest" (fun _ ->
    DotNetCli.RunCommand id ("HttpFs.UnitTests/bin/"+configuration+"/netcoreapp2.0/HttpFs.UnitTests.dll --summary --sequenced")
    //Shell.Exec ("HttpFs.UnitTests/bin/"+configuration+"/net461/HttpFs.UnitTests.exe","--summary --sequenced")
    //|> fun r -> if r<>0 then failwith "HttpFs.UnitTests.exe failed"

    DotNetCli.RunCommand id ("HttpFs.IntegrationTests/bin/"+configuration+"/netcoreapp2.0/HttpFs.IntegrationTests.dll --summary --sequenced")
    //Shell.Exec ("HttpFs.IntegrationTests/bin/"+configuration+"/net461/HttpFs.IntegrationTests.exe","--summary --sequenced")
    //|> fun r -> if r<>0 then failwith "HttpFs.IntegrationTests.exe failed"
)

Target "Pack" (fun _ ->
    let packParameters name =
        [
            "--no-build"
            "--no-restore"
            sprintf "/p:Title=\"%s\"" name
            "/p:PackageVersion=" + httpFsCoreInfo.Release.NugetVersion
            sprintf "/p:Authors=\"%s\"" httpFsCoreInfo.Authors
            sprintf "/p:Owners=\"%s\"" httpFsCoreInfo.Owners
            "/p:PackageRequireLicenseAcceptance=false"
            sprintf "/p:Description=\"%s\"" (httpFsCoreInfo.Description.Replace(",",""))
            sprintf "/p:PackageReleaseNotes=\"%O\"" ((toLines httpFsCoreInfo.Release.Notes).Replace(",",""))
            sprintf "/p:Copyright=\"%s\"" copyright
            sprintf "/p:PackageTags=\"%s\"" httpFsCoreInfo.Tags
            sprintf "/p:PackageProjectUrl=\"%s\"" projectUrl
            sprintf "/p:PackageIconUrl=\"%s\"" iconUrl
            sprintf "/p:PackageLicenseUrl=\"%s\"" licenceUrl
        ] |> String.concat " "

    DotNetCli.RunCommand id
        ("pack HttpFs.Core/HttpFs.Core.fsproj -c "+configuration + " -o ../bin " + (packParameters "HttpFs.Core"))
)

Target "Push" (fun _ -> Paket.Push (fun p -> { p with WorkingDir = "bin" }))

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
Target "Release" (fun _ ->
    let gitOwner = "haf"
    let gitName = "expecto"
    let gitOwnerName = gitOwner + "/" + gitName
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.tryFind (fun s -> s.EndsWith "(push)" && s.Contains gitOwnerName)
        |> function None -> ("ssh://github.com/"+gitOwnerName) | Some s -> s.Split().[0]

    Git.Staging.StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" httpFsCoreInfo.Release.NugetVersion)
    Git.Branches.pushBranch "" remote (Git.Information.getBranchName "")

    Git.Branches.tag "" httpFsCoreInfo.Release.NugetVersion
    Git.Branches.pushTag "" remote httpFsCoreInfo.Release.NugetVersion

    let user = getUserInput "Github Username: "
    let pw = getUserPassword "Github Password: "

    Octokit.createClient user pw
    |> Octokit.createDraft gitOwner gitName httpFsCoreInfo.Release.NugetVersion
        (Option.isSome httpFsCoreInfo.Release.SemVer.PreRelease) httpFsCoreInfo.Release.Notes
    |> Octokit.releaseDraft
    |> Async.RunSynchronously
)

Target "All" ignore

"Clean"
==> "AssemblyInfo"
==> "PaketFiles"
==> "ProjectVersion"
==> "BuildTest"
==> "RunTest"
==> "Pack"
==> "All"
==> "Push"
==> "Release"

RunTargetOrDefault "All"
