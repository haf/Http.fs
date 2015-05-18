// This is a script used to manually test the proxy, as I haven't got an automated test
// using NancyFx working yet.

// To use it again, you may have to find another proxy, from somewhere like
// http://www.proxy4free.com/list/webproxy1.html

// Unfortunately the proxies seem unreliable, and some try to hide themselves,
// so finding one that works can take a few goes.

#load "AsyncStreamReader.fs"
#load "HttpClient.fs"

open HttpClient  

// http://www.lagado.com/proxy-test is a page whic checks if the request came via a proxy.

// If it did, you get some text like

//<p class="testresult">
//This request appears to have come via a proxy.
//</p>
//<p>The proxy host has ip address 72.252.114.174</p>

// If it didn't, you'll see

//<p class="testresult">
//This request appears NOT to have come via a proxy.
//</p>

let response = 
    createRequest Get "http://www.lagado.com/proxy-test" 
    |> withProxy { Address = "72.252.114.174"; Port = 8080; Credentials = ProxyCredentials.None }
    |> withResponseCharacterEncoding "UTF-8"
    |> getResponseBody

printfn "%s" response