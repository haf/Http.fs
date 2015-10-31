namespace System
open System.Reflection
open System.Runtime.InteropServices

[<assembly: AssemblyTitleAttribute("HttpFs")>]
[<assembly: AssemblyDescriptionAttribute("An HTTP client for F#")>]
[<assembly: GuidAttribute("4ead3524-8220-4f0b-b77d-edd088597fcf")>]
[<assembly: AssemblyProductAttribute("Http.fs")>]
[<assembly: AssemblyVersionAttribute("2.0.0")>]
[<assembly: AssemblyFileVersionAttribute("2.0.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "2.0.0"
