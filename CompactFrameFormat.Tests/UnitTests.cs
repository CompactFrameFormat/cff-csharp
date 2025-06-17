using System;
using System.Linq;
using NUnit.Framework;

namespace CompactFrameFormat.Tests;

[TestFixture]
public class UnitTests {
    [Test]
    public void CreateFrame_WithMaxSizePayload_ThrowsArgumentException()
    {
        // Test the max payload size exception path
        var oversizedPayload = new byte[Cff.MAX_PAYLOAD_SIZE_BYTES + 1];

        var ex = Assert.Throws<ArgumentException>(() => Cff.CreateFrame(oversizedPayload, 0));
        Assert.That(ex.ParamName, Is.EqualTo("payload"));
        Assert.That(ex.Message, Does.Contain($"Payload size {oversizedPayload.Length} exceeds maximum of {Cff.MAX_PAYLOAD_SIZE_BYTES}"));
    }

    [Test]
    public void CreateFrame_WithExactMaxSizePayload_Succeeds()
    {
        // Test that exactly max size payload works
        var maxSizePayload = new byte[Cff.MAX_PAYLOAD_SIZE_BYTES];
        for (int i = 0; i < maxSizePayload.Length; i++) {
            maxSizePayload[i] = (byte)(i % 256);
        }

        var frame = Cff.CreateFrame(maxSizePayload, 12345);
        Assert.That(frame.Length, Is.EqualTo(Cff.HEADER_SIZE_BYTES + Cff.MAX_PAYLOAD_SIZE_BYTES + Cff.PAYLOAD_CRC_SIZE_BYTES));
    }

