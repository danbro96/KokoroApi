using System.Text;

namespace KokoroApi.Streaming;

public sealed class TextSegmenter
{
    static readonly char[] HardTerminators = ['.', '!', '?', ';', '\n', '—'];

    readonly StringBuilder _buffer = new();
    readonly int _minSegmentChars;
    readonly int _maxBufferChars;

    public TextSegmenter(int minSegmentChars = 30, int maxBufferChars = 400)
    {
        _minSegmentChars = minSegmentChars;
        _maxBufferChars = maxBufferChars;
    }

    public IReadOnlyList<string> Append(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return Array.Empty<string>();
        _buffer.Append(delta);
        return ExtractSegments(forceTail: _buffer.Length >= _maxBufferChars);
    }

    public IReadOnlyList<string> Flush()
    {
        var leading = ExtractSegments(forceTail: false);
        if (_buffer.Length == 0) return leading;
        var tail = _buffer.ToString().Trim();
        _buffer.Clear();
        if (tail.Length == 0) return leading;
        var combined = new List<string>(leading.Count + 1);
        combined.AddRange(leading);
        combined.Add(tail);
        return combined;
    }

    public void Reset() => _buffer.Clear();

    List<string> ExtractSegments(bool forceTail)
    {
        var result = new List<string>();
        var s = _buffer.ToString();
        var i = 0;
        while (i < s.Length)
        {
            var cut = -1;
            for (var j = i; j < s.Length; j++)
            {
                var c = s[j];
                if (Array.IndexOf(HardTerminators, c) >= 0)
                {
                    cut = j + 1;
                    break;
                }
                if (c == ',' && (j - i) + 1 >= _minSegmentChars)
                {
                    cut = j + 1;
                    break;
                }
            }
            if (cut < 0) break;
            var segment = s[i..cut].Trim();
            if (segment.Length > 0) result.Add(segment);
            i = cut;
        }

        var remaining = s[i..];
        _buffer.Clear();
        if (forceTail && remaining.Trim().Length > 0)
        {
            result.Add(remaining.Trim());
        }
        else
        {
            _buffer.Append(remaining);
        }
        return result;
    }
}
