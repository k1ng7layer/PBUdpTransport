namespace UdpTransport.Impl
{
    public class DefaultUdpConfiguration : IUdpConfiguration
    {
        public int MTU { get; set; }
        public int MaxPacketResendCount { get; set; }
    }
}