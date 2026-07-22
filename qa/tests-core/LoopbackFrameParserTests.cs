// ADR-0009 M2 — owner-local loopback frame parser + bind policy tests.
// Covers: malformed / oversized / partial / zero-length frame rejection, big-endian
// round-trip, and the 127.0.0.1-only + operator-token bind policy (AT-QA-LOOPBACK-ONLY
// transport half). Engine-free; link-compiles the shipped ControlPlane sources.
using System;
using System.Text;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class LoopbackFrameParserTests
    {
        private static readonly LoopbackFrameParser P = new();

        [Fact]
        public void CompleteFrame_RoundTrips()
        {
            byte[] frame = LoopbackFrameParser.EncodeFrame("{\"verb\":\"Ping\"}");
            var r = P.TryReadFrame(frame, 0, frame.Length);
            Assert.True(r.Ok);
            Assert.Equal("{\"verb\":\"Ping\"}", r.Payload);
            Assert.Equal(frame.Length, r.BytesConsumed);
        }

        [Fact]
        public void HeaderNotYetArrived_Partial()
        {
            var r = P.TryReadFrame(new byte[] { 0, 0 }, 0, 2);
            Assert.Equal(ControlPlaneReason.PartialFrame, r.Reason);
        }

        [Fact]
        public void PayloadNotYetComplete_Partial()
        {
            // Declares 10 bytes but only 3 present.
            byte[] buf = new byte[] { 0, 0, 0, 10, 1, 2, 3 };
            var r = P.TryReadFrame(buf, 0, buf.Length);
            Assert.Equal(ControlPlaneReason.PartialFrame, r.Reason);
        }

        [Fact]
        public void ZeroLength_Malformed()
        {
            byte[] buf = new byte[] { 0, 0, 0, 0 };
            Assert.Equal(ControlPlaneReason.MalformedFrame, P.TryReadFrame(buf, 0, buf.Length).Reason);
        }

        [Fact]
        public void OverCap_Oversized()
        {
            // Declared length = MaxPayloadBytes + 1, header only (we reject before needing the body).
            long declared = LoopbackFrameParser.MaxPayloadBytes + 1L;
            byte[] buf = new byte[]
            {
                (byte)((declared >> 24) & 0xFF), (byte)((declared >> 16) & 0xFF),
                (byte)((declared >> 8) & 0xFF), (byte)(declared & 0xFF),
            };
            Assert.Equal(ControlPlaneReason.OversizedFrame, P.TryReadFrame(buf, 0, buf.Length).Reason);
        }

        [Fact]
        public void NullBuffer_Malformed()
            => Assert.Equal(ControlPlaneReason.MalformedFrame, P.TryReadFrame(null, 0, 0).Reason);

        [Fact]
        public void ExactCap_Accepted()
        {
            string payload = new string('x', LoopbackFrameParser.MaxPayloadBytes);
            byte[] frame = LoopbackFrameParser.EncodeFrame(payload);
            var r = P.TryReadFrame(frame, 0, frame.Length);
            Assert.True(r.Ok);
            Assert.Equal(payload.Length, Encoding.UTF8.GetByteCount(r.Payload!));
        }

        [Fact]
        public void TrailingBytesAfterFrame_ConsumesOnlyOne()
        {
            byte[] one = LoopbackFrameParser.EncodeFrame("ab");
            byte[] two = new byte[one.Length + 3];
            Array.Copy(one, two, one.Length);
            var r = P.TryReadFrame(two, 0, two.Length);
            Assert.True(r.Ok);
            Assert.Equal(one.Length, r.BytesConsumed); // extra 3 bytes left for the next read
        }
    }

    public class LoopbackBindPolicyTests
    {
        private static readonly LoopbackBindPolicy Policy = new("op-token-secret");

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.0.0.1:49812", true)]
        [InlineData("::1", true)]
        [InlineData("[::1]", true)]
        [InlineData("10.0.0.5", false)]
        [InlineData("192.168.1.9:5000", false)]
        [InlineData("0.0.0.0", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsLoopback_Classifies(string? addr, bool expected)
            => Assert.Equal(expected, LoopbackBindPolicy.IsLoopback(addr));

        [Fact]
        public void Loopback_GoodToken_Admitted()
            => Assert.Equal(ControlPlaneReason.None, Policy.Admit("127.0.0.1", "op-token-secret"));

        [Fact]
        public void NonLoopback_RejectedBeforeToken()
            => Assert.Equal(ControlPlaneReason.NonLoopbackPeer, Policy.Admit("10.0.0.5", "op-token-secret"));

        [Fact]
        public void Loopback_BadToken_Rejected()
            => Assert.Equal(ControlPlaneReason.BadOperatorToken, Policy.Admit("127.0.0.1", "wrong"));

        [Fact]
        public void Loopback_NullToken_Rejected()
            => Assert.Equal(ControlPlaneReason.BadOperatorToken, Policy.Admit("127.0.0.1", null));

        [Fact]
        public void EmptyConfiguredToken_AlwaysRejects()
            => Assert.Equal(ControlPlaneReason.BadOperatorToken, new LoopbackBindPolicy("").Admit("127.0.0.1", ""));
    }
}
