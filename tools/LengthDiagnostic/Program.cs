using System;
using System.IO;
using System.Threading;
using LibVLCSharp.Shared;

var path = args.Length > 0 ? args[0] : @"C:\Users\75943\Desktop\bandicam 2026-10-21 22-55-01-258.mp4";
Console.WriteLine($"Testing video: {path}");
if (!File.Exists(path))
{
    Console.WriteLine("File not found!");
    return;
}

using var libVlc = new LibVLC(enableDebugLogs: false);
Console.WriteLine($"LibVLC version: {libVlc.Version}");

using var media = new Media(libVlc, new Uri(path));
Console.WriteLine($"Media.Duration (before Play): {media.Duration} ms");

using var player = new MediaPlayer(media);
Console.WriteLine($"MediaPlayer.Length (before Play): {player.Length} ms, State={player.State}");

player.Play();
Console.WriteLine($"MediaPlayer.Length (immediately after Play): {player.Length} ms, State={player.State}");

for (var i = 0; i < 20; i++)
{
    Thread.Sleep(300);
    var len = player.Length;
    var state = player.State;
    Console.WriteLine($"[{i*300}ms] Length={len} ms ({TimeSpan.FromMilliseconds(len):mm\\:ss}), State={state}, Media.Duration={media.Duration} ms");
    if (len > 0 && state == VLCState.Playing) break;
}

player.Stop();
