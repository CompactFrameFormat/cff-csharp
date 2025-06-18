using System;
using System.Text;
using CompactFrameFormat;

// Create and send frames
var messages = new[] { "Hello", "World", "CFF" };
var buffer = new byte[1024];
int bufferPos = 0;

Console.WriteLine("Creating frames:");
for (ushort i = 0; i < messages.Length; i++)
{
    var payload = Encoding.UTF8.GetBytes(messages[i]);
    var frame = Cff.CreateFrame(payload, i);

    Console.WriteLine($"  Frame {i}: {messages[i]} -> {frame.Length} bytes");

    // Simulate adding to receive buffer
    Array.Copy(frame, 0, buffer, bufferPos, frame.Length);
    bufferPos += frame.Length;
}

Console.WriteLine($"\nBuffer contains {bufferPos} bytes total");

// Parse all frames from buffer
var receivedFrames = Cff.FindFrames(buffer.AsMemory(0, bufferPos), out var consumedBytes);

Console.WriteLine($"\nProcessed {consumedBytes} bytes from buffer");
Console.WriteLine("Received frames:");
foreach (var frame in receivedFrames)
{
    var message = Encoding.UTF8.GetString(frame.Payload.Span);
    Console.WriteLine($"  Frame {frame.FrameCounter}: {message}");
}