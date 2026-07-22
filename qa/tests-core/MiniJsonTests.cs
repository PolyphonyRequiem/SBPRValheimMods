// ADR-0009 M2R — MiniJson strict-parser tests. Proves the bounded control-envelope JSON
// shape parses correctly and that malformed / over-nested / trailing-garbage / oversized
// inputs fail CLOSED (never throw, return false).
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class MiniJsonTests
    {
        [Fact]
        public void ParsesFlatObjectWithScalars()
        {
            Assert.True(MiniJson.TryParse("{\"a\":\"x\",\"b\":12,\"c\":1.5,\"d\":true,\"e\":null}", out var o));
            Assert.True(o.TryGetString("a", out var a));
            Assert.Equal("x", a);
            Assert.True(o.TryGetLong("b", out var b));
            Assert.Equal(12, b);
        }

        [Fact]
        public void ParsesOneNestedArgsObject()
        {
            Assert.True(MiniJson.TryParse("{\"verb\":\"Ping\",\"args\":{\"qty\":3,\"name\":\"Wood\"}}", out var o));
            Assert.True(o.TryGetObject("args", out var args));
            Assert.True(args.TryGetLong("qty", out var q));
            Assert.Equal(3, q);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\":1,}")]
        [InlineData("{\"a\":1} trailing")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"a\":{\"b\":{\"c\":1}}}")] // nesting beyond one level
        [InlineData("{\"a\":\"unterminated}")]
        [InlineData("{\"a\":1,\"a\":2}")]        // duplicate key
        public void RejectsMalformed(string input)
        {
            Assert.False(MiniJson.TryParse(input, out _));
        }

        [Fact]
        public void RejectsOversizedInput()
        {
            string big = "{\"a\":\"" + new string('x', MiniJson.MaxInputChars) + "\"}";
            Assert.False(MiniJson.TryParse(big, out _));
        }

        [Fact]
        public void NeverThrowsOnRandomBytes()
        {
            Assert.False(MiniJson.TryParse("\u0001\u0002\u0003", out _));
            Assert.False(MiniJson.TryParse("{\"a\":\u0001}", out _));
        }
    }
}
