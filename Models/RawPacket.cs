using System.Net;

namespace PBUdpTransport.Models
{
    internal readonly struct RawPacket
    {
        public readonly IPEndPoint EndPoint;
        public readonly byte[] Payload;
        public readonly int Count;

        public RawPacket(IPEndPoint endPoint, byte[] payload, int count)
        {
            EndPoint = endPoint;
            Payload = payload;
            Count = count;
        }
    }
}