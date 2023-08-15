namespace PBUdpTransport.Config
{
    public interface IUdpConfiguration
    {
        int MTU { get; set; }
        int MaxPacketResendCount { get; set; }
        int ReceiveBufferSize { get; set; }
    }
}