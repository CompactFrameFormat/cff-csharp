using System;
using System.Text;
using CompactFrameFormat;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== Compact Frame Format Usage Examples ===\n");

        // Example 1: Creating Frames
        Console.WriteLine("1. Creating Frames:");
        Console.WriteLine("------------------");
        CreateFrameExample();
        Console.WriteLine();

        // Example 2: Parsing Single Frames
        Console.WriteLine("2. Parsing Single Frames:");
        Console.WriteLine("-------------------------");
        ParseSingleFrameExample();
        Console.WriteLine();

        // Example 3: Finding Multiple Frames
        Console.WriteLine("3. Finding Multiple Frames:");
        Console.WriteLine("---------------------------");
        FindMultipleFramesExample();
        Console.WriteLine();

        // Example 4: Complete Example
        Console.WriteLine("4. Complete Example:");
        Console.WriteLine("-------------------");
        CompleteExample();
        Console.WriteLine();

        // Example 5: Error Handling Demo
        Console.WriteLine("5. Error Handling Demo:");
        Console.WriteLine("----------------------");
        ErrorHandlingDemo();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static void CreateFrameExample()
    {
// Create a frame with string payload
var payload = Encoding.UTF8.GetBytes("Hello, World!");
ushort frameCounter = 42;
byte[] frame = Cff.CreateFrame(payload, frameCounter);

// Frame now contains the complete CFF frame ready for transmission
Console.WriteLine($"Created frame of {frame.Length} bytes");
Console.WriteLine($"Frame bytes: {Convert.ToHexString(frame)}");
    }

    static void ParseSingleFrameExample()
    {
        // First create a frame to parse
        var payload = Encoding.UTF8.GetBytes("Test Message");
        ushort frameCounter = 123;
        byte[] receivedData = Cff.CreateFrame(payload, frameCounter);

        var result = Cff.TryParseFrame(receivedData, out CFrame parsedFrame, out int consumedBytes);

        switch (result)
        {
            case FrameParseResult.Success:
                Console.WriteLine($"Frame counter: {parsedFrame.FrameCounter}");
                Console.WriteLine($"Payload: {Encoding.UTF8.GetString(parsedFrame.Payload.Span)}");
                Console.WriteLine($"Consumed {consumedBytes} bytes");
                break;

            case FrameParseResult.InsufficientData:
                Console.WriteLine("Need more data to complete frame");
                break;

            case FrameParseResult.InvalidPreamble:
                Console.WriteLine("Invalid frame preamble");
                break;

            case FrameParseResult.InvalidHeaderCrc:
                Console.WriteLine("Header CRC validation failed");
                break;

            case FrameParseResult.InvalidPayloadCrc:
                Console.WriteLine("Payload CRC validation failed");
                break;
        }
    }

    static void FindMultipleFramesExample()
    {
        // Create multiple frames and combine them into a single buffer
        var messages = new[] { "Frame 1", "Frame 2", "Frame 3" };
        var buffer = new List<byte>();

        // Create frames and add them to buffer
        for (ushort i = 0; i < messages.Length; i++)
        {
            var payload = Encoding.UTF8.GetBytes(messages[i]);
            var frame = Cff.CreateFrame(payload, i);
            buffer.AddRange(frame);
        }

        // Add some noise/corrupted data
        buffer.AddRange(new byte[] { 0x00, 0x11, 0x22, 0x33 });

        byte[] dataBuffer = buffer.ToArray();
        Console.WriteLine($"Buffer contains {dataBuffer.Length} bytes total");

        var frames = Cff.FindFrames(dataBuffer, out var consumedBytes);

        Console.WriteLine($"Processed {consumedBytes} bytes, found {frames.Count()} frames");

        foreach (var frame in frames)
        {
            Console.WriteLine($"Found frame:");
            Console.WriteLine($"  Frame counter: {frame.FrameCounter}");
            Console.WriteLine($"  Payload size: {frame.PayloadSizeBytes} bytes");
            Console.WriteLine($"  Frame size: {frame.FrameSizeBytes} bytes");

            // Process payload
            var payloadText = Encoding.UTF8.GetString(frame.Payload.Span);
            Console.WriteLine($"  Payload: {payloadText}");
        }
    }

    static void CompleteExample()
    {
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
    }

    static void ErrorHandlingDemo()
    {
        Console.WriteLine("Testing various error conditions:");

        // Test insufficient data
        var incompleteData = new byte[] { 0xFA, 0xCE, 0x01 }; // Only partial header
        var result = Cff.TryParseFrame(incompleteData, out _, out _);
        Console.WriteLine($"Incomplete data result: {result}");

        // Test invalid preamble
        var invalidPreamble = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x00 };
        result = Cff.TryParseFrame(invalidPreamble, out _, out _);
        Console.WriteLine($"Invalid preamble result: {result}");

        // Test corrupted header CRC
        var corruptedHeader = new byte[] { 0xFA, 0xCE, 0x01, 0x00, 0x05, 0x00, 0xFF, 0xFF, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x00 };
        result = Cff.TryParseFrame(corruptedHeader, out _, out _);
        Console.WriteLine($"Corrupted header CRC result: {result}");

        // Test corrupted payload CRC
        var validFrame = Cff.CreateFrame(Encoding.UTF8.GetBytes("Hello"), 1);
        validFrame[validFrame.Length - 1] = 0xFF; // Corrupt the last byte (payload CRC)
        result = Cff.TryParseFrame(validFrame, out _, out _);
        Console.WriteLine($"Corrupted payload CRC result: {result}");

        // Test maximum payload size
        try
        {
            var largePayload = new byte[Cff.MAX_PAYLOAD_SIZE_BYTES + 1];
            Cff.CreateFrame(largePayload, 0);
            Console.WriteLine("Large payload: No exception thrown (unexpected)");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Large payload: {ex.Message}");
        }
    }
}
