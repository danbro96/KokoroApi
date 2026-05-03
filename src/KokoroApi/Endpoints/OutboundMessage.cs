namespace KokoroApi.Endpoints;

internal readonly record struct OutboundMessage(byte[] Bytes, bool IsBinary)
{
    public static OutboundMessage Text(byte[] b) => new(b, false);

    public static OutboundMessage Binary(byte[] b) => new(b, true);
}
