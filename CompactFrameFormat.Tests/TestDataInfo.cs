using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CompactFrameFormat.Tests;

[TestFixture]
public class TestDataInfoTests {
    [Test]
    public void DisplayTestDataInfo()
    {
        var testDataDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData");

        // Get all numbered frame files, sorted by number
        var frameFiles = Directory.GetFiles(testDataDir, "*.bin")
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^\d{2}_.*\.bin$"))
            .ToList();

        frameFiles.Sort((a, b) => ExtractNumber(a).CompareTo(ExtractNumber(b)));

        TestContext.Out.WriteLine($"Found {frameFiles.Count} test frame files:");
        TestContext.Out.WriteLine("=================================================");

        foreach (var frameFile in frameFiles) {
            var fileName = Path.GetFileName(frameFile);
            var fileSize = new FileInfo(frameFile).Length;

            // Parse the frame to get payload information
            var frameData = File.ReadAllBytes(frameFile);
            var result = Cff.TryParseFrame(frameData, out var frame, out var consumedBytes);

            if (result == FrameParseResult.Success) {
                var payloadPreview = GetPayloadPreview(frame.Payload.ToArray());
                TestContext.Out.WriteLine($"File: {fileName,-25} Size: {fileSize,4} bytes  " +
                                    $"Counter: {frame.FrameCounter,5}  Payload: {frame.PayloadSizeBytes,4} bytes  " +
                                    $"Preview: {payloadPreview}");
            }
            else {
                TestContext.Out.WriteLine($"File: {fileName,-25} Size: {fileSize,4} bytes  " +
                                    $"ERROR: {result}");
            }
        }

        // Test stream file
        var streamFile = Path.Combine(testDataDir, "stream.bin");
        if (File.Exists(streamFile)) {
            var streamSize = new FileInfo(streamFile).Length;
            TestContext.Out.WriteLine("=================================================");
            TestContext.Out.WriteLine($"Stream file: stream.bin       Size: {streamSize,4} bytes");

            // Count frames in stream
            var streamData = File.ReadAllBytes(streamFile);
            var frameCount = Cff.FindFrames(streamData, out var consumedBytes).Count();
            TestContext.Out.WriteLine($"Frames in stream: {frameCount}");
        }

        Assert.Pass($"Successfully displayed information for {frameFiles.Count} test data files");
    }

    private static int ExtractNumber(string filename)
    {
        var basename = Path.GetFileName(filename);
        var match = Regex.Match(basename, @"^(\d+)_");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static string GetPayloadPreview(byte[] payload)
    {
        if (payload.Length == 0) {
            return "[empty]";
        }

        if (payload.Length <= 20) {
            // Try to display as text if it's printable
            if (payload.All(b => b >= 32 && b <= 126)) {
                return $"\"{System.Text.Encoding.ASCII.GetString(payload)}\"";
            }
            else {
                return $"[{string.Join(" ", payload.Select(b => b.ToString("X2")))}]";
            }
        }
        else {
            // For larger payloads, show first few bytes and length
            var preview = string.Join(" ", payload.Take(8).Select(b => b.ToString("X2")));
            return $"[{preview}...] ({payload.Length} bytes)";
        }
    }
}