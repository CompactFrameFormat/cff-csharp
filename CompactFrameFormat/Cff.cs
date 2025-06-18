using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace CompactFrameFormat {
    /// <summary>
    /// Represents the result of frame parsing operations
    /// </summary>
    public enum FrameParseResult {
        Success,
        InsufficientData,
        InvalidPreamble,
        InvalidHeaderCrc,
        InvalidPayloadCrc
    }

    /// <summary>
    /// Represents a parsed CFF frame
    /// </summary>
    public readonly struct CFrame {
        /// <summary>
        /// The frame counter value
        /// </summary>
        public ushort FrameCounter { get; }

        /// <summary>
        /// The payload data
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>
        /// The size of the payload in bytes
        /// </summary>
        public int PayloadSizeBytes => Payload.Length;

        /// <summary>
        /// The total size of the frame in bytes (header + payload + payload CRC)
        /// </summary>
        public int FrameSizeBytes => Cff.HEADER_SIZE_BYTES + Payload.Length + Cff.PAYLOAD_CRC_SIZE_BYTES;

        internal CFrame(ushort frameCounter, ReadOnlyMemory<byte> payload)
        {
            FrameCounter = frameCounter;
            Payload = payload;
        }
    }

    /// <summary>
    /// Compact Frame Format implementation for creating, parsing, and finding frames in byte streams.
    /// </summary>
    public static class Cff {
        /// <summary>
        /// Frame preamble bytes: [0xFA, 0xCE]
        /// </summary>
        public const byte PREAMBLE_BYTE_1 = 0xFA;
        public const byte PREAMBLE_BYTE_2 = 0xCE;

        /// <summary>
        /// Size of the frame header in bytes
        /// </summary>
        public const int HEADER_SIZE_BYTES = 8;

        /// <summary>
        /// Size of the payload CRC in bytes
        /// </summary>
        public const int PAYLOAD_CRC_SIZE_BYTES = 2;

        /// <summary>
        /// Maximum payload size in bytes
        /// </summary>
        public const int MAX_PAYLOAD_SIZE_BYTES = ushort.MaxValue;

        /// <summary>
        /// Minimum frame size
        /// </summary>
        public const int MIN_FRAME_SIZE_BYTES = HEADER_SIZE_BYTES + PAYLOAD_CRC_SIZE_BYTES;

        /// <summary>
        /// CRC-16/CCITT-FALSE lookup table for fast CRC calculation
        /// </summary>
        private static readonly ushort[] Crc16Table = GenerateCrc16Table();

        /// <summary>
        /// Generates the CRC-16/CCITT-FALSE lookup table
        /// </summary>
        private static ushort[] GenerateCrc16Table()
        {
            const ushort polynomial = 0x1021;
            var table = new ushort[256];

            for (int i = 0; i < 256; i++) {
                ushort crc = (ushort)(i << 8);
                for (int j = 0; j < 8; j++) {
                    if ((crc & 0x8000) != 0) {
                        crc = (ushort)((crc << 1) ^ polynomial);
                    }
                    else {
                        crc = (ushort)(crc << 1);
                    }
                }
                table[i] = crc;
            }

            return table;
        }

        /// <summary>
        /// Calculates CRC-16/CCITT-FALSE for the given data
        /// </summary>
        /// <param name="data">Data to calculate CRC for</param>
        /// <returns>CRC-16 value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort CalculateCrc16(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF; // Init value for CRC-16/CCITT-FALSE

            foreach (byte b in data) {
                byte tableIndex = (byte)((crc >> 8) ^ b);
                crc = (ushort)((crc << 8) ^ Crc16Table[tableIndex]);
            }

            return crc; // XorOut is 0x0000, so no final XOR needed
        }

        /// <summary>
        /// Creates a frame with the given payload and frame counter
        /// </summary>
        /// <param name="payload">The payload data</param>
        /// <param name="frameCounter">The frame counter value</param>
        /// <returns>The complete frame as a byte array</returns>
        /// <exception cref="ArgumentException">Thrown when payload is too large</exception>
        public static byte[] CreateFrame(ReadOnlySpan<byte> payload, ushort frameCounter)
        {
            if (payload.Length > MAX_PAYLOAD_SIZE_BYTES) {
                throw new ArgumentException($"Payload size {payload.Length} exceeds maximum of {MAX_PAYLOAD_SIZE_BYTES}", nameof(payload));
            }

            var payloadSize = (ushort)payload.Length;
            var frame = new byte[HEADER_SIZE_BYTES + payload.Length + PAYLOAD_CRC_SIZE_BYTES];

            // Write header
            WriteHeader(frame.AsSpan(), frameCounter, payloadSize);

            // Copy payload
            if (payload.Length > 0) {
                payload.CopyTo(frame.AsSpan(HEADER_SIZE_BYTES));
            }

            // Calculate and write payload CRC
            var payloadCrc = CalculateCrc16(payload);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(HEADER_SIZE_BYTES + payload.Length), payloadCrc);

            return frame;
        }

        /// <summary>
        /// Writes the frame header to the given span
        /// </summary>
        private static void WriteHeader(Span<byte> headerSpan, ushort frameCounter, ushort payloadSize)
        {
            // Write preamble
            headerSpan[0] = PREAMBLE_BYTE_1;
            headerSpan[1] = PREAMBLE_BYTE_2;

            // Write frame counter
            BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(2), frameCounter);

            // Write payload size
            BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(4), payloadSize);

            // Calculate and write header CRC (over preamble, frame counter, and payload size)
            var headerCrc = CalculateCrc16(headerSpan.Slice(0, 6));
            BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(6), headerCrc);
        }

        /// <summary>
        /// Attempt to parse a frame from the given data
        /// </summary>
        /// <param name="data">The data to parse</param>
        /// <param name="frame">The parsed frame if successful</param>
        /// <param name="consumedBytes">Number of bytes consumed from the input</param>
        /// <returns>The result of the parsing operation</returns>
        public static FrameParseResult TryParseFrame(ReadOnlySpan<byte> data, out CFrame frame, out int consumedBytes)
        {
            frame = default;
            consumedBytes = 0;

            // Check if we have enough data for a complete header
            if (data.Length < HEADER_SIZE_BYTES) {
                return FrameParseResult.InsufficientData;
            }

            // Validate preamble
            if (data[0] != PREAMBLE_BYTE_1 || data[1] != PREAMBLE_BYTE_2) {
                return FrameParseResult.InvalidPreamble;
            }

            // Read frame counter and payload size
            var frameCounter = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2));
            var payloadSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4));

            // Validate header CRC
            var expectedHeaderCrc = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6));
            var actualHeaderCrc = CalculateCrc16(data.Slice(0, 6));
            if (expectedHeaderCrc != actualHeaderCrc) {
                return FrameParseResult.InvalidHeaderCrc;
            }

            // Check if we have enough data for the complete frame
            var totalFrameSize = HEADER_SIZE_BYTES + payloadSize + PAYLOAD_CRC_SIZE_BYTES;
            if (data.Length < totalFrameSize) {
                return FrameParseResult.InsufficientData;
            }

            // Extract payload
            var payloadSpan = data.Slice(HEADER_SIZE_BYTES, payloadSize);

            // Validate payload CRC
            var expectedPayloadCrc = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(HEADER_SIZE_BYTES + payloadSize));
            var actualPayloadCrc = CalculateCrc16(payloadSpan);
            if (expectedPayloadCrc != actualPayloadCrc) {
                return FrameParseResult.InvalidPayloadCrc;
            }

            // Success - create frame
            frame = new CFrame(frameCounter, payloadSpan.ToArray());
            consumedBytes = totalFrameSize;
            return FrameParseResult.Success;
        }

        /// <summary>
        /// Finds all valid frames in the given data buffer
        /// </summary>
        /// <param name="data">The data buffer to search</param>
        /// <param name="consumedBytes">The number of bytes from the start of the buffer that can be safely discarded because they are guaranteed not to contain a valid frame</param>
        /// <returns>An enumerable of successfully parsed frames</returns>
        public static IEnumerable<CFrame> FindFrames(byte[] data, out int consumedBytes)
        {
            return FindFrames(data.AsMemory(), out consumedBytes);
        }

        /// <summary>
        /// Finds all valid frames in the given data buffer
        /// </summary>
        /// <param name="data">The data buffer to search</param>
        /// <param name="consumedBytes">The number of bytes from the start of the buffer that can be safely discarded because they are guaranteed not to contain a valid frame</param>
        /// <returns>An enumerable of successfully parsed frames</returns>
        public static IEnumerable<CFrame> FindFrames(ReadOnlyMemory<byte> data, out int consumedBytes)
        {
            var results = new List<CFrame>();
            int position = 0;
            var dataSpan = data.Span;

            while (position < dataSpan.Length) {
                // Look for preamble
                var preambleIndex = FindPreamble(dataSpan.Slice(position));
                if (preambleIndex == -1) {
                    // No preamble found in remaining data
                    // We can safely discard all but the last byte (which could be start of preamble)
                    position = Math.Max(0, dataSpan.Length - 1);
                    break;
                }

                position += preambleIndex;

                // Try to parse frame at this position
                var result = TryParseFrame(dataSpan.Slice(position), out var frame, out var frameConsumedBytes);
                if (result == FrameParseResult.Success) {
                    results.Add(frame);
                    position += frameConsumedBytes;
                }
                else if (result == FrameParseResult.InsufficientData) {
                    // Not enough data for a complete frame, stop searching
                    break;
                }
                else if (result == FrameParseResult.InvalidHeaderCrc) {
                    // Header CRC is invalid, so we can't trust the payload size
                    // Skip past the preamble and continue searching
                    position += 2;
                }
                else if (result == FrameParseResult.InvalidPayloadCrc) {
                    // Header was valid (including CRC), so we can trust the payload size
                    // Skip the entire frame based on the header information
                    if (position + HEADER_SIZE_BYTES <= dataSpan.Length) {
                        var payloadSize = BinaryPrimitives.ReadUInt16LittleEndian(dataSpan.Slice(position + 4));
                        var totalFrameSize = HEADER_SIZE_BYTES + payloadSize + PAYLOAD_CRC_SIZE_BYTES;
                        position += Math.Min(totalFrameSize, dataSpan.Length - position);
                    } else {
                        // Not enough data to read payload size, skip past preamble
                        position += 2;
                    }
                }
                else {
                    // Other failure (e.g., InvalidPreamble - shouldn't happen since we found it)
                    // Skip past the preamble and continue searching
                    position += 2;
                }
            }

            // Set consumedBytes to the current search position
            // This represents bytes that can be safely discarded from the input buffer
            consumedBytes = position;
            return results;
        }

        /// <summary>
        /// Finds the next occurrence of the preamble in the data
        /// </summary>
        /// <param name="data">The data to search</param>
        /// <returns>The index of the preamble, or -1 if not found</returns>
        private static int FindPreamble(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i <= data.Length - 2; i++) {
                if (data[i] == PREAMBLE_BYTE_1 && data[i + 1] == PREAMBLE_BYTE_2) {
                    return i;
                }
            }
            return -1;
        }
    }
}