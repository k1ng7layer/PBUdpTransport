namespace PBUdpTransport.Config.Impl
{
    public class DefaultUdpConfiguration : IUdpConfiguration
    {
        public int MTU { get; set; }
        public int MaxPacketResendCount { get; set; }
        public int ReceiveBufferSize { get; set; }
    }
}