# S7.Net Plus

- Package: S7netplus **0.20.0**, `lib/netstandard2.0/S7.Net.dll`
- Source: https://github.com/S7NetPlus/s7netplus
- Source commit: `f1ae0ea084e712b59e414de6aaee7d196244a239`
- Package: https://api.nuget.org/v3-flatcontainer/s7netplus/0.20.0/s7netplus.0.20.0.nupkg
- DLL SHA-256: `3692B843D5F7CE331786BD2F5A085006C396414F7B85FFAD0C14E17EC4B99258`
- License: MIT, see `License.txt`.

Unity 2022.3's .NET Standard 2.1 compatibility assemblies supply System.Memory;
do not add a duplicate System.Memory DLL. The runtime uses explicit S7 bit writes,
not byte read/modify/write operations. Network access starts only after the user
chooses Connect in the PLC properties panel.
