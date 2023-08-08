namespace UdpTransport
{
    public interface IUdpConfiguration
    {
        int MTU { get; set; }
        int MaxPacketResendCount { get; set; }
    }
}