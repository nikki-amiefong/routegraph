using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Routegraph;

internal readonly record struct TraceRouteReply(IPAddress Address, bool ReachedDestination);

internal static class TraceRouteProbe
{
    private const int PayloadLength = 32;
    private const int IcmpHeaderLength = 8;

    public static async Task<TraceRouteReply?> SendAsync(
        IPAddress destination,
        int hopLimit,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
            return await SendMacOsAsync(destination, hopLimit, timeout, cancellationToken);

        using var ping = new Ping();
        var reply = await ping.SendPingAsync(
            destination,
            timeout,
            new byte[PayloadLength],
            new PingOptions(hopLimit, true),
            cancellationToken);

        if (reply.Address is null ||
            reply.Address.Equals(IPAddress.Any) ||
            reply.Address.Equals(IPAddress.IPv6Any) ||
            reply.Status is IPStatus.TimedOut or IPStatus.Unknown)
            return null;

        return new TraceRouteReply(reply.Address, reply.Status == IPStatus.Success);
    }

    private static async Task<TraceRouteReply?> SendMacOsAsync(
        IPAddress destination,
        int hopLimit,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var isIpv4 = destination.AddressFamily == AddressFamily.InterNetwork;
        var protocol = isIpv4 ? ProtocolType.Icmp : ProtocolType.IcmpV6;
        using var socket = new Socket(destination.AddressFamily, SocketType.Dgram, protocol);
        socket.Ttl = (short)hopLimit;

        if (isIpv4)
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

        var identifier = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var sequence = (ushort)hopLimit;
        var packet = isIpv4
            ? CreateIpv4Packet(destination, hopLimit, identifier, sequence)
            : CreateIpv6Packet(identifier, sequence);

        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(timeout);

        var responseBuffer = new byte[2 * (60 + IcmpHeaderLength) + PayloadLength];
        EndPoint remoteEndPoint = isIpv4
            ? new IPEndPoint(IPAddress.Any, 0)
            : new IPEndPoint(IPAddress.IPv6Any, 0);

        try
        {
            await socket.SendToAsync(
                packet,
                SocketFlags.None,
                new IPEndPoint(destination, 0),
                probeCancellation.Token);

            while (true)
            {
                var response = await socket.ReceiveFromAsync(
                    responseBuffer,
                    SocketFlags.None,
                    remoteEndPoint,
                    probeCancellation.Token);

                if (response.RemoteEndPoint is IPEndPoint remote &&
                    TryParseResponse(
                        responseBuffer,
                        response.ReceivedBytes,
                        remote.Address,
                        isIpv4,
                        identifier,
                        sequence,
                        out var parsedReply))
                    return parsedReply;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static byte[] CreateIpv4Packet(
        IPAddress destination,
        int hopLimit,
        ushort identifier,
        ushort sequence)
    {
        var packet = new byte[20 + IcmpHeaderLength + PayloadLength];
        packet[0] = 0x45;
        BitConverter.TryWriteBytes(packet.AsSpan(2, sizeof(ushort)), (ushort)packet.Length);
        packet[8] = (byte)hopLimit;
        packet[9] = (byte)ProtocolType.Icmp;
        destination.GetAddressBytes().CopyTo(packet, 16);

        var icmpOffset = 20;
        packet[icmpOffset] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(icmpOffset + 4), identifier);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(icmpOffset + 6), sequence);
        var checksum = ComputeChecksum(packet.AsSpan(icmpOffset));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(icmpOffset + 2), checksum);
        return packet;
    }

    private static byte[] CreateIpv6Packet(ushort identifier, ushort sequence)
    {
        var packet = new byte[IcmpHeaderLength + PayloadLength];
        packet[0] = 128;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), identifier);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), sequence);
        return packet;
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> packet)
    {
        uint sum = 0;
        for (var index = 0; index < packet.Length; index += 2)
        {
            var lowByte = index + 1 < packet.Length ? packet[index + 1] : 0;
            sum += (uint)((packet[index] << 8) | lowByte);
        }

        while ((sum >> 16) != 0)
            sum = (sum & 0xffff) + (sum >> 16);

        return (ushort)~sum;
    }

    private static bool TryParseResponse(
        byte[] packet,
        int packetLength,
        IPAddress source,
        bool isIpv4,
        ushort identifier,
        ushort sequence,
        out TraceRouteReply reply)
    {
        reply = default;
        if (isIpv4)
        {
            if (packetLength < 20 || source.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var outerHeaderLength = (packet[0] & 0x0f) * 4;
            if (outerHeaderLength < 20 || packetLength < outerHeaderLength + IcmpHeaderLength)
                return false;

            var icmpOffset = outerHeaderLength;
            var type = packet[icmpOffset];
            if (type == 0)
            {
                if (!MatchesIdentifierAndSequence(packet, icmpOffset + 4, packetLength, identifier, sequence))
                    return false;

                reply = new TraceRouteReply(source, ReachedDestination: true);
                return true;
            }

            if (type is not (3 or 11 or 12))
                return false;

            var originalIpOffset = icmpOffset + IcmpHeaderLength;
            if (packetLength < originalIpOffset + 20)
                return false;

            var originalHeaderLength = (packet[originalIpOffset] & 0x0f) * 4;
            var originalIcmpOffset = originalIpOffset + originalHeaderLength;
            if (originalHeaderLength < 20 ||
                packet[originalIpOffset + 9] != (byte)ProtocolType.Icmp ||
                packetLength < originalIcmpOffset + IcmpHeaderLength ||
                packet[originalIcmpOffset] != 8 ||
                !MatchesIdentifierAndSequence(packet, originalIcmpOffset + 4, packetLength, identifier, sequence))
                return false;

            reply = new TraceRouteReply(source, ReachedDestination: false);
            return true;
        }

        if (source.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        var ipv6HeaderOffset = packetLength >= 40 && (packet[0] >> 4) == 6 ? 40 : 0;
        var icmpv6Offset = ipv6HeaderOffset;
        if (packetLength < icmpv6Offset + IcmpHeaderLength)
            return false;

        var ipv6Type = packet[icmpv6Offset];
        if (ipv6Type == 129)
        {
            if (!MatchesIdentifierAndSequence(packet, icmpv6Offset + 4, packetLength, identifier, sequence))
                return false;

            reply = new TraceRouteReply(source, ReachedDestination: true);
            return true;
        }

        if (ipv6Type is not (1 or 2 or 3 or 4))
            return false;

        var originalIpv6Offset = icmpv6Offset + IcmpHeaderLength;
        var originalIcmpv6Offset = originalIpv6Offset + 40;
        if (packetLength < originalIcmpv6Offset + IcmpHeaderLength ||
            packet[originalIcmpv6Offset] != 128 ||
            !MatchesIdentifierAndSequence(packet, originalIcmpv6Offset + 4, packetLength, identifier, sequence))
            return false;

        reply = new TraceRouteReply(source, ReachedDestination: false);
        return true;
    }

    private static bool MatchesIdentifierAndSequence(
        byte[] packet,
        int identifierOffset,
        int packetLength,
        ushort identifier,
        ushort sequence) =>
        identifierOffset >= 0 &&
        identifierOffset + 4 <= packetLength &&
        BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(identifierOffset)) == identifier &&
        BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(identifierOffset + 2)) == sequence;
}
