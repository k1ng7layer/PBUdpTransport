using System.Net;

namespace UdpTransport
{
    public readonly struct RawPacket
    {
        public readonly IPEndPoint EndPoint;
        public readonly byte[] Payload;

        public RawPacket(IPEndPoint endPoint, byte[] payload)
        {
            EndPoint = endPoint;
            Payload = payload;
        }
    }
}