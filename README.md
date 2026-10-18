# Routegraph

Routegraph is a .NET 10 macOS desktop network monitor built with Avalonia, supporting Apple Silicon and Intel Macs.

## Run

Install the .NET 10 SDK, then from this directory run:

```sh
dotnet run
```

Enter a hostname or IPv4/IPv6 address, choose a route-probe mode and IP version, then select **Start tracing**. ICMP route probes are the default; UDP is also available. The IP version can be restricted to IPv4 or IPv6, or set to **IPv6 if possible** (the default), which falls back to IPv4 if the target has no IPv6 address. Routegraph displays the resolved target IP when tracing begins, streams hop discovery from macOS `traceroute` or `traceroute6`, and starts an independent ICMP ping loop for every responding hop. Drag the dividers in the results header to resize columns; widths stay aligned across hop rows. Missing hop numbers are shown as gaps in the results. Reverse DNS lookups run asynchronously; if the final hop matches the target IP, its hostname is inferred from the entered target and shown in italics.

Each hop's route RTT graph uses a shared scale: the whisker shows the observed minimum-to-maximum latency, and the dot marks the average. RTT is measured from your computer to that hop, so the shared scale makes the route's increasing end-to-end latency easy to compare without incorrectly adding hop RTTs together.

Hops that do not respond to traceroute or ICMP will not have an address to monitor. Some networks block traceroute or ping traffic, so missing hops and packet loss do not necessarily indicate a fault in the local connection.

## Publish for macOS

Publish a framework-dependent app for either Mac architecture:

```sh
dotnet publish -c Release -r osx-arm64 --self-contained false
dotnet publish -c Release -r osx-x64 --self-contained false
```

Route discovery uses the macOS `traceroute`/`traceroute6` commands. Route-probe mode affects route discovery; continuous per-hop latency monitoring uses ICMP pings.
