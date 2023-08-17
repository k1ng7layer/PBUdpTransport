namespace PBUdpTransport.Config.Impl
{
    public class DefaultUdpConfiguration : IUdpConfiguration
    {
        public int MTU { get; set; } = 1032;
        public int MaxPacketResendCount { get; set; } = 2;
        public int ReceiveBufferSize { get; set; } = 1032;
        public ushort TransmissionWindowSize { get; set; } = 4;
    }
}