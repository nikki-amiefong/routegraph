# Routegraph

Routegraph is a cross-platform .NET 10 desktop network monitor built with Avalonia.

## Run

Install the .NET 10 SDK, then from this directory run:

```sh
dotnet run
```

Enter a hostname or IPv4/IPv6 address, choose an IP version, then select **Start tracing**. The IP version can be restricted to IPv4 or IPv6, or set to **IPv6 if possible** (the default), which falls back to IPv4 if the target has no IPv6 address. Routegraph displays the resolved target IP when tracing begins, discovers hops with managed .NET ICMP probes, and starts an independent ICMP ping loop for every responding hop. Route discovery stops when the target responds or after 30 hops. Drag the dividers in the results header to resize columns; widths stay aligned across hop rows. Missing hop numbers are shown as gaps in the results. Reverse DNS lookups run asynchronously; if the final hop matches the target IP, its hostname is inferred from the entered target and shown in italics.

Each hop's route RTT graph uses a shared scale: the whisker shows the observed minimum-to-maximum latency, and the dot marks the average. RTT is measured from your computer to that hop, so the shared scale makes the route's increasing end-to-end latency easy to compare without incorrectly adding hop RTTs together.

Hops that do not respond to route discovery or ICMP monitoring will not have an address to monitor. Some networks block ICMP traffic, so missing hops and packet loss do not necessarily indicate a fault in the local connection.

## Publish for macOS

Publish a framework-dependent app for either Mac architecture:

```sh
dotnet publish -c Release -r osx-arm64 --self-contained false
dotnet publish -c Release -r osx-x64 --self-contained false
```

Route discovery and continuous per-hop latency monitoring use .NET ICMP ping APIs; no system `traceroute` executable is required.
