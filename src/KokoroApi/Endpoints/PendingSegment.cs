namespace KokoroApi.Endpoints;

internal readonly record struct PendingSegment(int Id, string Text, string Voice, float Speed);
