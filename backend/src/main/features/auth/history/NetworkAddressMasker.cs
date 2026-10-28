using System.Net;
using System.Net.Sockets;

namespace backend.main.features.auth.history;

/// <summary>
/// Reduces a source address to the network prefix the security history shows.
/// </summary>
/// <remarks>
/// An IPv4 /24 or IPv6 /48 is enough for an owner to recognise "my home network" or "somewhere
/// I have never been", without storing a full address that, kept for 90 days, would identify a
/// household. Nothing is shown for loopback or unspecified addresses, which say nothing to the
/// owner.
/// </remarks>
public static class NetworkAddressMasker
{
    public static string? Mask(IPAddress? address)
    {
        if (address is null)
            return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None))
            return null;

        var bytes = address.GetAddressBytes();
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
            case AddressFamily.InterNetworkV6:
                Array.Clear(bytes, 6, bytes.Length - 6);
                return $"{new IPAddress(bytes)}/48";
            default:
                return null;
        }
    }
}