    [Test]
    public void TryParseFrame_WithInvalidPreamble_ReturnsInvalidPreamble()
    {
        // Test invalid preamble handling
        var invalidFrame = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var result = Cff.TryParseFrame(invalidFrame, out var frame, out var consumedBytes);

        Assert.That(result, Is.EqualTo(FrameParseResult.InvalidPreamble));
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void TryParseFrame_WithInvalidHeaderCrc_ReturnsInvalidHeaderCrc()
    {
        // Create a frame with valid preamble but invalid header CRC
        var validFrame = Cff.CreateFrame("test"u8, 123);
        var corruptedFrame = new byte[validFrame.Length];
        Array.Copy(validFrame, corruptedFrame, validFrame.Length);

        // Corrupt the header CRC bytes (positions 6-7)
        corruptedFrame[6] = 0xFF;
        corruptedFrame[7] = 0xFF;

        var result = Cff.TryParseFrame(corruptedFrame, out var frame, out var consumedBytes);

        Assert.That(result, Is.EqualTo(FrameParseResult.InvalidHeaderCrc));
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void TryParseFrame_WithInvalidPayloadCrc_ReturnsInvalidPayloadCrc()
    {
        // Create a frame with valid header but invalid payload CRC
        var validFrame = Cff.CreateFrame("test"u8, 123);
        var corruptedFrame = new byte[validFrame.Length];
        Array.Copy(validFrame, corruptedFrame, validFrame.Length);

        // Corrupt the payload CRC bytes (last 2 bytes)
        corruptedFrame[^2] = 0xFF;
        corruptedFrame[^1] = 0xFF;

        var result = Cff.TryParseFrame(corruptedFrame, out var frame, out var consumedBytes);

        Assert.That(result, Is.EqualTo(FrameParseResult.InvalidPayloadCrc));
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void TryParseFrame_WithInsufficientDataForHeader_ReturnsInsufficientData()
    {
        // Test with data smaller than header size
        var incompleteData = new byte[Cff.HEADER_SIZE_BYTES - 1];

        var result = Cff.TryParseFrame(incompleteData, out var frame, out var consumedBytes);

        Assert.That(result, Is.EqualTo(FrameParseResult.InsufficientData));
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void TryParseFrame_WithInsufficientDataForPayload_ReturnsInsufficientData()
    {
        // Create a valid frame then truncate it before the payload ends
        var validFrame = Cff.CreateFrame("hello world"u8, 456);
        var truncatedFrame = new byte[validFrame.Length - 3]; // Remove last 3 bytes
        Array.Copy(validFrame, truncatedFrame, truncatedFrame.Length);

        var result = Cff.TryParseFrame(truncatedFrame, out var frame, out var consumedBytes);

        Assert.That(result, Is.EqualTo(FrameParseResult.InsufficientData));
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void FindFrames_WithEmptyData_ReturnsEmpty()
    {
        var result = Cff.FindFrames(Array.Empty<byte>(), out var consumedBytes).ToList();
        Assert.That(result, Is.Empty);
        Assert.That(consumedBytes, Is.EqualTo(0));
    }

    [Test]
    public void FindFrames_WithNoPreamble_ReturnsEmpty()
    {
        var dataWithoutPreamble = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var result = Cff.FindFrames(dataWithoutPreamble, out var consumedBytes).ToList();
        Assert.That(result, Is.Empty);
        // When no preamble is found, we can discard all but the last byte
        Assert.That(consumedBytes, Is.EqualTo(dataWithoutPreamble.Length - 1));
    }

    [Test]
    public void FindFrames_WithInvalidFrameAfterPreamble_SkipsAndContinues()
    {
        // Create data with preamble but invalid frame, followed by valid frame
        var validFrame = Cff.CreateFrame("test"u8, 100);
        var invalidData = new byte[] { Cff.PREAMBLE_BYTE_1, Cff.PREAMBLE_BYTE_2, 0xFF, 0xFF, 0xFF, 0xFF };
        var combinedData = invalidData.Concat(validFrame).ToArray();

        var result = Cff.FindFrames(combinedData, out var consumedBytes).ToList();

        // Should find the valid frame, skipping the invalid one
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].FrameCounter, Is.EqualTo(100));
        Assert.That(consumedBytes, Is.EqualTo(combinedData.Length));
    }

    [Test]
    public void CalculateCrc16_WithEmptyData_ReturnsCorrectValue()
    {
        var crc = Cff.CalculateCrc16(ReadOnlySpan<byte>.Empty);
        Assert.That(crc, Is.EqualTo(0xFFFF)); // CRC-16/CCITT-FALSE init value
    }

    [Test]
    public void CalculateCrc16_WithKnownData_ReturnsExpectedValue()
    {
        // Test with known data to verify CRC calculation
        var testData = "123456789"u8;
        var crc = Cff.CalculateCrc16(testData);
        Assert.That(crc, Is.EqualTo(0x29B1)); // Known CRC-16/CCITT-FALSE value for "123456789"
    }

    [Test]
    public void CFrame_Properties_ReturnCorrectValues()
    {
        var testPayload = "Hello, World!"u8;
        var frameBytes = Cff.CreateFrame(testPayload, 42);
        var result = Cff.TryParseFrame(frameBytes, out var frame, out _);

        Assert.That(result, Is.EqualTo(FrameParseResult.Success));
        Assert.That(frame.FrameCounter, Is.EqualTo(42));
        Assert.That(frame.Payload.ToArray(), Is.EqualTo(testPayload.ToArray()));
        Assert.That(frame.PayloadSizeBytes, Is.EqualTo(testPayload.Length));
        Assert.That(frame.FrameSizeBytes, Is.EqualTo(Cff.HEADER_SIZE_BYTES + testPayload.Length + Cff.PAYLOAD_CRC_SIZE_BYTES));
    }

    [Test]
    public void Constants_HaveExpectedValues()
    {
        // Test that constants have expected values
        Assert.That(Cff.PREAMBLE_BYTE_1, Is.EqualTo(0xFA));
        Assert.That(Cff.PREAMBLE_BYTE_2, Is.EqualTo(0xCE));
        Assert.That(Cff.HEADER_SIZE_BYTES, Is.EqualTo(8));
        Assert.That(Cff.PAYLOAD_CRC_SIZE_BYTES, Is.EqualTo(2));
        Assert.That(Cff.MAX_PAYLOAD_SIZE_BYTES, Is.EqualTo(65535));
        Assert.That(Cff.MIN_FRAME_SIZE_BYTES, Is.EqualTo(10));
    }

    [Test]
    public void FindFrames_ByteArrayOverload_WorksCorrectly()
    {
        // Test the byte[] overload of FindFrames
        var frame1 = Cff.CreateFrame("test1"u8, 1);
        var frame2 = Cff.CreateFrame("test2"u8, 2);
        var combinedData = frame1.Concat(frame2).ToArray();

        var result = Cff.FindFrames(combinedData, out var consumedBytes).ToList();

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].FrameCounter, Is.EqualTo(1));
        Assert.That(result[1].FrameCounter, Is.EqualTo(2));
        Assert.That(consumedBytes, Is.EqualTo(combinedData.Length));
    }

    [Test]
    public void CreateFrame_WithZeroLengthPayload_Succeeds()
    {
        var emptyPayload = ReadOnlySpan<byte>.Empty;
        var frame = Cff.CreateFrame(emptyPayload, 999);

        Assert.That(frame.Length, Is.EqualTo(Cff.HEADER_SIZE_BYTES + Cff.PAYLOAD_CRC_SIZE_BYTES));

        // Verify the frame can be parsed back
        var result = Cff.TryParseFrame(frame, out var parsedFrame, out var consumedBytes);
        Assert.That(result, Is.EqualTo(FrameParseResult.Success));
        Assert.That(parsedFrame.FrameCounter, Is.EqualTo(999));
        Assert.That(parsedFrame.PayloadSizeBytes, Is.EqualTo(0));
    }

    [Test]
    public void FindFrames_ConsumedBytes_ReflectsProcessedData()
    {
        // Create test data with valid frames and some garbage
        var frame1 = Cff.CreateFrame("test1"u8, 1);
        var frame2 = Cff.CreateFrame("test2"u8, 2);
        var garbageData = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        var combinedData = frame1.Concat(frame2).Concat(garbageData).ToArray();

        var frames = Cff.FindFrames(combinedData, out var consumedBytes).ToList();

        // Should find both frames
        Assert.That(frames.Count, Is.EqualTo(2));
        Assert.That(frames[0].FrameCounter, Is.EqualTo(1));
        Assert.That(frames[1].FrameCounter, Is.EqualTo(2));

        // consumedBytes should be the total length since all valid frames were processed
        // and the garbage at the end was searched through
        var expectedConsumed = frame1.Length + frame2.Length + garbageData.Length - 1;
        Assert.That(consumedBytes, Is.EqualTo(expectedConsumed),
            "consumedBytes should reflect all processed data except last byte");
    }
}