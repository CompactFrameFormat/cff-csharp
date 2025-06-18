using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CompactFrameFormat.Tests;

[TestFixture]
public class CompactFrameFormatIntegrationTests {
    private string _testDataDir = null!;
    private List<string> _frameFiles = null!;
    private string _streamFile = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Get the test data directory
        _testDataDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData");
        Assert.That(Directory.Exists(_testDataDir), $"Test data directory not found: {_testDataDir}");

        // Get all numbered frame files, sorted by number
        var framePattern = Path.Combine(_testDataDir, "[0-9][0-9]_*.bin");
        var frameFiles = Directory.GetFiles(_testDataDir, "*.bin")
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^\d{2}_.*\.bin$"))
            .ToList();

        frameFiles.Sort((a, b) => ExtractNumber(a).CompareTo(ExtractNumber(b)));
        _frameFiles = frameFiles;

        _streamFile = Path.Combine(_testDataDir, "stream.bin");

        Assert.That(_frameFiles.Count, Is.GreaterThan(0), "No numbered frame files found in test data");
        Assert.That(File.Exists(_streamFile), $"Stream file not found: {_streamFile}");
    }

    private static int ExtractNumber(string filename)
    {
        var basename = Path.GetFileName(filename);
        var match = Regex.Match(basename, @"^(\d+)_");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    [Test]
    public void FrameFiles_Exist_AndAreProperlyNamed()
    {
        // Verify files are properly named and numbered
        var expectedNumbers = new HashSet<int>();
        foreach (var frameFile in _frameFiles) {
            var basename = Path.GetFileName(frameFile);
            var match = Regex.Match(basename, @"^(\d+)_.*\.bin$");
            Assert.That(match.Success, $"Frame file {basename} doesn't match expected naming pattern");
            expectedNumbers.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        // Check for sequential numbering (allowing gaps)
        var minNum = expectedNumbers.Min();
        Assert.That(minNum, Is.GreaterThanOrEqualTo(1), "Frame numbering should start from 1 or higher");
        Assert.That(_frameFiles.Count, Is.EqualTo(expectedNumbers.Count), "Duplicate frame numbers detected");
    }

    [Test]
    public void AllFrameFiles_CanBeParsed_Successfully()
    {
        foreach (var frameFile in _frameFiles) {
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out var consumedBytes);

            Assert.That(result, Is.EqualTo(FrameParseResult.Success),
                $"Failed to parse {Path.GetFileName(frameFile)}: {result}");
            Assert.That(consumedBytes, Is.EqualTo(frameData.Length),
                $"Consumed bytes mismatch for {Path.GetFileName(frameFile)}");
        }
    }

    [Test]
    public void StreamFile_Exists_AndHasReasonableSize()
    {
        var fileInfo = new FileInfo(_streamFile);
        Assert.That(fileInfo.Length, Is.GreaterThan(0), "Stream file should not be empty");

        // Stream should be larger than any individual frame
        var individualSizes = _frameFiles.Select(f => new FileInfo(f).Length).ToList();
        var maxIndividualSize = individualSizes.Max();
        Assert.That(fileInfo.Length, Is.GreaterThanOrEqualTo(maxIndividualSize),
            "Stream should be at least as large as largest individual frame");
    }

    [Test]
    public void ParseStream_ExtractsExpectedPayloads()
    {
        // First, get expected payloads from individual frame files
        var expectedPayloads = new List<byte[]>();
        foreach (var frameFile in _frameFiles) {
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out _);
            Assert.That(result, Is.EqualTo(FrameParseResult.Success));
            expectedPayloads.Add(frame.Payload.ToArray());
        }

        // Parse all payloads from stream
        var streamData = File.ReadAllBytes(_streamFile);
        var parsedPayloads = new List<byte[]>();
        var remainingData = streamData.AsSpan();

        while (remainingData.Length > 0) {
            var frames = Cff.FindFrames(remainingData.ToArray(), out var consumedBytes).ToList();
            if (frames.Count == 0) {
                break;
            }

            // Process all frames found in this pass
            foreach (var frame in frames) {
                parsedPayloads.Add(frame.Payload.ToArray());
            }

            // Use consumedBytes to advance efficiently through processed data
            if (consumedBytes > 0) {
                remainingData = remainingData.Slice(Math.Min(consumedBytes, remainingData.Length));
            }
        }

        // Verify we got the expected number of payloads
        Assert.That(parsedPayloads.Count, Is.EqualTo(expectedPayloads.Count),
            $"Expected {expectedPayloads.Count} payloads, found {parsedPayloads.Count}");

        // Verify each payload matches expected
        for (int i = 0; i < expectedPayloads.Count; i++) {
            Assert.That(parsedPayloads[i], Is.EqualTo(expectedPayloads[i]),
                $"Payload {i + 1} mismatch");
        }
    }

    [Test]
    public void RecreateStream_FromPayloads_MatchesOriginal()
    {
        // Extract payloads from individual frame files
        var payloads = new List<byte[]>();
        var frameCounters = new List<ushort>();

        foreach (var frameFile in _frameFiles) {
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out _);
            Assert.That(result, Is.EqualTo(FrameParseResult.Success));
            payloads.Add(frame.Payload.ToArray());
            frameCounters.Add(frame.FrameCounter);
        }

        // Recreate stream using the original frame counters
        var recreatedStream = new List<byte>();
        for (int i = 0; i < payloads.Count; i++) {
            var frame = Cff.CreateFrame(payloads[i], frameCounters[i]);
            recreatedStream.AddRange(frame);
        }

        // Compare with original stream
        var originalStream = File.ReadAllBytes(_streamFile);
        var recreatedStreamArray = recreatedStream.ToArray();

        Assert.That(recreatedStreamArray.Length, Is.EqualTo(originalStream.Length),
            $"Recreated stream length {recreatedStreamArray.Length} doesn't match original {originalStream.Length}");
        Assert.That(recreatedStreamArray, Is.EqualTo(originalStream),
            "Recreated stream doesn't match original stream");
    }

    [Test]
    public void CompleteWorkflow_IndividualFiles_To_Stream_To_Payloads()
    {
        // Step 1: Load payloads from individual frame files
        var originalPayloads = new List<byte[]>();
        foreach (var frameFile in _frameFiles) {
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out _);
            Assert.That(result, Is.EqualTo(FrameParseResult.Success));
            originalPayloads.Add(frame.Payload.ToArray());
        }

        // Step 2: Create a stream from these payloads (using sequential frame counters)
        var createdStream = new List<byte>();
        ushort frameCounter = 0;
        foreach (var payload in originalPayloads) {
            var frame = Cff.CreateFrame(payload, frameCounter++);
            createdStream.AddRange(frame);
        }

        // Step 3: Parse the created stream back to payloads
        var parsedPayloads = new List<byte[]>();
        var remainingData = createdStream.ToArray().AsSpan();

        while (remainingData.Length > 0) {
            var frames = Cff.FindFrames(remainingData.ToArray(), out var consumedBytes).ToList();
            if (frames.Count == 0) {
                break;
            }

            // Process all frames found in this pass
            foreach (var frame in frames) {
                parsedPayloads.Add(frame.Payload.ToArray());
            }

            // Use consumedBytes to advance efficiently through processed data
            if (consumedBytes > 0) {
                remainingData = remainingData.Slice(Math.Min(consumedBytes, remainingData.Length));
            }
        }

        // Step 4: Verify the payloads match
        Assert.That(parsedPayloads.Count, Is.EqualTo(originalPayloads.Count),
            $"Payload count mismatch: {parsedPayloads.Count} vs {originalPayloads.Count}");

        for (int i = 0; i < originalPayloads.Count; i++) {
            Assert.That(parsedPayloads[i], Is.EqualTo(originalPayloads[i]),
                $"Payload {i + 1} mismatch in end-to-end workflow");
        }
    }

    [Test]
    public void FrameBoundaryDetection_InStreamData()
    {
        var streamData = File.ReadAllBytes(_streamFile);
        var framesFound = new List<(int Length, int PayloadSize, ushort FrameCounter)>();
        var remainingData = streamData.AsSpan();

        while (remainingData.Length > 0) {
            var frames = Cff.FindFrames(remainingData.ToArray(), out var consumedBytes).ToList();
            if (frames.Count == 0) {
                break;
            }

            // Process all frames found in this pass
            foreach (var frame in frames) {
                // Record frame info (position tracking is no longer available from FindFrames)
                var frameInfo = (
                    Length: frame.FrameSizeBytes,
                    PayloadSize: frame.PayloadSizeBytes,
                    FrameCounter: frame.FrameCounter
                );
                framesFound.Add(frameInfo);
            }

            // Use consumedBytes to advance efficiently through processed data
            if (consumedBytes > 0) {
                remainingData = remainingData.Slice(Math.Min(consumedBytes, remainingData.Length));
            }
        }

        // Verify we found frames and they have reasonable properties
        Assert.That(framesFound.Count, Is.GreaterThan(0), "Should find at least one frame in stream");

        // Verify all frames have positive lengths
        foreach (var (Length, PayloadSize, FrameCounter) in framesFound) {
            Assert.That(Length, Is.GreaterThan(0), "Frame length should be positive");
            Assert.That(PayloadSize, Is.GreaterThanOrEqualTo(0), "Payload size should be non-negative");
        }

        // Verify frame counters are valid and unique
        var frameCounters = framesFound.Select(f => f.FrameCounter).ToList();
        var uniqueCounters = new HashSet<ushort>(frameCounters);
        Assert.That(uniqueCounters.Count, Is.EqualTo(frameCounters.Count),
            "All frame counters should be unique");

        // Verify frame counters are within expected range (based on test data)
        foreach (var counter in frameCounters) {
            Assert.That(counter, Is.GreaterThanOrEqualTo(0), "Frame counter should be non-negative");
            Assert.That(counter, Is.LessThan(1000), "Frame counter should be reasonable");
        }
    }

    [Test]
    public void ErrorResilience_WithCorruptedData()
    {
        var streamData = File.ReadAllBytes(_streamFile);

        // Test with corrupted data at the beginning
        var corruptedStart = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
            .Concat(streamData).ToArray();

        var frames = Cff.FindFrames(corruptedStart, out var consumedBytes1).ToList();
        if (streamData.Length > 0) {
            // Should still find frames despite garbage at start
            Assert.That(frames.Count, Is.GreaterThan(0),
                "Should find frames despite garbage at start");
            // Verify that consumedBytes indicates data was processed
            Assert.That(consumedBytes1, Is.GreaterThan(0),
                "consumedBytes should indicate processed data");
        }

        // Test with corrupted data in the middle
        if (streamData.Length > 20) {
            var midPoint = streamData.Length / 2;
            var corruptedMiddle = streamData.Take(midPoint)
                .Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })
                .Concat(streamData.Skip(midPoint + 7))
                .ToArray();

            var corruptedFrames = Cff.FindFrames(corruptedMiddle, out var consumedBytes2).ToList();
            // Should find at least some frames even with corruption
            Assert.That(corruptedFrames.Count, Is.GreaterThan(0),
                "Should find at least one frame despite corruption");
            // Verify that consumedBytes indicates data was processed
            Assert.That(consumedBytes2, Is.GreaterThan(0),
                "consumedBytes should indicate processed data even with corruption");
        }
    }

    [Test]
    public void PayloadVariety_VerifyDifferentTypes()
    {
        var payloads = new List<byte[]>();
        foreach (var frameFile in _frameFiles) {
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out _);
            Assert.That(result, Is.EqualTo(FrameParseResult.Success));
            payloads.Add(frame.Payload.ToArray());
        }

        // Verify we have different payload types based on known test data
        var payloadLengths = payloads.Select(p => p.Length).ToList();
        var uniqueLengths = new HashSet<int>(payloadLengths);
        Assert.That(uniqueLengths.Count, Is.GreaterThan(1), "Expected payloads of different lengths");

        // Check for empty payload (should be first based on naming)
        Assert.That(payloads[0].Length, Is.EqualTo(0), "First payload should be empty based on file naming");

        // Verify we have some non-empty payloads
        Assert.That(payloads.Any(p => p.Length > 0), "Should have some non-empty payloads");

        // Verify we have some larger payloads
        Assert.That(payloads.Any(p => p.Length > 100), "Should have some larger payloads");
    }

    [Test]
    public void SystematicByteCorruption_ErrorRecovery()
    {
        // Read the original stream
        var byteStream = File.ReadAllBytes(_streamFile);
        var expectedFrameCount = _frameFiles.Count;
        
        // Test corruption at each byte position
        for (int corruptPos = 0; corruptPos < byteStream.Length; corruptPos++) {
            // Corrupt the byte at this position (flip all bits)
            byteStream[corruptPos] ^= 0xFF;
            var frames = Cff.FindFrames(byteStream, out _).ToList();

            // Corrupting any single byte should result in exactly one frame being corrupted,
            // so we should parse exactly (expectedFrameCount - 1) frames
            Assert.That(frames.Count, Is.EqualTo(expectedFrameCount - 1),
                $"Corrupting byte at position {corruptPos} should result in exactly {expectedFrameCount - 1} " +
                $"frames parsed, but got {frames.Count}");

            // Restore the corrupted byte
            byteStream[corruptPos] ^= 0xFF;
        }
    }
}