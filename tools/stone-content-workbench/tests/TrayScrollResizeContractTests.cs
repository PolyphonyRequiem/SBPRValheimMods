using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Regression contract for the bottom-tray scroll + resize bug (card t_42b44e70).
    //
    // Daniel's hands-on POC correction: the bottom tray could not be scrolled or resized. The grounded
    // root cause was purely structural CSS — the fixed-height .bottom grid track held children
    // (.out, #output) that defaulted to min-height:auto, so long "Selected JSON" expanded them past the
    // track and overflow:auto on #output never engaged (the element was as tall as its own content).
    //
    // A live layout assertion needs a real browser engine (jsdom does not compute box sizes and this
    // repo ships no JS/Playwright toolchain — the suite is deliberately headless-C# for CI). So this
    // test guards the exact SHIPPED source invariants that are necessary and sufficient for the fix:
    //   1. #output (and its ancestor grid tracks) carry min-height:0, so a fixed tray height wins and
    //      the overflow viewport forms — the specific property whose absence caused the bug.
    //   2. #output keeps overflow:auto — the scroll viewport itself.
    //   3. .app parametrizes the tray track height via a CSS custom property (--tray-h) and inserts a
    //      resizer track, so the tray is resizable rather than a hard-coded 184px row.
    //   4. index.html carries a keyboard-accessible role=separator with aria-orientation=horizontal.
    //   5. app.js clamps the tray height (min/max) and handles ArrowUp/ArrowDown keyboard resize.
    // The manual browser-measured evidence (scrollHeight > clientHeight, scrollTop changes, drag/keys)
    // is captured in the PR; these assertions stop the CSS/HTML regression from silently returning.
    public sealed class TrayScrollResizeContractTests
    {
        private static string Frontend(string file) =>
            File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "Frontend", file));

        private static string Css => Frontend("styles.css");
        private static string Html => Frontend("index.html");
        private static string Js => Frontend("app.js");

        // The bug fix: the output viewport and its grid-track ancestors must be min-height:0 so the
        // fixed tray height constrains them and overflow can form. A stray min-height:auto here is the
        // exact regression that broke scrolling.
        [Theory]
        [InlineData(".output")]
        [InlineData(".out")]
        [InlineData(".bottom")]
        public void Tray_grid_chain_declares_min_height_zero(string selector)
        {
            var rule = RuleBody(Css, selector);
            Assert.Contains("min-height:0", rule);
        }

        [Fact]
        public void Output_viewport_keeps_overflow_auto_so_it_can_scroll()
        {
            Assert.Contains("overflow:auto", RuleBody(Css, ".output"));
        }

        // The tray height must be a resizable CSS custom property, not a hard-coded row, and a resizer
        // track must sit in the grid — this is what makes the tray vertically resizable at all.
        [Fact]
        public void App_grid_parametrizes_the_tray_height_and_reserves_a_resizer_track()
        {
            var app = RuleBody(Css, ".app");
            Assert.Contains("grid-template-rows", app);
            Assert.Contains("--tray-h", app);
            // The fixed 184px row must no longer be a bare grid track value.
            Assert.DoesNotContain("54px 1fr 184px", app);
            Assert.Contains(".tray-resizer{", Css.Replace(" ", ""));
        }

        // A keyboard-accessible separator between the main workspace and the tray.
        [Fact]
        public void Html_has_keyboard_accessible_horizontal_separator()
        {
            Assert.Matches(new Regex("role=\"separator\""), Html);
            Assert.Matches(new Regex("aria-orientation=\"horizontal\""), Html);
            Assert.Matches(new Regex("id=\"trayResizer\""), Html);
            // Focusable via keyboard.
            Assert.Matches(new Regex("tabindex=\"0\""), Html);
            // Current value exposed for assistive tech.
            Assert.Matches(new Regex("aria-valuenow="), Html);
        }

        // Pointer + keyboard resize with clamped bounds that can never collapse the tray or cover the
        // main workspace.
        [Fact]
        public void Resize_logic_clamps_bounds_and_handles_keyboard()
        {
            Assert.Contains("setTrayHeight", Js);
            Assert.Contains("ArrowUp", Js);
            Assert.Contains("ArrowDown", Js);
            // Bounds clamp: both a floor (Math.max ... TRAY_MIN) and a ceiling (Math.min ... trayMax()).
            Assert.Contains("Math.max(TRAY_MIN", Js);
            Assert.Contains("trayMax()", Js);
            // Default height preserved near 184px.
            Assert.Contains("TRAY_DEFAULT = 184", Js);
            // Pointer drag path.
            Assert.Contains("pointerdown", Js);
        }

        // Extract and concatenate all CSS rule bodies { ... } for a selector from the (minified)
        // stylesheet — a selector may be split across a base rule and a later override.
        private static string RuleBody(string css, string selector)
        {
            var matches = Regex.Matches(css, Regex.Escape(selector) + @"\{([^}]*)\}");
            Assert.True(matches.Count > 0, $"CSS rule for '{selector}' not found");
            var sb = new System.Text.StringBuilder();
            foreach (Match m in matches) sb.Append(m.Groups[1].Value).Append(';');
            return sb.ToString();
        }
    }
}
