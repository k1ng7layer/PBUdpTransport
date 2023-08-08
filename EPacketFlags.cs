namespace UdpTransport
{
    public enum EPacketFlags : ushort
    {
        FirstPacket,
        LastPacket,
        Ack,
        RequestForPacket,
        Default
    }
}