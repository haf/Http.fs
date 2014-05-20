// To try stuff out.  Ensure latest files are in Release folder (e.g. with Build.bat)

#r "HttpClient.dll"
open HttpClient  

createRequest Get "http://www.google.com" 
|> getResponseBody