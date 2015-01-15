namespace System
open System.Reflection
open System.Runtime.InteropServices

[<assembly: AssemblyTitleAttribute("HttpClient")>]
[<assembly: AssemblyDescriptionAttribute("An HTTP client for F#")>]
[<assembly: GuidAttribute("4ead3524-8220-4f0b-b77d-edd088597fcf")>]
[<assembly: AssemblyProductAttribute("Http.fs")>]
[<assembly: AssemblyVersionAttribute("1.5.1")>]
[<assembly: AssemblyFileVersionAttribute("1.5.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.5.1"
