
using MobilityTicketing;

Console.WriteLine("Hello, World!");

string city = "CPH";
string fromStop = "STOP-NORREPORT";
string toStop = "STOP-AIRPORT";
DateTimeOffset start = new DateTimeOffset(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
DateTimeOffset end = new DateTimeOffset(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);

string key = RedisKeyBuilder.BuildSearchKey(
    city,
    fromStop,
    toStop,
    start,
    end);

Console.WriteLine($"Redis key: {key}");

Console.ReadKey();