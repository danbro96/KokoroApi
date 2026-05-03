using System.Buffers.Binary;

namespace KokoroApi.Streaming;

public static class PcmEncoder
{
    public const int SampleRate = 24_000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    public static byte[] FloatToInt16Bytes(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        var span = bytes.AsSpan();
        for (var i = 0; i < samples.Length; i++)
        {
            var f = samples[i];
            if (f > 1f) f = 1f;
            else if (f < -1f) f = -1f;
            var s = (short) Math.Round(f * 32767f);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(i * 2, 2), s);
        }

        return bytes;
    }

    public static byte[] FloatToWav(ReadOnlySpan<float> samples)
    {
        var pcm = FloatToInt16Bytes(samples);
        var wav = new byte[44 + pcm.Length];
        WriteHeader(wav, pcm.Length);
        Buffer.BlockCopy(pcm, 0, wav, 44, pcm.Length);
        return wav;
    }

    private static void WriteHeader(Span<byte> dst, int pcmBytes)
    {
        var byteRate = SampleRate * Channels * BitsPerSample / 8;
        var blockAlign = (short) (Channels * BitsPerSample / 8);

        // RIFF header
        dst[0] = (byte) 'R'; dst[1] = (byte) 'I'; dst[2] = (byte) 'F'; dst[3] = (byte) 'F';
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(4, 4), 36 + pcmBytes);
        dst[8] = (byte) 'W'; dst[9] = (byte) 'A'; dst[10] = (byte) 'V'; dst[11] = (byte) 'E';

        // fmt chunk
        dst[12] = (byte) 'f'; dst[13] = (byte) 'm'; dst[14] = (byte) 't'; dst[15] = (byte) ' ';
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(20, 2), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(22, 2), (short) Channels);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(24, 4), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(34, 2), (short) BitsPerSample);

        // data chunk
        dst[36] = (byte) 'd'; dst[37] = (byte) 'a'; dst[38] = (byte) 't'; dst[39] = (byte) 'a';
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(40, 4), pcmBytes);
    }
}
