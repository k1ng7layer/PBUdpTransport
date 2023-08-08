using System.Net;

namespace UdpTransport
{
    public class TransportMessage
    {
        public TransportMessage(
            byte[] payload, 
            IPEndPoint remoteEndpoint)
        {
            Payload = payload;
            RemoteEndpoint = remoteEndpoint;
        }
        
        public byte[] Payload { get; }
        public IPEndPoint RemoteEndpoint { get; }
    }
}